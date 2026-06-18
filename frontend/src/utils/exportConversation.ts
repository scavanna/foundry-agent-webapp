import type { IChatItem } from '../types/chat';
import type { ICohortComparisonResponse } from '../types/chat';

function escapeMarkdownCell(value: string): string {
  return value.replace(/\|/g, '\\|').replace(/\n/g, ' ');
}

export function exportAsMarkdown(messages: IChatItem[], agentName?: string): string {
  const lines: string[] = [];
  lines.push(`# Conversation with ${agentName || 'AI Agent'}`);
  lines.push(`_Exported ${new Date().toLocaleString()}_\n`);

  for (const msg of messages) {
    if (msg.role === 'user') {
      lines.push(`## You\n> ${msg.content.replace(/\n/g, '\n> ')}\n`);
    } else if (msg.role === 'assistant') {
      lines.push(`## ${agentName || 'Assistant'}\n${msg.content}\n`);
    } else if (msg.role === 'approval') {
      lines.push(`## Tool Approval\n_MCP tool approval: ${msg.mcpApproval?.toolName || 'unknown'}_\n`);
    }
  }
  return lines.join('\n');
}

export function exportFullSessionAsMarkdown(options: {
  messages: IChatItem[];
  agentName?: string;
  selectedAgentName?: string;
  selectedExecutionMode?: string;
  queryLabel?: string;
  comparisonResult?: ICohortComparisonResponse | null;
  executiveSummary?: string[];
  analyticalSummary?: string[];
  technicalSummary?: string[];
}): string {
  const { messages, agentName, selectedAgentName, selectedExecutionMode, queryLabel, comparisonResult, executiveSummary, analyticalSummary, technicalSummary } = options;
  const lines: string[] = [];

  lines.push(`# Full Session Export`);
  lines.push(`_Exported ${new Date().toLocaleString()}_`);
  if (agentName) lines.push(`- Agent: ${agentName}`);
  if (selectedAgentName) lines.push(`- Selected analyst: ${selectedAgentName}`);
  if (selectedExecutionMode) lines.push(`- Execution mode: ${selectedExecutionMode}`);
  if (queryLabel) lines.push(`- Query: ${queryLabel}`);
  lines.push('');

  lines.push(`## Conversation`);
  for (const msg of messages) {
    if (msg.role === 'user') {
      lines.push(`### You`);
      lines.push(`> ${msg.content.replace(/\n/g, '\n> ')}`);
      lines.push('');
    } else if (msg.role === 'assistant') {
      lines.push(`### ${agentName || 'Assistant'}`);
      lines.push(msg.content);
      lines.push('');
    } else if (msg.role === 'approval') {
      lines.push(`### Tool Approval`);
      lines.push(`_MCP tool approval: ${msg.mcpApproval?.toolName || 'unknown'}_`);
      lines.push('');
    }
  }

  if (comparisonResult) {
    lines.push(`## Cohort Comparison`);
    lines.push(`- Consensus points: ${comparisonResult.summary.consensusPoints}`);
    lines.push(`- Divergence points: ${comparisonResult.summary.divergencePoints}`);
    lines.push(`- Unique insights: ${comparisonResult.summary.uniqueInsights}`);
    lines.push(`- Total citations: ${comparisonResult.summary.totalCitations}`);
    lines.push('');

    if (executiveSummary?.length) {
      lines.push(`### Executive`);
      executiveSummary.forEach((line) => lines.push(`- ${line}`));
      lines.push('');
    }

    if (analyticalSummary?.length) {
      lines.push(`### Analytical`);
      analyticalSummary.forEach((line) => lines.push(`- ${line}`));
      lines.push('');
    }

    if (technicalSummary?.length) {
      lines.push(`### Technical`);
      technicalSummary.forEach((line) => lines.push(`- ${line}`));
      lines.push('');
    }

    lines.push(`### Consensus Table`);
    lines.push(`| Statement | Support | Agents |`);
    lines.push(`| --- | ---: | --- |`);
    comparisonResult.consensusTable.forEach((row) => {
      lines.push(`| ${escapeMarkdownCell(row.statement)} | ${row.supportCount} | ${escapeMarkdownCell(row.supportingAgents.join(', '))} |`);
    });
    lines.push('');

    lines.push(`### Divergence Table`);
    lines.push(`| Dimension | Position | Support | Agents |`);
    lines.push(`| --- | --- | ---: | --- |`);
    comparisonResult.divergenceTable.forEach((row) => {
      lines.push(`| ${escapeMarkdownCell(row.dimension)} | ${escapeMarkdownCell(row.position)} | ${row.supportCount} | ${escapeMarkdownCell(row.supportingAgents.join(', '))} |`);
    });
    lines.push('');

    lines.push(`### Unique Insights Table`);
    lines.push(`| Agent | Statement |`);
    lines.push(`| --- | --- |`);
    comparisonResult.uniqueInsightsTable.forEach((row) => {
      lines.push(`| ${escapeMarkdownCell(row.agentName)} | ${escapeMarkdownCell(row.statement)} |`);
    });
    lines.push('');

    lines.push(`### Evidence Coverage Table`);
    lines.push(`| Agent | Citations | Hallazgos | Riesgos | Oportunidades | Supuestos | Confidence |`);
    lines.push(`| --- | ---: | ---: | ---: | ---: | ---: | ---: |`);
    comparisonResult.evidenceCoverageTable.forEach((row) => {
      lines.push(`| ${escapeMarkdownCell(row.agentName)} | ${row.citationCount} | ${row.hallazgosCount} | ${row.riesgosCount} | ${row.oportunidadesCount} | ${row.supuestosCount} | ${row.confidence} |`);
    });
    lines.push('');
  }

  return lines.join('\n');
}

