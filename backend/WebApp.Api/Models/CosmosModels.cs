using Newtonsoft.Json;

namespace WebApp.Api.Models;

/// <summary>
/// Conversation document stored in Cosmos DB container 'conversations'.
/// Partition key: /userId — all queries are scoped per user for isolation.
/// </summary>
public class ConversationDocument
{
    [JsonProperty("id")]
    public required string Id { get; set; }

    [JsonProperty("userId")]
    public required string UserId { get; set; }

    [JsonProperty("agentId")]
    public required string AgentId { get; set; }

    /// <summary>Title derived from the first user message (truncated to 120 chars).</summary>
    [JsonProperty("title")]
    public string? Title { get; set; }

    /// <summary>ISO-8601 UTC timestamp when the conversation was created.</summary>
    [JsonProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>ISO-8601 UTC timestamp when the last message was added.</summary>
    [JsonProperty("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Total message count (user + assistant), maintained incrementally.</summary>
    [JsonProperty("messageCount")]
    public int MessageCount { get; set; }

    /// <summary>
    /// Soft-delete flag. Deleted conversations are hidden from list but retained until TTL expires.
    /// Use DELETE endpoint for hard delete (removes document immediately).
    /// </summary>
    [JsonProperty("deleted")]
    public bool Deleted { get; set; }

    /// <summary>
    /// Cosmos TTL in seconds. Set to a positive value to enable automatic expiration.
    /// -1 = no TTL (keep forever). When null the property is omitted so the
    /// container's default TTL policy applies (Cosmos rejects an explicit null ttl).
    /// </summary>
    [JsonProperty("ttl", NullValueHandling = NullValueHandling.Ignore)]
    public int? Ttl { get; set; }

    /// <summary>User display name at the time of creation (for audit display).</summary>
    [JsonProperty("userDisplayName")]
    public string? UserDisplayName { get; set; }

    /// <summary>Foundry conversation ID used to resume streaming.</summary>
    [JsonProperty("foundryConversationId")]
    public string? FoundryConversationId { get; set; }
}

/// <summary>
/// Message document stored in Cosmos DB container 'messages'.
/// Partition key: /conversationId — all messages for a conversation are co-located.
/// </summary>
public class MessageDocument
{
    [JsonProperty("id")]
    public required string Id { get; set; }

    [JsonProperty("conversationId")]
    public required string ConversationId { get; set; }

    [JsonProperty("userId")]
    public required string UserId { get; set; }

    [JsonProperty("agentId")]
    public required string AgentId { get; set; }

    /// <summary>"user" or "assistant"</summary>
    [JsonProperty("role")]
    public required string Role { get; set; }

    [JsonProperty("content")]
    public required string Content { get; set; }

    [JsonProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Token usage for assistant messages.</summary>
    [JsonProperty("promptTokens")]
    public int? PromptTokens { get; set; }

    [JsonProperty("completionTokens")]
    public int? CompletionTokens { get; set; }

    /// <summary>Inherits TTL from parent conversation (set by repository on write).
    /// Omitted from JSON when null (Cosmos rejects an explicit null ttl).</summary>
    [JsonProperty("ttl", NullValueHandling = NullValueHandling.Ignore)]
    public int? Ttl { get; set; }
}

/// <summary>
/// Audit event stored in Cosmos DB container 'audit'.
/// Partition key: /userId — scope all audit queries per user.
/// Container default TTL = 90 days.
/// </summary>
public class AuditDocument
{
    [JsonProperty("id")]
    public required string Id { get; set; }

    [JsonProperty("userId")]
    public required string UserId { get; set; }

    [JsonProperty("userDisplayName")]
    public string? UserDisplayName { get; set; }

    [JsonProperty("agentId")]
    public required string AgentId { get; set; }

    [JsonProperty("conversationId")]
    public string? ConversationId { get; set; }

    /// <summary>
    /// Action performed: conversation_created, message_sent, conversation_deleted,
    /// conversation_listed, conversation_retention_updated.
    /// </summary>
    [JsonProperty("action")]
    public required string Action { get; set; }

    [JsonProperty("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Optional extra context (e.g. new retention value, message length).</summary>
    [JsonProperty("details")]
    public Dictionary<string, string>? Details { get; set; }
}

/// <summary>DTO returned to the frontend for conversation list.</summary>
public record CosmosConversationSummary(
    string Id,
    string? Title,
    string AgentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MessageCount,
    string? FoundryConversationId
);

/// <summary>DTO returned to the frontend for conversation message history.</summary>
public record CosmosMessageInfo(
    string Id,
    string Role,
    string Content,
    DateTimeOffset CreatedAt
);

/// <summary>
/// Cohort comparison run document stored in Cosmos DB container 'cohortRuns'.
/// Partition key: /userId — user-scoped record of each comparison execution.
/// </summary>
public class CohortRunDocument
{
    [JsonProperty("id")]
    public required string Id { get; set; }

    [JsonProperty("userId")]
    public required string UserId { get; set; }

    [JsonProperty("agentId")]
    public required string AgentId { get; set; }

    [JsonProperty("userDisplayName")]
    public string? UserDisplayName { get; set; }

    [JsonProperty("conversationId")]
    public string? ConversationId { get; set; }

    [JsonProperty("query")]
    public string? Query { get; set; }

    [JsonProperty("executionMode")]
    public string? ExecutionMode { get; set; }

    [JsonProperty("contractVersion")]
    public string? ContractVersion { get; set; }

    [JsonProperty("agentCount")]
    public int AgentCount { get; set; }

    [JsonProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonProperty("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonProperty("requestJson")]
    public required string RequestJson { get; set; }

    [JsonProperty("responseJson")]
    public required string ResponseJson { get; set; }

    /// <summary>
    /// Optional TTL in seconds. Null means use container default policy.
    /// </summary>
    [JsonProperty("ttl", NullValueHandling = NullValueHandling.Ignore)]
    public int? Ttl { get; set; }
}

/// <summary>DTO returned to the frontend for cohort run history.</summary>
public record CohortRunSummary(
    string Id,
    string? ConversationId,
    string? Query,
    string? ExecutionMode,
    string? ContractVersion,
    int AgentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
