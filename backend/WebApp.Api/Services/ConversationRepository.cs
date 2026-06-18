using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

/// <summary>
/// Repository for per-user conversation history backed by Cosmos DB (Serverless, NoSQL API).
///
/// Design decisions for budget:
/// - Serverless capacity mode: no minimum monthly cost, pay per RU consumed.
/// - disableLocalAuth=true on account: managed identity only, no connection strings.
/// - Separate containers for conversations / messages / audit so index costs are minimal.
/// - TTL on audit container (90 d) and optional per-conversation TTL for retention policy.
/// - Composite indexes scoped to (userId + createdAt) to keep index overhead low.
/// </summary>
public class ConversationRepository
{
    private const string DatabaseName = "conversations";
    private const string ConversationsContainerName = "conversations";
    private const string MessagesContainerName = "messages";
    private const string AuditContainerName = "audit";
    private const string CohortRunsContainerName = "cohortRuns";

    private readonly CosmosClient _client;
    private readonly ILogger<ConversationRepository> _logger;
    private readonly string _agentId;

    // Container references — resolved once at startup
    private Container _convContainer = null!;
    private Container _msgContainer = null!;
    private Container _auditContainer = null!;
    private Container _cohortRunsContainer = null!;

    public ConversationRepository(
        CosmosClient client,
        IConfiguration configuration,
        ILogger<ConversationRepository> logger)
    {
        _client = client;
        _logger = logger;
        _agentId = configuration["AI_AGENT_ID"]
            ?? throw new InvalidOperationException("AI_AGENT_ID is required");
    }

    /// <summary>
    /// Initialize container references. Called from the DI-registered hosted service or
    /// lazily on first request. Safe to call multiple times.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var db = _client.GetDatabase(DatabaseName);
        _convContainer = db.GetContainer(ConversationsContainerName);
        _msgContainer = db.GetContainer(MessagesContainerName);
        _auditContainer = db.GetContainer(AuditContainerName);

        // Keep cohort persistence resilient when infrastructure drifts.
        // If the container is missing, create it with the expected partition key.
        await db.CreateContainerIfNotExistsAsync(
            new ContainerProperties(CohortRunsContainerName, "/userId"),
            cancellationToken: ct);
        _cohortRunsContainer = db.GetContainer(CohortRunsContainerName);

