import React, { useState, useMemo, useCallback, useEffect, useRef } from 'react';
import { ChatInterface } from './ChatInterface';
import { ConversationSidebar } from './ConversationSidebar';
import { SettingsPanel } from './core/SettingsPanel';
import { useAppState } from '../hooks/useAppState';
import { useAuth } from '../hooks/useAuth';
import { ChatService } from '../services/chatService';
import { useAppContext } from '../contexts/AppContext';
import {
  exportAsMarkdown,
  exportFullSessionAsMarkdown,
  downloadMarkdown,
  downloadFullSessionAsWord,
  downloadFullSessionAsExcel,
  printFullSessionAsPdf,
} from '../utils/exportConversation';
import { trackFeedback } from '../services/telemetry';
import type { IChatItem } from '../types/chat';
import type { ICohortAgentSummary, ICohortExecutionMode } from '../types/chat';
import type { ICohortComparisonResponse } from '../types/chat';
import type { ICohortAutoRunError } from '../types/chat';
import styles from './AgentChat.module.css';

type CohortSynthesis = {
  executive: string[];
  analytical: string[];
  technical: string[];
};

interface AgentChatProps {
  agentId: string;
  agentName: string;
  agentDescription?: string;
  agentLogo?: string;
  starterPrompts?: string[];
  selectedAgentId: string;
  selectedExecutionMode: string;
  cohortAgents: ICohortAgentSummary[];
  executionModes: ICohortExecutionMode[];
  onSelectedAgentChange: (agentId: string) => void;
  onSelectedExecutionModeChange: (mode: string) => void;
}

