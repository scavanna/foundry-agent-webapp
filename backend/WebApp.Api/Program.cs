using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.Cosmos;
using Microsoft.Identity.Web;
using WebApp.Api.Models;
using WebApp.Api.Services;
using System.Security.Claims;

// Load .env file for local development BEFORE building the configuration
// In production (Docker), Container Apps injects environment variables directly
var envFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envFilePath))
{
    foreach (var line in File.ReadAllLines(envFilePath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            continue;

        var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            // Set as environment variables so they're picked up by configuration system
            Environment.SetEnvironmentVariable(parts[0], parts[1]);
        }
    }
}

var builder = WebApplication.CreateBuilder(args);

// Enable PII logging for debugging auth issues (ONLY IN DEVELOPMENT)
if (builder.Environment.IsDevelopment())
{
    Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
}

// Add ServiceDefaults (telemetry, health checks)
builder.AddServiceDefaults();

// Add ProblemDetails service for standardized RFC 7807 error responses
builder.Services.AddProblemDetails();

// Register IHttpContextAccessor for services that need access to the current HTTP request
builder.Services.AddHttpContextAccessor();

// Configure CORS for local development and production
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:8080" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        // In development, allow any localhost port for flexibility
        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(origin => 
            {
                if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return uri.Host == "localhost" || uri.Host == "127.0.0.1";
                }
                return false;
            })
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

// Override ClientId and TenantId from environment variables if provided
// These will be set by azd during deployment or by AppHost in local dev
var clientId = builder.Configuration["ENTRA_SPA_CLIENT_ID"]
    ?? builder.Configuration["AzureAd:ClientId"];

if (!string.IsNullOrEmpty(clientId))
{
    builder.Configuration["AzureAd:ClientId"] = clientId;
    // Set audience to match the expected token audience claim
    builder.Configuration["AzureAd:Audience"] = $"api://{clientId}";
}

var tenantId = builder.Configuration["ENTRA_TENANT_ID"]
    ?? builder.Configuration["AzureAd:TenantId"];

if (!string.IsNullOrEmpty(tenantId))
{
    builder.Configuration["AzureAd:TenantId"] = tenantId;
}

const string RequiredScope = "Chat.ReadWrite";
const string ScopePolicyName = "RequireChatScope";

// Add Microsoft Identity Web authentication
// Validates JWT bearer tokens issued for the SPA's delegated scope
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(options =>
    {
        builder.Configuration.Bind("AzureAd", options);
        var configuredClientId = builder.Configuration["AzureAd:ClientId"];
        var backendClientId = builder.Configuration["ENTRA_BACKEND_CLIENT_ID"];

        // When OBO is enabled, tokens are scoped to the backend API app
        var audiences = new List<string> { configuredClientId!, $"api://{configuredClientId}" };
        if (!string.IsNullOrEmpty(backendClientId))
        {
            audiences.Add(backendClientId);
            audiences.Add($"api://{backendClientId}");
        }
        options.TokenValidationParameters.ValidAudiences = audiences;

        options.TokenValidationParameters.NameClaimType = ClaimTypes.Name;
        options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
    }, options => builder.Configuration.Bind("AzureAd", options));

builder.Services.AddAuthorization(options =>
{
    // Use Microsoft.Identity.Web's built-in scope validation
    options.AddPolicy(ScopePolicyName, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireScope(RequiredScope);
    });
});

// Register Foundry Agent Service (v2 Agents API)
// Uses Azure.AI.Projects SDK which works with v2 Agents API (/agents/ endpoint with human-readable IDs).
builder.Services.AddHttpClient();
builder.Services.AddScoped<AgentFrameworkService>();
builder.Services.AddSingleton<SharedRetrievalCacheService>();
builder.Services.AddSingleton<CohortComparisonService>();

// Register Cosmos DB client (Serverless, managed identity only — no connection strings)
var cosmosEndpoint = builder.Configuration["COSMOS_ENDPOINT"];
if (!string.IsNullOrEmpty(cosmosEndpoint))
{
    builder.Services.AddSingleton(_ =>
    {
        var managedIdentityClientId = builder.Configuration["MANAGED_IDENTITY_CLIENT_ID"];
        Azure.Core.TokenCredential credential = string.IsNullOrEmpty(managedIdentityClientId)
            ? new DefaultAzureCredential()
            : new ManagedIdentityCredential(
                ManagedIdentityId.FromUserAssignedClientId(managedIdentityClientId));

        return new CosmosClient(
            cosmosEndpoint,
            credential,
            new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                },
                ApplicationName = "foundry-agent-webapp",
            });
    });
    builder.Services.AddSingleton<ConversationRepository>();
}

var app = builder.Build();

// Add exception handling middleware for production
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

// Add status code pages for consistent error responses
app.UseStatusCodePages();