        _logger.LogInformation("ConversationRepository initialized for agent {AgentId}", _agentId);
        await Task.CompletedTask;
    }

    private Container ConvContainer => _convContainer ?? throw new InvalidOperationException(
        "ConversationRepository not initialized. Call InitializeAsync first.");

    private Container MsgContainer => _msgContainer ?? throw new InvalidOperationException(
        "ConversationRepository not initialized. Call InitializeAsync first.");

    private Container AuditContainer => _auditContainer ?? throw new InvalidOperationException(
        "ConversationRepository not initialized. Call InitializeAsync first.");

    private Container CohortRunsContainer => _cohortRunsContainer ?? throw new InvalidOperationException(
        "ConversationRepository not initialized. Call InitializeAsync first.");

    // -------------------------------------------------------------------------
    // Conversations
    // -------------------------------------------------------------------------

    /// <summary>
    /// Create a new conversation record linked to a Foundry conversation ID.
    /// </summary>
    public async Task<ConversationDocument> CreateConversationAsync(
        string userId,
        string? userDisplayName,
        string foundryConversationId,
        string? title,
        CancellationToken ct = default)
    {
        var doc = new ConversationDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            AgentId = _agentId,
            FoundryConversationId = foundryConversationId,
            Title = TruncateTitle(title),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            MessageCount = 0,
            Deleted = false,
            UserDisplayName = userDisplayName,
        };

        await ConvContainer.CreateItemAsync(doc, new PartitionKey(userId), cancellationToken: ct);
        await WriteAuditAsync(userId, userDisplayName, "conversation_created", doc.Id, ct: ct);
        _logger.LogInformation("Created conversation {ConvId} for user {UserId}", doc.Id, userId);
        return doc;
    }

    /// <summary>
    /// List non-deleted conversations for a user, newest first.
    /// </summary>
    public async Task<List<CosmosConversationSummary>> ListConversationsAsync(
        string userId,
        int limit = 20,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);

        var query = ConvContainer.GetItemLinqQueryable<ConversationDocument>(
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(userId),
                MaxItemCount = limit
            })
            .Where(c => c.UserId == userId && c.AgentId == _agentId && !c.Deleted)
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit)
            .ToFeedIterator();

        var results = new List<CosmosConversationSummary>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => new CosmosConversationSummary(
                d.Id, d.Title, d.AgentId, d.CreatedAt, d.UpdatedAt, d.MessageCount, d.FoundryConversationId)));
        }

        await WriteAuditAsync(userId, null, "conversation_listed", null, ct: ct);
        return results;
    }

    /// <summary>
    /// Get a single conversation by ID (validates ownership).
    /// Returns null if not found or belongs to another user.
    /// </summary>
    public async Task<ConversationDocument?> GetConversationAsync(
        string userId,
        string conversationId,
        CancellationToken ct = default)
    {
        try
        {
            var response = await ConvContainer.ReadItemAsync<ConversationDocument>(
                conversationId, new PartitionKey(userId), cancellationToken: ct);
            var doc = response.Resource;
            if (doc.Deleted || doc.AgentId != _agentId) return null;
            return doc;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Hard-delete a conversation and all its messages. Writes an audit record.
    /// </summary>
    public async Task DeleteConversationAsync(
        string userId,
        string? userDisplayName,
        string conversationId,
        CancellationToken ct = default)
    {
        // Validate ownership before deleting
        var doc = await GetConversationAsync(userId, conversationId, ct);
        if (doc is null)
        {
            _logger.LogWarning("Delete: conversation {ConvId} not found for user {UserId}", conversationId, userId);
            return;
        }

        // Hard-delete messages in the messages container (partition key = conversationId)
        await DeleteAllMessagesAsync(conversationId, ct);

        // Hard-delete the conversation document
        await ConvContainer.DeleteItemAsync<ConversationDocument>(
            conversationId, new PartitionKey(userId), cancellationToken: ct);

        await WriteAuditAsync(userId, userDisplayName, "conversation_deleted", conversationId, ct: ct);
        _logger.LogInformation("Hard-deleted conversation {ConvId} for user {UserId}", conversationId, userId);
    }

    /// <summary>
    /// Update retention (TTL in days) for a conversation and its messages.
    /// Pass 0 to clear TTL (keep forever).
    /// </summary>
    public async Task SetRetentionAsync(
        string userId,
        string? userDisplayName,
        string conversationId,
        int retentionDays,
        CancellationToken ct = default)
    {
        var doc = await GetConversationAsync(userId, conversationId, ct);
        if (doc is null) return;

        doc.Ttl = retentionDays > 0 ? retentionDays * 86400 : null;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await ConvContainer.ReplaceItemAsync(doc, doc.Id, new PartitionKey(userId), cancellationToken: ct);

        await WriteAuditAsync(userId, userDisplayName, "conversation_retention_updated", conversationId,
            details: new() { ["retentionDays"] = retentionDays.ToString() }, ct: ct);
    }

    /// <summary>
    /// Search conversations by title substring for a user.
    /// </summary>
    public async Task<List<CosmosConversationSummary>> SearchConversationsAsync(
        string userId,
        string searchText,
        int limit = 20,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50);
        var lower = searchText.ToLowerInvariant();

        var query = ConvContainer.GetItemLinqQueryable<ConversationDocument>(
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) })
            .Where(c => c.UserId == userId && c.AgentId == _agentId && !c.Deleted)
            .Where(c => c.Title != null && c.Title.ToLower().Contains(lower))
            .OrderByDescending(c => c.UpdatedAt)
            .Take(limit)
            .ToFeedIterator();

        var results = new List<CosmosConversationSummary>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => new CosmosConversationSummary(
                d.Id, d.Title, d.AgentId, d.CreatedAt, d.UpdatedAt, d.MessageCount, d.FoundryConversationId)));
        }
        return results;
    }

    // -------------------------------------------------------------------------
    // Messages
    // -------------------------------------------------------------------------

    /// <summary>
    /// Append a user or assistant message to the conversation.
    /// Also updates the conversation's UpdatedAt and MessageCount.
    /// </summary>
    public async Task<MessageDocument> AddMessageAsync(
        string userId,
        string conversationId,
        string role,
        string content,
        int? promptTokens = null,
        int? completionTokens = null,
        int? inheritedTtlSeconds = null,
        CancellationToken ct = default)
    {
        var msg = new MessageDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            ConversationId = conversationId,
            UserId = userId,
            AgentId = _agentId,
            Role = role,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            Ttl = inheritedTtlSeconds,
        };

        await MsgContainer.CreateItemAsync(msg, new PartitionKey(conversationId), cancellationToken: ct);

        // Increment counter on the conversation document (best-effort patch)
        try
        {
            var patchOps = new[]
            {
                PatchOperation.Increment("/messageCount", 1),
                PatchOperation.Set("/updatedAt", DateTimeOffset.UtcNow),
            };
            await ConvContainer.PatchItemAsync<ConversationDocument>(
                conversationId, new PartitionKey(userId), patchOps, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to patch messageCount on conversation {ConvId}", conversationId);
        }

        if (role == "user")
            await WriteAuditAsync(userId, null, "message_sent", conversationId, ct: ct);

        return msg;
    }

    /// <summary>
    /// Get all messages for a conversation (newest first).
    /// </summary>
    public async Task<List<CosmosMessageInfo>> GetMessagesAsync(
        string conversationId,
        CancellationToken ct = default)
    {
        var query = MsgContainer.GetItemLinqQueryable<MessageDocument>(
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(conversationId) })
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToFeedIterator();

        var results = new List<CosmosMessageInfo>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(m => new CosmosMessageInfo(m.Id, m.Role, m.Content, m.CreatedAt)));
        }
        return results;
    }

    // -------------------------------------------------------------------------
    // Audit
    // -------------------------------------------------------------------------

    /// <summary>
    /// Query audit log for a user (newest first, up to 200 entries).
    /// </summary>
    public async Task<List<AuditDocument>> GetAuditLogAsync(
        string userId,
        int limit = 100,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        var query = AuditContainer.GetItemLinqQueryable<AuditDocument>(
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) })
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .ToFeedIterator();

        var results = new List<AuditDocument>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page);
        }
        return results;
    }

    // -------------------------------------------------------------------------
    // Cohort runs
    // -------------------------------------------------------------------------

    public async Task<CohortRunDocument> SaveCohortRunAsync(
        string userId,
        string? userDisplayName,
        string? conversationId,
        CohortComparisonRequest request,
        CohortComparisonResponse response,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var doc = new CohortRunDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            AgentId = _agentId,
            UserDisplayName = userDisplayName,
            ConversationId = conversationId,
            Query = request.Query,
            ExecutionMode = request.ExecutionMode,
            ContractVersion = request.ContractVersion,
            AgentCount = request.AgentResponses.Count,
            CreatedAt = now,
            UpdatedAt = now,
            RequestJson = System.Text.Json.JsonSerializer.Serialize(request),
            ResponseJson = System.Text.Json.JsonSerializer.Serialize(response),
        };

        await CohortRunsContainer.CreateItemAsync(doc, new PartitionKey(userId), cancellationToken: ct);
        await WriteAuditAsync(userId, userDisplayName, "cohort_run_created", conversationId, details: new()
        {
            ["cohortRunId"] = doc.Id,
            ["agentCount"] = doc.AgentCount.ToString(),
        }, ct: ct);

        return doc;
    }

    public async Task<List<CohortRunSummary>> ListCohortRunsAsync(
        string userId,
        int limit = 20,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);

        var query = CohortRunsContainer.GetItemLinqQueryable<CohortRunDocument>(
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId), MaxItemCount = limit })
            .Where(run => run.UserId == userId && run.AgentId == _agentId)
            .OrderByDescending(run => run.CreatedAt)
            .Take(limit)
            .ToFeedIterator();

        var results = new List<CohortRunSummary>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(run => new CohortRunSummary(
                run.Id,
                run.ConversationId,
                run.Query,
                run.ExecutionMode,
                run.ContractVersion,
                run.AgentCount,
                run.CreatedAt,
                run.UpdatedAt)));
        }

        return results;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task DeleteAllMessagesAsync(string conversationId, CancellationToken ct)
    {
        var query = MsgContainer.GetItemLinqQueryable<MessageDocument>(
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(conversationId) })
            .Where(m => m.ConversationId == conversationId)
            .Select(m => m.Id)
            .ToFeedIterator();

        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            foreach (var id in page)
            {
                try
                {
                    await MsgContainer.DeleteItemAsync<MessageDocument>(
                        id, new PartitionKey(conversationId), cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete message {MsgId} in conversation {ConvId}", id, conversationId);
                }
            }
        }
    }

    private async Task WriteAuditAsync(
        string userId,
        string? userDisplayName,
        string action,
        string? conversationId,
        Dictionary<string, string>? details = null,
        CancellationToken ct = default)
    {
        try
        {
            var doc = new AuditDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                UserDisplayName = userDisplayName,
                AgentId = _agentId,
                ConversationId = conversationId,
                Action = action,
                Timestamp = DateTimeOffset.UtcNow,
                Details = details,
            };
            await AuditContainer.CreateItemAsync(doc, new PartitionKey(userId), cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // Audit failures must never break the main flow
            _logger.LogWarning(ex, "Failed to write audit event {Action} for user {UserId}", action, userId);
        }
    }

    private static string? TruncateTitle(string? title) =>
        title is null ? null : (title.Length > 120 ? title[..120] : title);
}