export const AgentChat: React.FC<AgentChatProps> = ({
  agentName,
  agentDescription,
  agentLogo,
  starterPrompts,
  selectedAgentId,
  selectedExecutionMode,
  cohortAgents,
  executionModes,
  onSelectedAgentChange,
  onSelectedExecutionModeChange
}) => {
  const { chat, state } = useAppState();
  const { dispatch } = useAppContext();
  const { getAccessToken } = useAuth();
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [isComparisonOpen, setIsComparisonOpen] = useState(false);
  const [comparisonQuery, setComparisonQuery] = useState('');
  const [comparisonInputs, setComparisonInputs] = useState<Record<string, string>>({});
  const [isComparing, setIsComparing] = useState(false);
  const [comparisonError, setComparisonError] = useState<string | null>(null);
  const [comparisonResult, setComparisonResult] = useState<ICohortComparisonResponse | null>(null);
  const [autoRunErrors, setAutoRunErrors] = useState<ICohortAutoRunError[]>([]);

  // Create service instances
  const apiUrl = import.meta.env.VITE_API_URL || '/api';
  
  const chatService = useMemo(() => {
    return new ChatService(apiUrl, getAccessToken, dispatch);
  }, [apiUrl, getAccessToken, dispatch]);

  useEffect(() => {
    chatService.setExecutionContext(selectedAgentId, selectedExecutionMode);
  }, [chatService, selectedAgentId, selectedExecutionMode]);

  useEffect(() => {
    setComparisonInputs((current) => {
      const next: Record<string, string> = {};
      cohortAgents.forEach((agent) => {
        if (agent.role === 'analyst') {
          next[agent.agentId] = current[agent.agentId] ?? '';
        }
      });
      return next;
    });
  }, [cohortAgents]);

  const handleComparisonInputChange = useCallback((agentId: string, value: string) => {
    setComparisonInputs((current) => ({
      ...current,
      [agentId]: value,
    }));
  }, []);

  const handleRunComparison = useCallback(async () => {
    setComparisonError(null);
    setAutoRunErrors([]);

    const agentResponses = cohortAgents
      .filter((agent) => agent.role === 'analyst')
      .map((agent) => ({
        agentId: agent.agentId,
        agentName: agent.displayName,
        outputJson: (comparisonInputs[agent.agentId] ?? '').trim(),
      }))
      .filter((item) => item.outputJson.length > 0);

    if (agentResponses.length === 0) {
      setComparisonError('Provide at least one analyst JSON output to run comparison.');
      return;
    }

    setIsComparing(true);
    try {
      const result = await chatService.compareCohortOutputs({
        agentResponses,
        query: comparisonQuery.trim() || undefined,
        executionMode: selectedExecutionMode,
        contractVersion: '1.0.0',
        conversationId: chat.currentConversationId || undefined,
      });
      setComparisonResult(result);
    } catch (error) {
      setComparisonError(error instanceof Error ? error.message : 'Comparison failed.');
    } finally {
      setIsComparing(false);
    }
  }, [chatService, cohortAgents, comparisonInputs, comparisonQuery, selectedExecutionMode]);

  const handleRunCohortAuto = useCallback(async () => {
    setComparisonError(null);
    setAutoRunErrors([]);

    const fallbackMessage = [...chat.messages]
      .reverse()
      .find((item) => item.role === 'user' && item.content.trim().length > 0)
      ?.content
      .trim();

    const runMessage = comparisonQuery.trim() || fallbackMessage || '';
    if (!runMessage) {
      setComparisonError('Provide a query label or send a user message first to run all analysts automatically.');
      return;
    }

    setIsComparing(true);
    try {
      const result = await chatService.runCohortAndCompare({
        message: runMessage,
        query: comparisonQuery.trim() || runMessage,
        executionMode: selectedExecutionMode,
        contractVersion: '1.0.0',
        conversationId: chat.currentConversationId || undefined,
      });

      setComparisonResult(result.comparison);
      setAutoRunErrors(result.errors);
      setComparisonInputs((current) => {
        const next = { ...current };
        result.agentResponses.forEach((agent) => {
          next[agent.agentId] = agent.outputJson;
        });
        return next;
      });
    } catch (error) {
      setComparisonError(error instanceof Error ? error.message : 'Automatic cohort run failed.');
    } finally {
      setIsComparing(false);
    }
  }, [chat.messages, chat.currentConversationId, chatService, comparisonQuery, selectedExecutionMode]);

  const selectedAgentName = useMemo(() => {
    return cohortAgents.find((agent) => agent.agentId === selectedAgentId)?.displayName || selectedAgentId;
  }, [cohortAgents, selectedAgentId]);

  const comparisonSynthesis = useMemo<CohortSynthesis | null>(() => {
    if (!comparisonResult) {
      return null;
    }

    const topConsensus = comparisonResult.consensusTable.slice(0, 3);
    const topDivergence = comparisonResult.divergenceTable.slice(0, 3);
    const topUnique = comparisonResult.uniqueInsightsTable.slice(0, 3);
    const topCoverage = [...comparisonResult.evidenceCoverageTable].sort((left, right) => right.confidence - left.confidence).slice(0, 3);

    const executive: string[] = [];
    if (topConsensus.length > 0) {
      executive.push(`Los agentes convergen en ${topConsensus.length} tesis principales, con el punto más respaldado en: ${topConsensus[0].statement}`);
    } else {
      executive.push('No se detectó consenso suficiente para consolidar una tesis dominante.');
    }
    if (topDivergence.length > 0) {
      executive.push(`La principal divergencia se concentra en ${topDivergence[0].dimension}: ${topDivergence[0].position}`);
    }
    if (topUnique.length > 0) {
      executive.push(`Hay señales únicas relevantes en ${topUnique[0].agentName}, lo que justifica conservar la diversidad de postura.`);
    }

    const analytical: string[] = [];
    analytical.push(`Consenso total: ${comparisonResult.summary.consensusPoints}; divergencias estructurales: ${comparisonResult.summary.divergencePoints}; hallazgos únicos: ${comparisonResult.summary.uniqueInsights}.`);
    if (comparisonResult.warnings.length > 0) {
      analytical.push(`Warnings operativos: ${comparisonResult.warnings.length} salida(s) requieren limpieza o corrección de JSON.`);
    }
    if (topCoverage.length > 0) {
      analytical.push(`La mayor cobertura de evidencia y confianza la aporta ${topCoverage[0].agentName} con ${topCoverage[0].citationCount} citas y confianza ${topCoverage[0].confidence}/100.`);
    }

    const technical: string[] = [];
    technical.push(`Tabla de consenso: ${comparisonResult.consensusTable.length} filas; divergencia: ${comparisonResult.divergenceTable.length}; únicos: ${comparisonResult.uniqueInsightsTable.length}; cobertura: ${comparisonResult.evidenceCoverageTable.length}.`);
    technical.push(`Contratos usados: agent_output_contract v${comparisonResult.contractVersion}; modo de ejecución: ${comparisonResult.executionMode || selectedExecutionMode}.`);
    if (comparisonResult.evidenceCoverageTable.length > 0) {
      const averageConfidence = Math.round(
        comparisonResult.evidenceCoverageTable.reduce((sum, row) => sum + row.confidence, 0) / comparisonResult.evidenceCoverageTable.length
      );
      technical.push(`Confianza media en la cohorte: ${averageConfidence}/100.`);
    }

    return { executive, analytical, technical };
  }, [comparisonResult, selectedExecutionMode]);

  const handleSendMessage = async (text: string, files?: File[]) => {
    if (chat.status === 'streaming' || chat.status === 'sending') {
      dispatch({ type: 'CHAT_QUEUE_MESSAGE', text, files });
      return;
    }
    await chatService.sendMessage(text, chat.currentConversationId, files);
  };

  // Drain the queue when the stream completes
  const pendingRef = useRef(chat.pendingMessages);
  pendingRef.current = chat.pendingMessages;

  useEffect(() => {
    if (chat.status === 'idle' && pendingRef.current.length > 0) {
      const combinedText = pendingRef.current.map(m => m.text).join('\n\n');
      const combinedFiles = pendingRef.current.flatMap(m => m.files || []);
      dispatch({ type: 'CHAT_CLEAR_QUEUE' });
      chatService.sendMessage(
        combinedText,
        chat.currentConversationId,
        combinedFiles.length > 0 ? combinedFiles : undefined
      );
    }
  }, [chat.status, chat.currentConversationId, chatService, dispatch]);

  const handleDequeueMessage = (index: number) => {
    dispatch({ type: 'CHAT_DEQUEUE_MESSAGE', index });
  };

  const handleClearError = () => {
    chatService.clearError();
  };

  const handleNewChat = () => {
    chatService.cancelStream();
    chatService.clearChat();
  };

  const handleCancelStream = () => {
    chatService.cancelStream();
  };

  const handleRecoveredInputConsumed = () => {
    dispatch({ type: 'CHAT_CONSUMED_RECOVERED_INPUT' });
  };

  const handleRegenerate = useCallback(() => {
    chatService.cancelStream();
    dispatch({ type: 'CHAT_REGENERATE' });
  }, [chatService, dispatch]);

  const handleEditMessage = useCallback((messageId: string, newText: string) => {
    dispatch({ type: 'CHAT_EDIT_MESSAGE', messageId, newText });
  }, [dispatch]);

  const handleFeedback = useCallback((messageId: string, rating: 'positive' | 'negative') => {
    trackFeedback(messageId, chat.currentConversationId, rating);
  }, [chat.currentConversationId]);

  const handleCancelEdit = useCallback(() => {
    dispatch({ type: 'CHAT_CANCEL_EDIT' });
  }, [dispatch]);

  const handleDownloadFile = useCallback(async (fileId: string, fileName: string, containerId?: string) => {
    try {
      await chatService.downloadFile(fileId, fileName, containerId);
    } catch (err) {
      dispatch({
        type: 'CHAT_ERROR',
        error: { code: 'NETWORK', message: `Failed to download ${fileName}: ${err instanceof Error ? err.message : 'Unknown error'}`, recoverable: true },
      });
    }
  }, [chatService, dispatch]);

  // Auto-send when regenerateText is set (from regenerate or edit actions)
  useEffect(() => {
    if (chat.regenerateText?.trim() && chat.status === 'idle') {
      const text = chat.regenerateText;
      dispatch({ type: 'CHAT_CONSUMED_REGENERATE' });
      chatService.sendMessage(text, chat.currentConversationId);
    }
  }, [chat.regenerateText, chat.status, chat.currentConversationId, chatService, dispatch]);

  const handleMcpApproval = async (
    approvalRequestId: string,
    approved: boolean,
    previousResponseId: string,
    conversationId: string
  ) => {
    dispatch({ type: 'CHAT_MCP_APPROVAL_RESOLVED', approvalRequestId, resolved: approved ? 'approved' : 'rejected' });
    try {
      await chatService.sendMcpApproval(approvalRequestId, approved, previousResponseId, conversationId);
    } catch {
      // Rollback so user can retry — clears resolved state, restoring buttons
      dispatch({ type: 'CHAT_MCP_APPROVAL_RESOLVED', approvalRequestId, resolved: undefined });
    }
  };

  const handleExportConversation = useCallback(() => {
    const md = exportAsMarkdown(chat.messages, agentName);
    downloadMarkdown(md);
  }, [chat.messages, agentName]);

  const handleExportFullSession = useCallback(() => {
    const md = exportFullSessionAsMarkdown({
      messages: chat.messages,
      agentName,
      selectedAgentName,
      selectedExecutionMode,
      queryLabel: comparisonQuery.trim() || undefined,
      comparisonResult,
      executiveSummary: comparisonSynthesis?.executive,
      analyticalSummary: comparisonSynthesis?.analytical,
      technicalSummary: comparisonSynthesis?.technical,
    });
    const fileName = `full-session-${Date.now()}.md`;
    downloadMarkdown(md, fileName);
  }, [agentName, chat.messages, comparisonQuery, comparisonResult, comparisonSynthesis?.analytical, comparisonSynthesis?.executive, comparisonSynthesis?.technical, selectedAgentName, selectedExecutionMode]);

  const getFullSessionExportOptions = useCallback(() => ({
    messages: chat.messages,
    agentName,
    selectedAgentName,
    selectedExecutionMode,
    queryLabel: comparisonQuery.trim() || undefined,
    comparisonResult,
    executiveSummary: comparisonSynthesis?.executive,
    analyticalSummary: comparisonSynthesis?.analytical,
    technicalSummary: comparisonSynthesis?.technical,
  }), [agentName, chat.messages, comparisonQuery, comparisonResult, comparisonSynthesis?.analytical, comparisonSynthesis?.executive, comparisonSynthesis?.technical, selectedAgentName, selectedExecutionMode]);

  const handleExportFullSessionWord = useCallback(() => {
    downloadFullSessionAsWord(getFullSessionExportOptions());
  }, [getFullSessionExportOptions]);

  const handleExportFullSessionExcel = useCallback(() => {
    downloadFullSessionAsExcel(getFullSessionExportOptions());
  }, [getFullSessionExportOptions]);

  const handleExportFullSessionPdf = useCallback(() => {
    printFullSessionAsPdf(getFullSessionExportOptions());
  }, [getFullSessionExportOptions]);

  const handleToggleSidebar = useCallback(async () => {
    const willOpen = !state.conversations.sidebarOpen;
    dispatch({ type: 'CONVERSATIONS_TOGGLE_SIDEBAR' });
    if (willOpen) {
      dispatch({ type: 'CONVERSATIONS_LOADING' });
      try {
        const result = await chatService.listConversations();
        dispatch({ type: 'CONVERSATIONS_SET_LIST', conversations: result.conversations, hasMore: result.hasMore });
      } catch (error) {
        console.error('Failed to load conversations:', error);
        dispatch({ type: 'CONVERSATIONS_SET_LIST', conversations: [], hasMore: false });
      }
    }
  }, [state.conversations.sidebarOpen, dispatch, chatService]);

  const handleSidebarOpenChange = useCallback((open: boolean) => {
    if (!open && state.conversations.sidebarOpen) {
      dispatch({ type: 'CONVERSATIONS_TOGGLE_SIDEBAR' });
    }
  }, [state.conversations.sidebarOpen, dispatch]);

  const handleLoadMoreConversations = useCallback(async () => {
    dispatch({ type: 'CONVERSATIONS_LOADING' });
    try {
      const currentCount = state.conversations.list.length;
      const result = await chatService.listConversations(currentCount + 20);
      // Slice off items we already have and append only new ones
      const newItems = result.conversations.slice(currentCount);
      // If no new items returned (e.g., backend limit cap), stop pagination
      const hasMore = newItems.length > 0 && result.hasMore;
      dispatch({ type: 'CONVERSATIONS_SET_LIST', conversations: newItems, hasMore, append: true });
    } catch (error) {
      console.error('Failed to load more conversations:', error);
      dispatch({ type: 'CONVERSATIONS_LOADING_DONE' });
    }
  }, [state.conversations.list.length, dispatch, chatService]);

  const handleSelectConversation = useCallback(async (conversationId: string) => {
    try {
      chatService.cancelStream();
      const messages = await chatService.getConversationMessages(conversationId);
      const chatItems: IChatItem[] = messages
        .filter(msg => msg.role === 'user' || msg.role === 'assistant')
        .map((msg, index) => ({
          id: `${conversationId}-${index}`,
          role: msg.role as 'user' | 'assistant',
          content: msg.content,
          more: { time: new Date().toISOString() },
        }));

      dispatch({ type: 'CHAT_LOAD_CONVERSATION', conversationId, messages: chatItems });
    } catch (error) {
      console.error('Failed to load conversation:', error);
    }
  }, [chatService, dispatch]);

  const handleDeleteConversation = useCallback(async (conversationId: string) => {
    // Remove from UI immediately (optimistic)
    dispatch({ type: 'CONVERSATIONS_REMOVE', conversationId });
    if (chat.currentConversationId === conversationId) {
      chatService.clearChat();
    }
    // Attempt server-side delete (may not be supported yet)
    try {
      await chatService.deleteConversation(conversationId);
    } catch (error) {
      // 501 = SDK doesn't support delete yet — item is hidden locally only
      console.warn('Server-side conversation delete not available:', error);
    }
  }, [chatService, dispatch, chat.currentConversationId]);

  return (
    <div className={styles.content}>
      <div className={styles.selectorBar}>
        <label className={styles.selectorField}>
          <span>Analyst agent</span>
          <select
            className={styles.selectorInput}
            value={selectedAgentId}
            onChange={(e) => onSelectedAgentChange(e.target.value)}
          >
            {cohortAgents.map((agent) => (
              <option key={agent.agentId} value={agent.agentId}>
                {agent.displayName}{agent.provisional ? ' (provisional)' : ''}
              </option>
            ))}
          </select>
        </label>

        <label className={styles.selectorField}>
          <span>Execution mode</span>
          <select
            className={styles.selectorInput}
            value={selectedExecutionMode}
            onChange={(e) => onSelectedExecutionModeChange(e.target.value)}
          >
            {executionModes.map((mode) => (
              <option key={mode.mode} value={mode.mode}>
                {mode.mode}
              </option>
            ))}
          </select>
        </label>

        <button
          type="button"
          className={styles.compareToggleButton}
          onClick={() => setIsComparisonOpen((open) => !open)}
        >
          {isComparisonOpen ? 'Hide Cohort Tables' : 'Open Cohort Tables'}
        </button>
      </div>

      {isComparisonOpen && (
        <div className={styles.comparisonPanel}>
          <div className={styles.comparisonHeader}>
            <h3 className={styles.comparisonTitle}>Cohort Comparison Engine</h3>
            <div className={styles.comparisonActions}>
              <button
                type="button"
                className={styles.compareAutoRunButton}
                onClick={handleRunCohortAuto}
                disabled={isComparing}
              >
                {isComparing ? 'Running...' : 'Run 9 Analysts'}
              </button>
              <button
                type="button"
                className={styles.compareRunButton}
                onClick={handleRunComparison}
                disabled={isComparing}
              >
                {isComparing ? 'Running...' : 'Run Comparison'}
              </button>
            </div>
          </div>

          <label className={styles.comparisonQueryField}>
            <span>Query label (optional)</span>
            <input
              className={styles.comparisonQueryInput}
              type="text"
              value={comparisonQuery}
              onChange={(e) => setComparisonQuery(e.target.value)}
              placeholder="EDGAR question or run identifier"
            />
          </label>

          <div className={styles.comparisonInputsGrid}>
            {cohortAgents
              .filter((agent) => agent.role === 'analyst')
              .map((agent) => (
                <label key={agent.agentId} className={styles.comparisonInputCard}>
                  <span>{agent.displayName}</span>
                  <textarea
                    className={styles.comparisonTextarea}
                    value={comparisonInputs[agent.agentId] ?? ''}
                    onChange={(e) => handleComparisonInputChange(agent.agentId, e.target.value)}
                    placeholder="Paste raw JSON output that follows agent_output_contract.json"
                  />
                </label>
              ))}
          </div>

          {comparisonError && <div className={styles.comparisonError}>{comparisonError}</div>}

          {autoRunErrors.length > 0 && (
            <div className={styles.autoRunWarningsBox}>
              <h4 className={styles.tableTitle}>Auto-run warnings</h4>
              {autoRunErrors.map((warning, index) => (
                <p key={`autorun-warning-${index}`}>
                  [{warning.agentName}] {warning.message}
                </p>
              ))}
            </div>
          )}

          {comparisonResult && (
            <div className={styles.comparisonResults}>
              <div className={styles.summaryGrid}>
                <div className={styles.summaryCard}>Consensus: {comparisonResult.summary.consensusPoints}</div>
                <div className={styles.summaryCard}>Divergence: {comparisonResult.summary.divergencePoints}</div>
                <div className={styles.summaryCard}>Unique: {comparisonResult.summary.uniqueInsights}</div>
                <div className={styles.summaryCard}>Citations: {comparisonResult.summary.totalCitations}</div>
              </div>

              <div className={styles.comparisonExportRow}>
                <button
                  type="button"
                  className={styles.compareToggleButton}
                  onClick={handleExportFullSession}
                >
                  Export Markdown
                </button>
                <button
                  type="button"
                  className={styles.compareToggleButton}
                  onClick={handleExportFullSessionWord}
                >
                  Export Word
                </button>
                <button
                  type="button"
                  className={styles.compareToggleButton}
                  onClick={handleExportFullSessionExcel}
                >
                  Export Excel
                </button>
                <button
                  type="button"
                  className={styles.compareToggleButton}
                  onClick={handleExportFullSessionPdf}
                >
                  Export PDF
                </button>
              </div>

              <h4 className={styles.tableTitle}>Consensus Table</h4>
              <table className={styles.resultTable}>
                <thead>
                  <tr>
                    <th>Statement</th>
                    <th>Support</th>
                    <th>Agents</th>
                  </tr>
                </thead>
                <tbody>
                  {comparisonResult.consensusTable.map((row, index) => (
                    <tr key={`consensus-${index}`}>
                      <td>{row.statement}</td>
                      <td>{row.supportCount}</td>
                      <td>{row.supportingAgents.join(', ')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>

              <h4 className={styles.tableTitle}>Divergence Table</h4>
              <table className={styles.resultTable}>
                <thead>
                  <tr>
                    <th>Dimension</th>
                    <th>Position</th>
                    <th>Support</th>
                    <th>Agents</th>
                  </tr>
                </thead>
                <tbody>
                  {comparisonResult.divergenceTable.map((row, index) => (
                    <tr key={`divergence-${index}`}>
                      <td>{row.dimension}</td>
                      <td>{row.position}</td>
                      <td>{row.supportCount}</td>
                      <td>{row.supportingAgents.join(', ')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>

              <h4 className={styles.tableTitle}>Unique Insights Table</h4>
              <table className={styles.resultTable}>
                <thead>
                  <tr>
                    <th>Agent</th>
                    <th>Statement</th>
                  </tr>
                </thead>
                <tbody>
                  {comparisonResult.uniqueInsightsTable.map((row, index) => (
                    <tr key={`unique-${index}`}>
                      <td>{row.agentName}</td>
                      <td>{row.statement}</td>
                    </tr>
                  ))}
                </tbody>
              </table>

              <h4 className={styles.tableTitle}>Evidence Coverage Table</h4>
              <table className={styles.resultTable}>
                <thead>
                  <tr>
                    <th>Agent</th>
                    <th>Citations</th>
                    <th>Hallazgos</th>
                    <th>Riesgos</th>
                    <th>Oportunidades</th>
                    <th>Supuestos</th>
                    <th>Confidence</th>
                  </tr>
                </thead>
                <tbody>
                  {comparisonResult.evidenceCoverageTable.map((row, index) => (
                    <tr key={`coverage-${index}`}>
                      <td>{row.agentName}</td>
                      <td>{row.citationCount}</td>
                      <td>{row.hallazgosCount}</td>
                      <td>{row.riesgosCount}</td>
                      <td>{row.oportunidadesCount}</td>
                      <td>{row.supuestosCount}</td>
                      <td>{row.confidence}</td>
                    </tr>
                  ))}
                </tbody>
              </table>

              {comparisonResult.warnings.length > 0 && (
                <div className={styles.warningsBox}>
                  <h4 className={styles.tableTitle}>Warnings</h4>
                  {comparisonResult.warnings.map((warning, index) => (
                    <p key={`warning-${index}`}>
                      [{warning.agentName}] {warning.message}
                    </p>
                  ))}
                </div>
              )}

                  {comparisonSynthesis && (
                    <div className={styles.synthesisPanel}>
                      <h4 className={styles.tableTitle}>Multilevel Synthesis</h4>

                      <div className={styles.synthesisGrid}>
                        <section className={styles.synthesisCard}>
                          <h5 className={styles.synthesisCardTitle}>Executive</h5>
                          {comparisonSynthesis.executive.map((line, index) => (
                            <p key={`exec-${index}`}>{line}</p>
                          ))}
                        </section>

                        <section className={styles.synthesisCard}>
                          <h5 className={styles.synthesisCardTitle}>Analytical</h5>
                          {comparisonSynthesis.analytical.map((line, index) => (
                            <p key={`ana-${index}`}>{line}</p>
                          ))}
                        </section>

                        <section className={styles.synthesisCard}>
                          <h5 className={styles.synthesisCardTitle}>Technical</h5>
                          {comparisonSynthesis.technical.map((line, index) => (
                            <p key={`tech-${index}`}>{line}</p>
                          ))}
                        </section>
                      </div>
                    </div>
                  )}
            </div>
          )}
        </div>
      )}

      <div className={styles.mainContent}>
        <ChatInterface 
          messages={chat.messages}
          status={chat.status}
          error={chat.error}
          streamingMessageId={chat.streamingMessageId}
          recoveredInput={chat.recoveredInput}
          recoveredAttachments={chat.recoveredAttachments}
          onSendMessage={handleSendMessage}
          onClearError={handleClearError}
          onRecoveredInputConsumed={handleRecoveredInputConsumed}
          onOpenSettings={() => setIsSettingsOpen(true)}
          onNewChat={handleNewChat}
          onCancelStream={handleCancelStream}
          onMcpApproval={handleMcpApproval}
          onToggleSidebar={handleToggleSidebar}
          onExportConversation={handleExportConversation}
          onRegenerate={handleRegenerate}
          onEditMessage={handleEditMessage}
          onCancelEdit={handleCancelEdit}
          isEditing={!!chat.editSnapshot}
          onFeedback={handleFeedback}
          onDownloadFile={handleDownloadFile}
          conversationId={chat.currentConversationId}
          pendingMessages={chat.pendingMessages}
          onDequeueMessage={handleDequeueMessage}
          hasMessages={chat.messages.length > 0}
          disabled={false}
          agentName={agentName}
          agentDescription={agentDescription}
          agentLogo={agentLogo}
          starterPrompts={starterPrompts}
        />
      </div>

      <ConversationSidebar
        isOpen={state.conversations.sidebarOpen}
        onOpenChange={handleSidebarOpenChange}
        conversations={state.conversations.list}
        isLoading={state.conversations.isLoading}
        hasMore={state.conversations.hasMore}
        currentConversationId={chat.currentConversationId}
        onSelectConversation={handleSelectConversation}
        onNewChat={handleNewChat}
        onDeleteConversation={handleDeleteConversation}
        onLoadMore={handleLoadMoreConversations}
      />
      
      <SettingsPanel
        isOpen={isSettingsOpen}
        onOpenChange={setIsSettingsOpen}
        chatService={chatService}
      />
    </div>
  );
};