export function downloadMarkdown(content: string, filename?: string) {
  const blob = new Blob([content], { type: 'text/markdown' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename || `conversation-${Date.now()}.md`;
  a.click();
  URL.revokeObjectURL(url);
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function buildSafeFilename(base: string, extension: string): string {
  const sanitized = base.replace(/[^a-z0-9\-_.]/gi, '-').replace(/-+/g, '-');
  return `${sanitized || 'full-session'}-${Date.now()}.${extension}`;
}

function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

type FullSessionExportOptions = {
  messages: IChatItem[];
  agentName?: string;
  selectedAgentName?: string;
  selectedExecutionMode?: string;
  queryLabel?: string;
  comparisonResult?: ICohortComparisonResponse | null;
  executiveSummary?: string[];
  analyticalSummary?: string[];
  technicalSummary?: string[];
};

function buildFullSessionHtml(options: FullSessionExportOptions): string {
  const markdown = exportFullSessionAsMarkdown(options);

  return `<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  <title>Full Session Export</title>
  <style>
    body { font-family: 'Segoe UI', Tahoma, Arial, sans-serif; margin: 24px; color: #1b1a19; }
    h1, h2, h3, h4 { margin: 16px 0 8px; }
    pre { white-space: pre-wrap; word-break: break-word; border: 1px solid #ddd; padding: 12px; border-radius: 6px; background: #fafafa; }
  </style>
</head>
<body>
  <h1>Full Session Export</h1>
  <pre>${escapeHtml(markdown)}</pre>
</body>
</html>`;
}

function buildFullSessionExcelHtml(options: FullSessionExportOptions): string {
  const { messages, comparisonResult } = options;
  const rows: string[] = [];

  rows.push('<table border="1">');
  rows.push('<tr><th>Section</th><th>Field</th><th>Value</th></tr>');
  rows.push(`<tr><td>Metadata</td><td>ExportedAt</td><td>${escapeHtml(new Date().toISOString())}</td></tr>`);

  messages.forEach((msg, index) => {
    rows.push(`<tr><td>Conversation</td><td>Message ${index + 1} (${escapeHtml(msg.role ?? 'unknown')})</td><td>${escapeHtml(msg.content)}</td></tr>`);
  });

  if (comparisonResult) {
    rows.push(`<tr><td>Summary</td><td>Consensus</td><td>${comparisonResult.summary.consensusPoints}</td></tr>`);
    rows.push(`<tr><td>Summary</td><td>Divergence</td><td>${comparisonResult.summary.divergencePoints}</td></tr>`);
    rows.push(`<tr><td>Summary</td><td>Unique</td><td>${comparisonResult.summary.uniqueInsights}</td></tr>`);
    rows.push(`<tr><td>Summary</td><td>TotalCitations</td><td>${comparisonResult.summary.totalCitations}</td></tr>`);

    comparisonResult.consensusTable.forEach((row, i) => {
      rows.push(`<tr><td>Consensus</td><td>${i + 1}. ${escapeHtml(row.statement)}</td><td>${row.supportCount} | ${escapeHtml(row.supportingAgents.join(', '))}</td></tr>`);
    });

    comparisonResult.divergenceTable.forEach((row, i) => {
      rows.push(`<tr><td>Divergence</td><td>${i + 1}. ${escapeHtml(row.dimension)}</td><td>${escapeHtml(row.position)} | ${row.supportCount} | ${escapeHtml(row.supportingAgents.join(', '))}</td></tr>`);
    });

    comparisonResult.uniqueInsightsTable.forEach((row, i) => {
      rows.push(`<tr><td>UniqueInsight</td><td>${i + 1}. ${escapeHtml(row.agentName)}</td><td>${escapeHtml(row.statement)}</td></tr>`);
    });

    comparisonResult.evidenceCoverageTable.forEach((row, i) => {
      rows.push(`<tr><td>EvidenceCoverage</td><td>${i + 1}. ${escapeHtml(row.agentName)}</td><td>Citations=${row.citationCount}; Confidence=${row.confidence}</td></tr>`);
    });
  }

  rows.push('</table>');

  return `<!doctype html><html><head><meta charset="utf-8" /></head><body>${rows.join('')}</body></html>`;
}

export function downloadFullSessionAsWord(options: FullSessionExportOptions, baseFilename = 'full-session') {
  const html = buildFullSessionHtml(options);
  const blob = new Blob([html], { type: 'application/msword' });
  downloadBlob(blob, buildSafeFilename(baseFilename, 'doc'));
}

export function downloadFullSessionAsExcel(options: FullSessionExportOptions, baseFilename = 'full-session') {
  const html = buildFullSessionExcelHtml(options);
  const blob = new Blob([html], { type: 'application/vnd.ms-excel' });
  downloadBlob(blob, buildSafeFilename(baseFilename, 'xls'));
}

export function printFullSessionAsPdf(options: FullSessionExportOptions) {
  const html = buildFullSessionHtml(options);
  const printWindow = window.open('', '_blank', 'noopener,noreferrer,width=1024,height=768');
  if (!printWindow) {
    throw new Error('Unable to open print window. Please allow pop-ups for this site.');
  }

  printWindow.document.open();
  printWindow.document.write(html);
  printWindow.document.close();
  printWindow.focus();
  printWindow.print();
}