// Map health checks
app.MapDefaultEndpoints();

// Serve static files from wwwroot (frontend)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("AllowFrontend");

// Note: HTTPS redirection not needed - Azure Container Apps handles SSL termination at ingress
// The container receives HTTP traffic on port 8080

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Unauthenticated health endpoint for container probes
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }))
.WithName("GetHealth");

// Streaming Chat endpoint: Streams agent response via SSE (conversationId → chunks → usage → done)
// Supports MCP tool approval flow with previousResponseId and mcpApproval parameters
app.MapPost("/api/chat/stream", async (
    ChatRequest request,
    AgentFrameworkService agentService,
    ConversationRepository? cosmosRepo,
    SharedRetrievalCacheService retrievalCache,
    HttpContext httpContext,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        var requestMessage = request.Message?.Trim() ?? string.Empty;
        var outputContractJson = await LoadAgentOutputContractJsonAsync(environment, cancellationToken);
        var requestUserId = GetUserId(httpContext);
        var isCohortRequest = IsCohortContext(request) && request.McpApproval is null;
        var sharedEvidence = isCohortRequest
            ? retrievalCache.TryGet(requestUserId, requestMessage)
            : null;

        var effectiveMessage = BuildEffectiveMessage(
            request,
            outputContractJson,
            sharedEvidence?.EvidencePackage,
            sharedEvidence is not null);

        httpContext.Response.Headers.Append("Content-Type", "text/event-stream");
        httpContext.Response.Headers.Append("Cache-Control", "no-cache");
        httpContext.Response.Headers.Append("Connection", "keep-alive");

        if (isCohortRequest)
        {
            await WriteRetrievalCacheEvent(
                httpContext.Response,
                sharedEvidence is not null,
                sharedEvidence?.CacheKey,
                sharedEvidence?.CreatedAt,
                cancellationToken);
        }

        // Resolve Foundry conversation ID and Cosmos record (new or resumed).
        // IMPORTANT: when Cosmos is enabled the client receives the Cosmos document id,
        // so on resume request.ConversationId is the Cosmos id (a "N"-format GUID), NOT a
        // Foundry conversation id. We must look up the stored FoundryConversationId before
        // calling Foundry, otherwise Foundry rejects it with 400 "Malformed identifier".
        string foundryConversationId;
        string? cosmosConversationId = null;

        if (cosmosRepo is not null && !string.IsNullOrEmpty(request.Message))
        {
            await cosmosRepo.InitializeAsync(cancellationToken);
            var userId = GetUserId(httpContext);
            var displayName = GetUserDisplayName(httpContext);

            if (string.IsNullOrEmpty(request.ConversationId))
            {
                // New conversation — create the real Foundry conversation, then the Cosmos record
                foundryConversationId = await agentService.CreateConversationAsync(request.Message, cancellationToken);
                var title = request.Message.Length > 80
                    ? request.Message[..80]
                    : request.Message;
                var convDoc = await cosmosRepo.CreateConversationAsync(
                    userId, displayName, foundryConversationId, title, cancellationToken);
                cosmosConversationId = convDoc.Id;
            }
            else
            {
                // Resuming — request.ConversationId is the Cosmos id; resolve the stored Foundry id
                cosmosConversationId = request.ConversationId;
                var cosmosConv = await cosmosRepo.GetConversationAsync(userId, request.ConversationId, cancellationToken);
                if (cosmosConv?.FoundryConversationId is { Length: > 0 } storedFoundryId)
                {
                    foundryConversationId = storedFoundryId;
                }
                else
                {
                    // Legacy/orphaned conversation without a stored Foundry id — start a fresh Foundry conversation
                    foundryConversationId = await agentService.CreateConversationAsync(requestMessage, cancellationToken);
                }
            }

            // Persist user message
            await cosmosRepo.AddMessageAsync(
                userId, cosmosConversationId ?? foundryConversationId,
                "user", requestMessage, ct: cancellationToken);
        }
        else
        {
            // No Cosmos persistence — request.ConversationId (if any) is the Foundry id directly
            foundryConversationId = request.ConversationId
                ?? await agentService.CreateConversationAsync(requestMessage, cancellationToken);
        }

        // Emit Cosmos conversation ID to frontend (preferred) or Foundry ID as fallback
        var clientConversationId = cosmosConversationId ?? foundryConversationId;
        await WriteConversationIdEvent(httpContext.Response, clientConversationId, cancellationToken);

        var startTime = DateTime.UtcNow;
        var assistantContentBuilder = new System.Text.StringBuilder();
        var collectedAnnotations = new List<WebApp.Api.Models.AnnotationInfo>();
        var usedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await foreach (var chunk in agentService.StreamMessageAsync(
            foundryConversationId,
            effectiveMessage,
            request.ImageDataUris,
            request.FileDataUris,
            request.PreviousResponseId,
            request.McpApproval,
            cancellationToken))
        {
            if (chunk.IsText && chunk.TextDelta != null)
            {
                assistantContentBuilder.Append(chunk.TextDelta);
                await WriteChunkEvent(httpContext.Response, chunk.TextDelta, cancellationToken);
            }
            else if (chunk.HasAnnotations && chunk.Annotations != null)
            {
                collectedAnnotations.AddRange(chunk.Annotations);
                await WriteAnnotationsEvent(httpContext.Response, chunk.Annotations, cancellationToken);
            }
            else if (chunk.IsMcpApprovalRequest && chunk.McpApprovalRequest != null)
            {
                await WriteMcpApprovalRequestEvent(httpContext.Response, chunk.McpApprovalRequest, cancellationToken);
            }
            else if (chunk.IsToolUse && chunk.ToolName != null)
            {
                usedTools.Add(chunk.ToolName);
                await WriteToolUseEvent(httpContext.Response, chunk.ToolName, cancellationToken);
            }
        }

        if (isCohortRequest && sharedEvidence is null)
        {
            var evidencePackage = BuildSharedEvidencePackage(
                assistantContentBuilder.ToString(),
                collectedAnnotations);

            retrievalCache.Set(
                requestUserId,
                requestMessage,
                evidencePackage,
                collectedAnnotations,
                usedTools.ToList());
        }

        var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        var usage = agentService.GetLastUsage();

        // Persist assistant reply to Cosmos
        if (cosmosRepo is not null && assistantContentBuilder.Length > 0 && cosmosConversationId is not null)
        {
            var userId = GetUserId(httpContext);
            await cosmosRepo.AddMessageAsync(
                userId, cosmosConversationId,
                "assistant", assistantContentBuilder.ToString(),
                promptTokens: (int?)usage?.InputTokens,
                completionTokens: (int?)usage?.OutputTokens,
                ct: cancellationToken);
        }

        await WriteUsageEvent(
            httpContext.Response,
            duration,
            usage?.InputTokens ?? 0,
            usage?.OutputTokens ?? 0,
            usage?.TotalTokens ?? 0,
            cancellationToken);

        await WriteDoneEvent(httpContext.Response, cancellationToken);
    }
    catch (ArgumentException ex) when (ex.Message.Contains("Invalid") && (ex.Message.Contains("attachments") || ex.Message.Contains("image") || ex.Message.Contains("file")))
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 400, environment.IsDevelopment());
        await WriteErrorEvent(httpContext.Response, errorResponse.Detail ?? errorResponse.Title, cancellationToken);
    }
    catch (Exception ex)
    {
        var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Chat stream error: {Message}", ex.Message);
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        await WriteErrorEvent(httpContext.Response, errorResponse.Detail ?? errorResponse.Title, cancellationToken);
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("StreamChatMessage");

static async Task WriteConversationIdEvent(HttpResponse response, string conversationId, CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new { type = "conversationId", conversationId });
    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteChunkEvent(HttpResponse response, string content, CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new { type = "chunk", content });
    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteToolUseEvent(HttpResponse response, string toolName, CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new { type = "toolUse", toolName });
    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteRetrievalCacheEvent(
    HttpResponse response,
    bool hit,
    string? cacheKey,
    DateTimeOffset? createdAt,
    CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new
    {
        type = "retrievalCache",
        hit,
        cacheKey,
        createdAt
    });

    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteAnnotationsEvent(HttpResponse response, List<WebApp.Api.Models.AnnotationInfo> annotations, CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new
    {
        type = "annotations",
        annotations = annotations.Select(a => new
        {
            type = a.Type,
            label = a.Label,
            url = a.Url,
            fileId = a.FileId,
            containerId = a.ContainerId,
            textToReplace = a.TextToReplace,
            startIndex = a.StartIndex,
            endIndex = a.EndIndex,
            quote = a.Quote
        })
    });
    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteMcpApprovalRequestEvent(HttpResponse response, WebApp.Api.Models.McpApprovalRequest approval, CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new
    {
        type = "mcpApprovalRequest",
        approvalRequest = new
        {
            id = approval.Id,
            toolName = approval.ToolName,
            serverLabel = approval.ServerLabel,
            arguments = approval.Arguments,
            previousResponseId = approval.PreviousResponseId
        }
    });
    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteUsageEvent(HttpResponse response, double duration, int promptTokens, int completionTokens, int totalTokens, CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new
    {
        type = "usage",
        duration,
        promptTokens,
        completionTokens,
        totalTokens
    });
    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteDoneEvent(HttpResponse response, CancellationToken ct)
{
    await response.WriteAsync("data: {\"type\":\"done\"}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteErrorEvent(HttpResponse response, string message, CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new { type = "error", message });
    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

// Get agent metadata (name, description, model, metadata)
// Used by frontend to display agent information in the UI
app.MapGet("/api/agent", async (
    AgentFrameworkService agentService,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        var metadata = await agentService.GetAgentMetadataAsync(cancellationToken);
        return Results.Ok(metadata);
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(
            ex, 
            500, 
            environment.IsDevelopment());
        
        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("GetAgentMetadata");

// Get canonical cohort registry (9 analyst agents + orchestrator)
app.MapGet("/api/agents/cohort", async (
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        var registryPath = ResolveSupportFilePath(environment, "agent_cohort_registry.json");

        if (!File.Exists(registryPath))
        {
            return Results.Problem(
                title: "Cohort registry not found",
                detail: $"Expected file not found at '{registryPath}'.",
                statusCode: 404);
        }

        var json = await File.ReadAllTextAsync(registryPath, cancellationToken);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return Results.Json(document.RootElement.Clone());
    }
    catch (System.Text.Json.JsonException ex)
    {
        return Results.Problem(
            title: "Invalid cohort registry",
            detail: ex.Message,
            statusCode: 500);
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("GetCohortRegistry");

// Get standard output contract used by cohort analyst agents.
app.MapGet("/api/agents/output-contract", async (
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        var json = await LoadAgentOutputContractJsonAsync(environment, cancellationToken);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return Results.Json(document.RootElement.Clone());
    }
    catch (FileNotFoundException ex)
    {
        return Results.Problem(
            title: "Output contract not found",
            detail: ex.Message,
            statusCode: 404);
    }
    catch (System.Text.Json.JsonException ex)
    {
        return Results.Problem(
            title: "Invalid output contract",
            detail: ex.Message,
            statusCode: 500);
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("GetAgentOutputContract");

// Get shared retrieval cache stats for cohort token optimization monitoring.
app.MapGet("/api/agents/retrieval-cache/stats", (SharedRetrievalCacheService retrievalCache) =>
{
    return Results.Ok(retrievalCache.GetStats());
})
.RequireAuthorization(ScopePolicyName)
.WithName("GetSharedRetrievalCacheStats");

// Compare multiple analyst outputs and generate structured cohort tables.
app.MapPost("/api/agents/cohort/compare", async (
    CohortComparisonRequest request,
    CohortComparisonService comparisonService,
    ConversationRepository? cosmosRepo,
    HttpContext httpContext,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    if (request.AgentResponses is null || request.AgentResponses.Count == 0)
    {
        return Results.BadRequest(new
        {
            error = "AgentResponses is required and must contain at least one item."
        });
    }

    var result = comparisonService.Compare(request);

    if (cosmosRepo is not null)
    {
        try
        {
            await cosmosRepo.InitializeAsync(cancellationToken);
            var userId = GetUserId(httpContext);
            var userDisplayName = GetUserDisplayName(httpContext);
            await cosmosRepo.SaveCohortRunAsync(
                userId,
                userDisplayName,
                request.ConversationId,
                request,
                result,
                cancellationToken);
        }
        catch (Exception ex)
        {
            var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "Failed to persist cohort run");
            if (environment.IsDevelopment())
            {
                return Results.Problem(title: "Cohort run persistence failed", detail: ex.Message, statusCode: 500);
            }
        }
    }

    return Results.Ok(result);
})
.RequireAuthorization(ScopePolicyName)
.WithName("CompareCohortOutputs");

// Run all active cohort analysts automatically and return comparison tables.
app.MapPost("/api/agents/cohort/run", async (
    CohortAutoRunRequest request,
    AgentFrameworkService agentService,
    CohortComparisonService comparisonService,
    ConversationRepository? cosmosRepo,
    HttpContext httpContext,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    var requestMessage = request.Message?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(requestMessage))
    {
        return Results.BadRequest(new
        {
            error = "Message is required to run the cohort."
        });
    }

    var analysts = await LoadActiveCohortAnalystsAsync(environment, cancellationToken);
    if (analysts.Count == 0)
    {
        return Results.Problem(
            title: "No active analysts found",
            detail: "The cohort registry does not contain active analyst agents.",
            statusCode: 400);
    }

    var outputContractJson = await LoadAgentOutputContractJsonAsync(environment, cancellationToken);
    var contractVersion = string.IsNullOrWhiteSpace(request.ContractVersion)
        ? "1.0.0"
        : request.ContractVersion!;
    var executionMode = string.IsNullOrWhiteSpace(request.ExecutionMode)
        ? "hybrid"
        : request.ExecutionMode!;

    var maxConcurrency = ResolveCohortRunMaxConcurrency(analysts.Count);
    var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    var sync = new object();
    var responseByAgent = new Dictionary<string, AgentAnalysisInput>(StringComparer.OrdinalIgnoreCase);
    var runErrors = new List<CohortAutoRunError>();

    var runTasks = analysts.Select(async analyst =>
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var conversationId = await agentService.CreateConversationAsync(requestMessage, cancellationToken);

            var effectiveMessage = BuildEffectiveMessage(
                new ChatRequest
                {
                    Message = requestMessage,
                    SelectedAgentId = analyst.AgentId,
                    ExecutionMode = executionMode,
                    OutputContractVersion = contractVersion,
                },
                outputContractJson,
                sharedEvidencePackage: null,
                retrievalCacheHit: false);

            var outputBuilder = new System.Text.StringBuilder();
            await foreach (var chunk in agentService.StreamMessageAsync(
                conversationId,
                effectiveMessage,
                cancellationToken: cancellationToken))
            {
                if (chunk.IsText && chunk.TextDelta is { Length: > 0 })
                {
                    outputBuilder.Append(chunk.TextDelta);
                    continue;
                }

                if (chunk.IsMcpApprovalRequest)
                {
                    throw new InvalidOperationException("Cohort auto-run cannot continue when MCP approval is required.");
                }
            }

            var outputJson = outputBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(outputJson))
            {
                lock (sync)
                {
                    runErrors.Add(new CohortAutoRunError(
                        analyst.AgentId,
                        analyst.DisplayName,
                        "Agent returned an empty response."));
                }
                return;
            }

            lock (sync)
            {
                responseByAgent[analyst.AgentId] = new AgentAnalysisInput
                {
                    AgentId = analyst.AgentId,
                    AgentName = analyst.DisplayName,
                    OutputJson = outputJson,
                };
            }
        }
        catch (Exception ex)
        {
            lock (sync)
            {
                runErrors.Add(new CohortAutoRunError(
                    analyst.AgentId,
                    analyst.DisplayName,
                    ex.Message));
            }
        }
        finally
        {
            semaphore.Release();
        }
    }).ToList();

    await Task.WhenAll(runTasks);

    var agentResponses = analysts
        .Where(analyst => responseByAgent.ContainsKey(analyst.AgentId))
        .Select(analyst => responseByAgent[analyst.AgentId])
        .ToList();

    if (agentResponses.Count == 0)
    {
        return Results.Problem(
            title: "Cohort execution failed",
            detail: "No analyst produced a usable response.",
            statusCode: 502,
            extensions: new Dictionary<string, object?>
            {
                ["errors"] = runErrors
            });
    }

    var comparisonRequest = new CohortComparisonRequest
    {
        AgentResponses = agentResponses,
        Query = string.IsNullOrWhiteSpace(request.Query) ? requestMessage : request.Query,
        ExecutionMode = executionMode,
        ContractVersion = contractVersion,
        ConversationId = request.ConversationId,
    };

    var comparison = comparisonService.Compare(comparisonRequest);

    if (cosmosRepo is not null)
    {
        try
        {
            await cosmosRepo.InitializeAsync(cancellationToken);
            var userId = GetUserId(httpContext);
            var userDisplayName = GetUserDisplayName(httpContext);
            await cosmosRepo.SaveCohortRunAsync(
                userId,
                userDisplayName,
                comparisonRequest.ConversationId,
                comparisonRequest,
                comparison,
                cancellationToken);
        }
        catch (Exception ex)
        {
            var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "Failed to persist auto cohort run");
            if (environment.IsDevelopment())
            {
                return Results.Problem(title: "Cohort run persistence failed", detail: ex.Message, statusCode: 500);
            }
        }
    }

    return Results.Ok(new CohortAutoRunResponse
    {
        AgentResponses = agentResponses,
        Comparison = comparison,
        Errors = runErrors,
    });
})
.RequireAuthorization(ScopePolicyName)
.WithName("RunCohortAndCompare");

// List persisted cohort runs for the current user.
app.MapGet("/api/agents/cohort/runs", async (
    HttpContext httpContext,
    ConversationRepository? cosmosRepo,
    IHostEnvironment environment,
    int? limit,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (cosmosRepo is null)
        {
            return Results.Problem(title: "Not Implemented", detail: "Cohort run history requires Cosmos DB.", statusCode: 501);
        }

        await cosmosRepo.InitializeAsync(cancellationToken);
        var userId = GetUserId(httpContext);
        var runs = await cosmosRepo.ListCohortRunsAsync(userId, limit ?? 20, cancellationToken);
        return Results.Ok(new { runs, hasMore = false });
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(title: errorResponse.Title, detail: errorResponse.Detail,
            statusCode: errorResponse.Status, extensions: errorResponse.Extensions);
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("GetCohortRuns");

// Get agent info (for debugging)
app.MapGet("/api/agent/info", async (
    AgentFrameworkService agentService,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        var agentInfo = await agentService.GetAgentInfoAsync(cancellationToken);
        return Results.Ok(new
        {
            info = agentInfo,
            status = "ready"
        });
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(
            ex, 
            500, 
            environment.IsDevelopment());
        
        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("GetAgentInfo");

// ── Conversation History (Cosmos DB) ─────────────────────────────────────
// All endpoints are user-scoped: only conversations belonging to the
// authenticated user (identified by 'oid' claim) are returned.

static string GetUserId(HttpContext ctx)
{
    // 'oid' (object ID) is stable across renames; fall back to 'sub' for local dev tokens
    return ctx.User.FindFirst("oid")?.Value
        ?? ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? throw new InvalidOperationException("Cannot resolve user identity from token");
}

static string? GetUserDisplayName(HttpContext ctx) =>
    ctx.User.FindFirst("name")?.Value
    ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

// List conversations — user-scoped, optional search
app.MapGet("/api/conversations", async (
    HttpContext httpContext,
    ConversationRepository? cosmosRepo,
    AgentFrameworkService agentService,
    IHostEnvironment environment,
    int? limit,
    string? search,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (cosmosRepo is not null)
        {
            await cosmosRepo.InitializeAsync(cancellationToken);
            var userId = GetUserId(httpContext);
            var pageSize = Math.Clamp(limit ?? 20, 1, 100);

            List<CosmosConversationSummary> conversations;
            if (!string.IsNullOrWhiteSpace(search))
                conversations = await cosmosRepo.SearchConversationsAsync(userId, search, pageSize, cancellationToken);
            else
                conversations = await cosmosRepo.ListConversationsAsync(userId, pageSize, cancellationToken);

            return Results.Ok(new { conversations, hasMore = false });
        }
        else
        {
            // Cosmos not configured — fall back to Foundry (agent-scoped)
            var pageSize = Math.Clamp(limit ?? 20, 1, 100);
            var conversations = await agentService.ListConversationsAsync(pageSize, cancellationToken);
            var hasMore = conversations.Count > pageSize;
            if (hasMore) conversations = conversations.Take(pageSize).ToList();
            return Results.Ok(new { conversations, hasMore });
        }
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(title: errorResponse.Title, detail: errorResponse.Detail,
            statusCode: errorResponse.Status, extensions: errorResponse.Extensions);
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("ListConversations");

// Get conversation messages
app.MapGet("/api/conversations/{conversationId}/messages", async (
    string conversationId,
    HttpContext httpContext,
    ConversationRepository? cosmosRepo,
    AgentFrameworkService agentService,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (cosmosRepo is not null)
        {
            await cosmosRepo.InitializeAsync(cancellationToken);
            var userId = GetUserId(httpContext);
            // Ownership check
            var conv = await cosmosRepo.GetConversationAsync(userId, conversationId, cancellationToken);
            if (conv is null) return Results.NotFound();
            var messages = await cosmosRepo.GetMessagesAsync(conversationId, cancellationToken);
            return Results.Ok(messages);
        }
        else
        {
            var messages = await agentService.GetConversationMessagesAsync(conversationId, cancellationToken);
            return Results.Ok(messages);
        }
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(title: errorResponse.Title, detail: errorResponse.Detail,
            statusCode: errorResponse.Status, extensions: errorResponse.Extensions);
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("GetConversationMessages");

// Hard-delete conversation (user-scoped, real delete)
app.MapDelete("/api/conversations/{conversationId}", async (
    string conversationId,
    HttpContext httpContext,
    ConversationRepository? cosmosRepo,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (cosmosRepo is not null)
        {
            await cosmosRepo.InitializeAsync(cancellationToken);
            var userId = GetUserId(httpContext);
            var displayName = GetUserDisplayName(httpContext);
            await cosmosRepo.DeleteConversationAsync(userId, displayName, conversationId, cancellationToken);
            return Results.NoContent();
        }
        return Results.Problem(title: "Not Implemented",
            detail: "Conversation deletion requires Cosmos DB. Set COSMOS_ENDPOINT environment variable.",
            statusCode: 501);
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(title: errorResponse.Title, detail: errorResponse.Detail,
            statusCode: errorResponse.Status, extensions: errorResponse.Extensions);
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("DeleteConversation");

// Set retention policy for a conversation (days; 0 = keep forever)
app.MapPut("/api/conversations/{conversationId}/retention", async (
    string conversationId,
    RetentionRequest retentionRequest,
    HttpContext httpContext,
    ConversationRepository? cosmosRepo,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (cosmosRepo is null)
            return Results.Problem(title: "Not Implemented",
                detail: "Retention management requires Cosmos DB.", statusCode: 501);

        await cosmosRepo.InitializeAsync(cancellationToken);
        var userId = GetUserId(httpContext);
        var displayName = GetUserDisplayName(httpContext);
        await cosmosRepo.SetRetentionAsync(userId, displayName, conversationId, retentionRequest.RetentionDays, cancellationToken);
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(title: errorResponse.Title, detail: errorResponse.Detail,
            statusCode: errorResponse.Status, extensions: errorResponse.Extensions);
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("SetConversationRetention");

// Get audit log for the authenticated user
app.MapGet("/api/audit", async (
    HttpContext httpContext,
    ConversationRepository? cosmosRepo,
    IHostEnvironment environment,
    int? limit,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (cosmosRepo is null)
            return Results.Problem(title: "Not Implemented",
                detail: "Audit log requires Cosmos DB.", statusCode: 501);

        await cosmosRepo.InitializeAsync(cancellationToken);
        var userId = GetUserId(httpContext);
        var entries = await cosmosRepo.GetAuditLogAsync(userId, limit ?? 100, cancellationToken);
        return Results.Ok(entries);
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(title: errorResponse.Title, detail: errorResponse.Detail,
            statusCode: errorResponse.Status, extensions: errorResponse.Extensions);
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("GetAuditLog");

// File download endpoint for code interpreter outputs
app.MapGet("/api/files/{fileId}", async (
    string fileId,
    string? containerId,
    AgentFrameworkService agentService,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        var (content, fileName) = await agentService.DownloadFileAsync(fileId, containerId, cancellationToken);
        var contentType = GetMimeType(fileName);
        return Results.File(content.ToArray(), contentType, fileName);
    }
    catch (HttpRequestException httpEx)
    {
        var statusCode = (int?)httpEx.StatusCode ?? 502;
        var errorResponse = ErrorResponseFactory.CreateFromException(httpEx, statusCode, environment.IsDevelopment());
        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("DownloadFile");

// Uploaded-files cleanup endpoints — inspect & delete image files previously uploaded by
// this web app. Uses the WebAppUploadFilenamePrefix tag applied on upload to scope the
// operation to our own files, because the Foundry Files API does not expose a typed
// expires_after parameter in the GA SDK (see README "Known limitations").
app.MapGet("/api/files/uploaded", async (
    AgentFrameworkService agentService,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        var info = await agentService.ListUploadedFilesAsync(cancellationToken);
        return Results.Ok(info);
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("ListUploadedFiles");

app.MapPost("/api/files/cleanup", async (
    AgentFrameworkService agentService,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await agentService.CleanupUploadedFilesAsync(cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("CleanupUploadedFiles");

// Fallback route for SPA - serve index.html for any non-API routes
app.MapFallbackToFile("index.html");

app.Run();

static bool IsCohortContext(ChatRequest request)
{
    return !string.IsNullOrWhiteSpace(request.SelectedAgentId)
        || !string.IsNullOrWhiteSpace(request.ExecutionMode);
}

static string BuildEffectiveMessage(
    ChatRequest request,
    string outputContractJson,
    string? sharedEvidencePackage,
    bool retrievalCacheHit)
{
    // MCP approval resumes should not alter message content.
    if (request.McpApproval is not null || !IsCohortContext(request))
    {
        return request.Message;
    }

    var contractVersion = string.IsNullOrWhiteSpace(request.OutputContractVersion)
        ? "1.0.0"
        : request.OutputContractVersion;

    var executionMode = string.IsNullOrWhiteSpace(request.ExecutionMode)
        ? "hybrid"
        : request.ExecutionMode;

    var selectedAgent = string.IsNullOrWhiteSpace(request.SelectedAgentId)
        ? "unknown_agent"
        : request.SelectedAgentId;

    var retrievalDirective = retrievalCacheHit
        ? "Shared evidence package was found in cache. Use it as primary evidence and avoid retrieval/search tools unless evidence is insufficient."
        : "No shared evidence cache found. Retrieve evidence once if needed and structure output exactly by contract.";

    var sharedEvidenceSection = string.IsNullOrWhiteSpace(sharedEvidencePackage)
        ? string.Empty
        : $"\n[SHARED_EVIDENCE_PACKAGE]\n{sharedEvidencePackage}\n[/SHARED_EVIDENCE_PACKAGE]\n";

    return $"""
[COHORT_EXECUTION_CONTEXT]
selectedAgentId: {selectedAgent}
executionMode: {executionMode}
outputContractVersion: {contractVersion}
retrievalCacheHit: {retrievalCacheHit}

{retrievalDirective}

You MUST answer using ONLY valid JSON matching this output contract:
{outputContractJson}

Return plain JSON without markdown fences.
[/COHORT_EXECUTION_CONTEXT]
{sharedEvidenceSection}

{request.Message}
""";
}

static string BuildSharedEvidencePackage(string assistantContent, List<AnnotationInfo> annotations)
{
    const int maxAssistantChars = 2200;
    const int maxAnnotations = 8;
    const int maxQuoteChars = 320;

    var sb = new System.Text.StringBuilder();

    var summary = assistantContent.Length > maxAssistantChars
        ? assistantContent[..maxAssistantChars]
        : assistantContent;

    sb.AppendLine("summary:");
    sb.AppendLine(summary);

    if (annotations.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("citations:");

        foreach (var ann in annotations.Take(maxAnnotations))
        {
            var quote = ann.Quote;
            if (!string.IsNullOrWhiteSpace(quote) && quote!.Length > maxQuoteChars)
            {
                quote = quote[..maxQuoteChars];
            }

            sb.AppendLine($"- label: {ann.Label}");
            if (!string.IsNullOrWhiteSpace(ann.Url))
            {
                sb.AppendLine($"  url: {ann.Url}");
            }
            if (!string.IsNullOrWhiteSpace(quote))
            {
                sb.AppendLine($"  quote: {quote}");
            }
        }
    }

    return sb.ToString();
}

static async Task<string> LoadAgentOutputContractJsonAsync(IHostEnvironment environment, CancellationToken cancellationToken)
{
    var contractPath = ResolveSupportFilePath(environment, "agent_output_contract.json");

    if (!File.Exists(contractPath))
    {
        throw new FileNotFoundException($"Expected file not found at '{contractPath}'.");
    }

    return await File.ReadAllTextAsync(contractPath, cancellationToken);
}

static async Task<List<(string AgentId, string DisplayName)>> LoadActiveCohortAnalystsAsync(
    IHostEnvironment environment,
    CancellationToken cancellationToken)
{
    var registryPath = ResolveSupportFilePath(environment, "agent_cohort_registry.json");

    if (!File.Exists(registryPath))
    {
        throw new FileNotFoundException($"Expected file not found at '{registryPath}'.");
    }

    var json = await File.ReadAllTextAsync(registryPath, cancellationToken);
    using var document = System.Text.Json.JsonDocument.Parse(json);

    var analysts = new List<(string AgentId, string DisplayName)>();
    if (!document.RootElement.TryGetProperty("analystAgents", out var analystAgents)
        || analystAgents.ValueKind != System.Text.Json.JsonValueKind.Array)
    {
        return analysts;
    }

    foreach (var item in analystAgents.EnumerateArray())
    {
        if (!item.TryGetProperty("agentId", out var agentIdProp)
            || agentIdProp.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            continue;
        }

        if (item.TryGetProperty("status", out var statusProp)
            && statusProp.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(statusProp.GetString(), "inactive", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var agentId = agentIdProp.GetString();
        if (string.IsNullOrWhiteSpace(agentId))
        {
            continue;
        }

        var displayName = item.TryGetProperty("displayName", out var displayNameProp)
            && displayNameProp.ValueKind == System.Text.Json.JsonValueKind.String
                ? displayNameProp.GetString()
                : agentId;

        analysts.Add((agentId, string.IsNullOrWhiteSpace(displayName) ? agentId : displayName!));
    }

    return analysts;
}

static int ResolveCohortRunMaxConcurrency(int analystCount)
{
    var configured = Environment.GetEnvironmentVariable("COHORT_RUN_MAX_CONCURRENCY");
    if (!int.TryParse(configured, out var parsed))
    {
        parsed = 3;
    }

    parsed = Math.Clamp(parsed, 1, 12);
    return Math.Clamp(parsed, 1, Math.Max(1, analystCount));
}

static string ResolveSupportFilePath(IHostEnvironment environment, string fileName)
{
    var candidates = new[]
    {
        Path.Combine(environment.ContentRootPath, "9AgentesConPersonalidad", fileName),
        Path.Combine(environment.ContentRootPath, "..", "9AgentesConPersonalidad", fileName),
        Path.Combine(environment.ContentRootPath, "..", "..", "9AgentesConPersonalidad", fileName),
        Path.Combine(AppContext.BaseDirectory, "9AgentesConPersonalidad", fileName),
        Path.Combine(AppContext.BaseDirectory, "..", "9AgentesConPersonalidad", fileName),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "9AgentesConPersonalidad", fileName)
    }
    .Select(Path.GetFullPath)
    .Distinct(StringComparer.OrdinalIgnoreCase);

    foreach (var candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return candidates.First();
}

// Helper to determine MIME type from file extension
static string GetMimeType(string fileName)
{
    var ext = Path.GetExtension(fileName).ToLowerInvariant();
    return ext switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".pdf" => "application/pdf",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".txt" => "text/plain",
        ".md" => "text/markdown",
        ".html" => "text/html",
        ".py" => "text/x-python",
        ".js" => "text/javascript",
        _ => "application/octet-stream",
    };
}
