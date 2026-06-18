using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

/// <summary>
/// Shared retrieval cache for cohort mode to avoid repeated tool/index lookups
/// across multiple analyst agents for the same user question.
/// </summary>
public sealed class SharedRetrievalCacheService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);
    private static readonly Regex MultiSpace = new("\\s+", RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    public SharedRetrievalSnapshot? TryGet(string userId, string query)
    {
        var key = BuildKey(userId, query);
        if (!_entries.TryGetValue(key, out var entry))
        {
            return null;
        }

        if (DateTimeOffset.UtcNow - entry.CreatedAt > DefaultTtl)
        {
            _entries.TryRemove(key, out _);
            return null;
        }

        return new SharedRetrievalSnapshot(
            entry.EvidencePackage,
            entry.Annotations,
            entry.ToolNames,
            entry.CreatedAt,
            key);
    }

    public void Set(string userId, string query, string evidencePackage, List<AnnotationInfo> annotations, List<string> toolNames)
    {
        var key = BuildKey(userId, query);

        var dedupedTools = toolNames
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _entries[key] = new CacheEntry(
            DateTimeOffset.UtcNow,
            evidencePackage,
            annotations,
            dedupedTools);
    }

    public object GetStats()
    {
        var now = DateTimeOffset.UtcNow;
        var active = _entries.Values.Count(v => now - v.CreatedAt <= DefaultTtl);
        var expired = _entries.Count - active;

        return new
        {
            ttlMinutes = (int)DefaultTtl.TotalMinutes,
            totalEntries = _entries.Count,
            activeEntries = active,
            expiredEntries = expired
        };
    }

    private static string BuildKey(string userId, string query)
    {
        var normalized = NormalizeQuery(query);
        var raw = $"{userId}:{normalized}";

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hashBytes);
    }

    private static string NormalizeQuery(string query)
    {
        var trimmed = query.Trim().ToLowerInvariant();
        return MultiSpace.Replace(trimmed, " ");
    }

    private sealed record CacheEntry(
        DateTimeOffset CreatedAt,
        string EvidencePackage,
        List<AnnotationInfo> Annotations,
        List<string> ToolNames);
}

public sealed record SharedRetrievalSnapshot(
    string EvidencePackage,
    List<AnnotationInfo> Annotations,
    List<string> ToolNames,
    DateTimeOffset CreatedAt,
    string CacheKey);
