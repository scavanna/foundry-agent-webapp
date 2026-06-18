import { AuthenticatedTemplate, UnauthenticatedTemplate, useMsalAuthentication } from "@azure/msal-react";
import { Spinner } from '@fluentui/react-components';
import { useAppState } from './hooks/useAppState';
import { InteractionType } from "@azure/msal-browser";
import { ErrorBoundary } from "./components/core/ErrorBoundary";
import { AgentChat } from "./components/AgentChat";
import { loginRequest } from "./config/authConfig";
import { useState, useEffect, useCallback } from "react";
import { useAuth } from "./hooks/useAuth";
import type { IAgentMetadata, ICohortRegistry } from "./types/chat";
import "./App.css";

function App() {
  // This hook handles authentication automatically - redirects if not authenticated
  useMsalAuthentication(InteractionType.Redirect, loginRequest);
  const { auth } = useAppState();
  const { getAccessToken } = useAuth();
  const [agentMetadata, setAgentMetadata] = useState<IAgentMetadata | null>(null);
  const [cohortRegistry, setCohortRegistry] = useState<ICohortRegistry | null>(null);
  const [isLoadingAgent, setIsLoadingAgent] = useState(true);
  const [isLoadingCohort, setIsLoadingCohort] = useState(true);
  const [selectedAgentId, setSelectedAgentId] = useState<string>('');
  const [selectedExecutionMode, setSelectedExecutionMode] = useState<string>('hybrid');

  // Wrap fetchAgentMetadata in useCallback to make it stable for the effect
  const fetchAgentMetadata = useCallback(async () => {
    if (auth.status !== 'authenticated') return;

    try {
      const token = await getAccessToken();
      const apiUrl = import.meta.env.VITE_API_URL || '/api';
      
      const response = await fetch(`${apiUrl}/agent`, {
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        }
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }

      const data = await response.json();
      setAgentMetadata(data);
      
      // Update document title with agent name
      document.title = data.name ? `${data.name} - Azure AI Agent` : 'Azure AI Agent';
    } catch (error) {
      console.error('Error fetching agent metadata:', error);
      // Fallback data keeps UI functional on error
      setAgentMetadata({
        id: 'fallback-agent',
        object: 'agent',
        createdAt: Date.now() / 1000,
        name: 'Azure AI Agent',
        description: 'Your intelligent conversational partner powered by Azure AI',
        model: 'gpt-4o-mini',
        metadata: { logo: 'Avatar_Default.svg' }
      });
      document.title = 'Azure AI Agent';
    } finally {
      setIsLoadingAgent(false);
    }
  }, [auth.status, getAccessToken]);

  useEffect(() => {
    fetchAgentMetadata();
  }, [fetchAgentMetadata]);

  const fetchCohortRegistry = useCallback(async () => {
    if (auth.status !== 'authenticated') return;

    try {
      const token = await getAccessToken();
      const apiUrl = import.meta.env.VITE_API_URL || '/api';

      const response = await fetch(`${apiUrl}/agents/cohort`, {
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        }
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }

      const data: ICohortRegistry = await response.json();
      setCohortRegistry(data);

      // Initialize defaults from server registry.
      if (!selectedAgentId && data.analystAgents.length > 0) {
        setSelectedAgentId(data.analystAgents[0].agentId);
      }

      if (data.executionModes.length > 0 && !data.executionModes.some((m) => m.mode === selectedExecutionMode)) {
        setSelectedExecutionMode(data.executionModes[0].mode);
      }
    } catch (error) {
      console.error('Error fetching cohort registry:', error);
      setCohortRegistry({
        version: 'fallback',
        lastUpdated: new Date().toISOString().slice(0, 10),
        analystAgents: [],
        executionModes: [
          { mode: 'competitive', description: 'Independent analysis per agent' },
          { mode: 'cooperative', description: 'Peer-aware revision pass' },
          { mode: 'hybrid', description: 'Competitive plus cooperative synthesis' }
        ]
      });
    } finally {
      setIsLoadingCohort(false);
    }
  }, [auth.status, getAccessToken, selectedAgentId, selectedExecutionMode]);

  useEffect(() => {
    fetchCohortRegistry();
  }, [fetchCohortRegistry]);

  return (
    <ErrorBoundary>
      {auth.status === 'initializing' || isLoadingAgent || isLoadingCohort ? (
        <div className="app-container" style={{ 
          display: 'flex', 
          alignItems: 'center', 
          justifyContent: 'center', 
          height: '100vh', 
          flexDirection: 'column', 
          gap: '1rem' 
        }}>
          <Spinner size="large" />
          <p style={{ margin: 0 }}>
            {auth.status === 'initializing' ? 'Preparing your session...' : 'Loading agent and cohort...'}
          </p>
        </div>
      ) : (
        <>
          <AuthenticatedTemplate>
            {agentMetadata && (
              <div className="app-container">
                <AgentChat 
                  agentId={agentMetadata.id}
                  agentName={agentMetadata.name}
                  agentDescription={agentMetadata.description || undefined}
                  agentLogo={agentMetadata.metadata?.logo}
                  starterPrompts={agentMetadata.starterPrompts || undefined}
                  selectedAgentId={selectedAgentId}
                  selectedExecutionMode={selectedExecutionMode}
                  cohortAgents={cohortRegistry?.analystAgents || []}
                  executionModes={cohortRegistry?.executionModes || []}
                  onSelectedAgentChange={setSelectedAgentId}
                  onSelectedExecutionModeChange={setSelectedExecutionMode}
                />
              </div>
            )}
          </AuthenticatedTemplate>
          <UnauthenticatedTemplate>
            <div className="app-container" style={{ 
              display: 'flex', 
              alignItems: 'center', 
              justifyContent: 'center', 
              height: '100vh'
            }}>
              <p>Signing in...</p>
            </div>
          </UnauthenticatedTemplate>
        </>
      )}
    </ErrorBoundary>
  );
}

export default App;
