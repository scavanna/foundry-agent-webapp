using System.Text.Json;
using System.Text.RegularExpressions;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public sealed class CohortComparisonService
{
    private static readonly Regex MultiSpace = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex NonWord = new("[^\\p{L}\\p{N}\\s]", RegexOptions.Compiled);

    public CohortComparisonResponse Compare(CohortComparisonRequest request)
    {
        var warnings = new List<ComparisonWarning>();
        var parsed = new List<ParsedAgentOutput>();

        foreach (var input in request.AgentResponses)
        {
            if (TryParse(input, out var output, out var warning) && output is not null)
            {
                parsed.Add(output);
            }
            else if (warning is not null)
            {
                warnings.Add(warning);
            }
        }

        var consensus = BuildConsensusTable(parsed);
        var divergence = BuildDivergenceTable(parsed);
        var unique = BuildUniqueInsightsTable(parsed);
        var coverage = BuildEvidenceCoverageTable(parsed);

        var totalCitations = coverage.Sum(c => c.CitationCount);

        return new CohortComparisonResponse
        {
            Query = request.Query ?? string.Empty,
            ExecutionMode = request.ExecutionMode ?? "hybrid",
            ContractVersion = request.ContractVersion ?? "1.0.0",
            AgentCount = request.AgentResponses.Count,
            Summary = new ComparisonSummary(
                ConsensusPoints: consensus.Count,
                DivergencePoints: divergence.Count,
                UniqueInsights: unique.Count,
                TotalCitations: totalCitations),
            ConsensusTable = consensus,
            DivergenceTable = divergence,
            UniqueInsightsTable = unique,
            EvidenceCoverageTable = coverage,
            Warnings = warnings,
        };
    }

    private static bool TryParse(
        AgentAnalysisInput input,
        out ParsedAgentOutput? parsed,
        out ComparisonWarning? warning)
    {
        parsed = null;
        warning = null;

        try
        {
            using var doc = JsonDocument.Parse(input.OutputJson);
            var root = doc.RootElement;

            parsed = new ParsedAgentOutput
            {
                AgentId = input.AgentId,
                AgentName = input.AgentName,
                TesisPrincipal = GetString(root, "tesis_principal"),
                Recomendacion = GetString(root, "recomendacion"),
                Hallazgos = GetStringList(root, "hallazgos_clave"),
                Riesgos = GetStringList(root, "riesgos"),
                Oportunidades = GetStringList(root, "oportunidades"),
                Supuestos = GetStringList(root, "supuestos"),
                Confidence = GetInt(root, "confianza_0_100"),
                CitationCount = GetArrayCount(root, "evidencia_edgar_citada"),
            };

            return true;
        }
        catch (Exception ex)
        {
            warning = new ComparisonWarning(input.AgentId, input.AgentName, $"Invalid output JSON: {ex.Message}");
            return false;
        }
    }

    private static List<ConsensusRow> BuildConsensusTable(List<ParsedAgentOutput> parsed)
    {
        if (parsed.Count == 0)
        {
            return [];
        }

        var threshold = Math.Max(2, (int)Math.Ceiling(parsed.Count * 0.6));

        var statementMap = new Dictionary<string, (string original, HashSet<string> agents)>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in parsed)
        {
            foreach (var statement in p.Hallazgos)
            {
                var key = Normalize(statement);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!statementMap.TryGetValue(key, out var entry))
                {
                    entry = (statement, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                }

                entry.agents.Add(p.AgentId);
                statementMap[key] = entry;
            }
        }

        return statementMap.Values
            .Where(v => v.agents.Count >= threshold)
            .Select(v => new ConsensusRow(v.original, v.agents.Count, v.agents.OrderBy(a => a).ToList()))
            .OrderByDescending(r => r.SupportCount)
            .ThenBy(r => r.Statement)
            .ToList();
    }

    private static List<DivergenceRow> BuildDivergenceTable(List<ParsedAgentOutput> parsed)
    {
        var rows = new List<DivergenceRow>();

        rows.AddRange(BuildPositionRows(parsed, "tesis_principal", p => p.TesisPrincipal));
        rows.AddRange(BuildPositionRows(parsed, "recomendacion", p => p.Recomendacion));

        return rows
            .Where(r => r.SupportCount > 0)
            .OrderByDescending(r => r.SupportCount)
            .ThenBy(r => r.Dimension)
            .ToList();
    }

    private static IEnumerable<DivergenceRow> BuildPositionRows(
        List<ParsedAgentOutput> parsed,
        string dimension,
        Func<ParsedAgentOutput, string> selector)
    {
        var map = new Dictionary<string, (string original, List<string> agents)>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in parsed)
        {
            var value = selector(p);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var key = Normalize(value);
            if (!map.TryGetValue(key, out var entry))
            {
                entry = (value, new List<string>());
            }

            entry.agents.Add(p.AgentId);
            map[key] = entry;
        }

        if (map.Count <= 1)
        {
            return [];
        }

        return map.Values.Select(v => new DivergenceRow(
            Dimension: dimension,
            Position: v.original,
            SupportCount: v.agents.Count,
            SupportingAgents: v.agents.OrderBy(a => a).ToList()));
    }

    private static List<UniqueInsightRow> BuildUniqueInsightsTable(List<ParsedAgentOutput> parsed)
    {
        var map = new Dictionary<string, (string original, List<(string id, string name)> agents)>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in parsed)
        {
            foreach (var statement in p.Hallazgos)
            {
                var key = Normalize(statement);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!map.TryGetValue(key, out var entry))
                {
                    entry = (statement, new List<(string id, string name)>());
                }

                if (!entry.agents.Any(a => string.Equals(a.id, p.AgentId, StringComparison.OrdinalIgnoreCase)))
                {
                    entry.agents.Add((p.AgentId, p.AgentName));
                }

                map[key] = entry;
            }
        }

        return map.Values
            .Where(v => v.agents.Count == 1)
            .Select(v => new UniqueInsightRow(v.original, v.agents[0].id, v.agents[0].name))
            .OrderBy(r => r.AgentName)
            .ThenBy(r => r.Statement)
            .ToList();
    }

    private static List<EvidenceCoverageRow> BuildEvidenceCoverageTable(List<ParsedAgentOutput> parsed)
    {
        return parsed
            .Select(p => new EvidenceCoverageRow(
                AgentId: p.AgentId,
                AgentName: p.AgentName,
                CitationCount: p.CitationCount,
                HallazgosCount: p.Hallazgos.Count,
                RiesgosCount: p.Riesgos.Count,
                OportunidadesCount: p.Oportunidades.Count,
                SupuestosCount: p.Supuestos.Count,
                Confidence: p.Confidence))
            .OrderByDescending(r => r.CitationCount)
            .ThenByDescending(r => r.Confidence)
            .ToList();
    }

    private static string GetString(JsonElement root, string propName)
    {
        if (root.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static int GetInt(JsonElement root, string propName)
    {
        if (root.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value))
        {
            return value;
        }

        return 0;
    }

    private static int GetArrayCount(JsonElement root, string propName)
    {
        if (root.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.Array)
        {
            return prop.GetArrayLength();
        }

        return 0;
    }

    private static List<string> GetStringList(JsonElement root, string propName)
    {
        var result = new List<string>();
        if (!root.TryGetProperty(propName, out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(value.Trim());
                }
            }
        }

        return result;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lowered = text.Trim().ToLowerInvariant();
        var noPunct = NonWord.Replace(lowered, " ");
        var singleSpaced = MultiSpace.Replace(noPunct, " ");
        return singleSpaced.Trim();
    }

    private sealed class ParsedAgentOutput
    {
        public required string AgentId { get; init; }
        public required string AgentName { get; init; }
        public required string TesisPrincipal { get; init; }
        public required string Recomendacion { get; init; }
        public required List<string> Hallazgos { get; init; }
        public required List<string> Riesgos { get; init; }
        public required List<string> Oportunidades { get; init; }
        public required List<string> Supuestos { get; init; }
        public required int Confidence { get; init; }
        public required int CitationCount { get; init; }
    }
}
