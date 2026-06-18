export interface IChatItem {
  id: string;
  role?: 'user' | 'assistant' | 'approval';
  content: string;
  duration?: number; // response time in ms
  attachments?: IFileAttachment[]; // File attachments
  annotations?: IAnnotation[]; // Citations/references from AI agent
  mcpApproval?: IMcpApprovalRequest; // MCP tool approval request
  activeToolUse?: string; // Currently active tool (e.g. "file_search", "code_interpreter")
  retryAttempt?: number; // Current retry attempt (set during retries)
  maxRetries?: number; // Max retry attempts (set during retries)
  more?: {
    time?: string; // ISO timestamp
    usage?: IUsageInfo; // Usage info from backend
  };
}

export interface IMcpApprovalRequest {
  id: string;
  toolName: string;
  serverLabel: string;
  arguments?: string;
  previousResponseId?: string;
  resolved?: 'approved' | 'rejected';
}

export interface IUsageInfo {
  duration?: number;           // Response time in milliseconds
  promptTokens: number;        // Input token count
  completionTokens: number;    // Output token count
  totalTokens?: number;        // Total token count
}

export interface IFileAttachment {
  fileName: string;
  fileSizeBytes: number;
  dataUri?: string; // Base64 data URI for inline image preview
}

/** Citation/annotation from AI agent responses (Azure AI Agent SDK annotation types). */
export interface IAnnotation {
  /** Type: "uri_citation", "file_citation", "file_path", or "container_file_citation" */
  type: 'uri_citation' | 'file_citation' | 'file_path' | 'container_file_citation';
  /** Display label (title or filename) */
  label: string;
  /** URL for URI citations */
  url?: string;
  /** File ID for file citations */
  fileId?: string;
  /** Container ID for container file citations (code interpreter outputs) */
  containerId?: string;
  /** Placeholder text in the response to replace (e.g., "【4:0†source】") */
  textToReplace?: string;
  /** Start index in the text where the citation applies */
  startIndex?: number;
  /** End index in the text where the citation applies */
  endIndex?: number;
  /** Quote from the source document (for file citations) */
  quote?: string;
}

// Agent metadata types
export interface IAgentMetadata {
  id: string;
  object: string;
  createdAt: number;
  name: string;
  description?: string | null;
  model: string;
  instructions?: string | null;
  metadata?: Record<string, string> | null;
  /**
   * Starter prompts from agent metadata ("starterPrompts" key, camelCase).
   * Stored as newline-separated text in the metadata dictionary.
   * If not set in Microsoft Foundry, defaults will be used in the UI.
   */
  starterPrompts?: string[] | null;
}

// Cohort registry types
export interface ICohortAgentSummary {
  agentId: string;
  displayName: string;
  role: 'analyst' | 'orchestrator';
  status: 'active' | 'inactive';
  provisional?: boolean;
  personaGroup?: string;
}

export interface ICohortExecutionMode {
  mode: string;
  description: string;
}

export interface ICohortRegistry {
  version: string;
  lastUpdated: string;
  analystAgents: ICohortAgentSummary[];
  executionModes: ICohortExecutionMode[];
}

export interface IAgentAnalysisInput {
  agentId: string;
  agentName: string;
  outputJson: string;
}

export interface ICohortComparisonRequest {
  agentResponses: IAgentAnalysisInput[];
  query?: string;
  executionMode?: string;
  contractVersion?: string;
  conversationId?: string;
}

export interface IComparisonSummary {
  consensusPoints: number;
  divergencePoints: number;
  uniqueInsights: number;
  totalCitations: number;
}

export interface IConsensusRow {
  statement: string;
  supportCount: number;
  supportingAgents: string[];
}

export interface IDivergenceRow {
  dimension: string;
  position: string;
  supportCount: number;
  supportingAgents: string[];
}

export interface IUniqueInsightRow {
  statement: string;
  agentId: string;
  agentName: string;
}

export interface IEvidenceCoverageRow {
  agentId: string;
  agentName: string;
  citationCount: number;
  hallazgosCount: number;
  riesgosCount: number;
  oportunidadesCount: number;
  supuestosCount: number;
  confidence: number;
}

export interface IComparisonWarning {
  agentId: string;
  agentName: string;
  message: string;
}

export interface ICohortComparisonResponse {
  query: string;
  executionMode: string;
  contractVersion: string;
  agentCount: number;
  summary: IComparisonSummary;
  consensusTable: IConsensusRow[];
  divergenceTable: IDivergenceRow[];
  uniqueInsightsTable: IUniqueInsightRow[];
  evidenceCoverageTable: IEvidenceCoverageRow[];
  warnings: IComparisonWarning[];
}

export interface ICohortAutoRunRequest {
  message: string;
  query?: string;
  executionMode?: string;
  contractVersion?: string;
  conversationId?: string;
}

export interface ICohortAutoRunError {
  agentId: string;
  agentName: string;
  message: string;
}

export interface ICohortAutoRunResponse {
  agentResponses: IAgentAnalysisInput[];
  comparison: ICohortComparisonResponse;
  errors: ICohortAutoRunError[];
}
