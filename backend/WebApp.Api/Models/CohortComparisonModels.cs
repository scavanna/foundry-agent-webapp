namespace WebApp.Api.Models;

public record CohortComparisonRequest
{
    public required List<AgentAnalysisInput> AgentResponses { get; init; }
    public string? Query { get; init; }
    public string? ExecutionMode { get; init; }
    public string? ContractVersion { get; init; }
    public string? ConversationId { get; init; }
}

public record CohortAutoRunRequest
{
    public required string Message { get; init; }
    public string? Query { get; init; }
    public string? ExecutionMode { get; init; }
    public string? ContractVersion { get; init; }
    public string? ConversationId { get; init; }
}

public record AgentAnalysisInput
{
    public required string AgentId { get; init; }
    public required string AgentName { get; init; }
    // Raw JSON returned by the agent following agent_output_contract.json
    public required string OutputJson { get; init; }
}

public record CohortComparisonResponse
{
    public required string Query { get; init; }
    public required string ExecutionMode { get; init; }
    public required string ContractVersion { get; init; }
    public required int AgentCount { get; init; }
    public required ComparisonSummary Summary { get; init; }
    public required List<ConsensusRow> ConsensusTable { get; init; }
    public required List<DivergenceRow> DivergenceTable { get; init; }
    public required List<UniqueInsightRow> UniqueInsightsTable { get; init; }
    public required List<EvidenceCoverageRow> EvidenceCoverageTable { get; init; }
    public required List<ComparisonWarning> Warnings { get; init; }
}

public record CohortAutoRunResponse
{
    public required List<AgentAnalysisInput> AgentResponses { get; init; }
    public required CohortComparisonResponse Comparison { get; init; }
    public required List<CohortAutoRunError> Errors { get; init; }
}

public record CohortAutoRunError(
    string AgentId,
    string AgentName,
    string Message);

public record ComparisonSummary(
    int ConsensusPoints,
    int DivergencePoints,
    int UniqueInsights,
    int TotalCitations);

public record ConsensusRow(
    string Statement,
    int SupportCount,
    List<string> SupportingAgents);

public record DivergenceRow(
    string Dimension,
    string Position,
    int SupportCount,
    List<string> SupportingAgents);

public record UniqueInsightRow(
    string Statement,
    string AgentId,
    string AgentName);

public record EvidenceCoverageRow(
    string AgentId,
    string AgentName,
    int CitationCount,
    int HallazgosCount,
    int RiesgosCount,
    int OportunidadesCount,
    int SupuestosCount,
    int Confidence);

public record ComparisonWarning(
    string AgentId,
    string AgentName,
    string Message);
