import axios from 'axios';
import { apiBaseUrl } from '../config/api';

const apiClient = axios.create({
  baseURL: apiBaseUrl,
  headers: { 'Content-Type': 'application/json' },
});

export interface AskResponse {
  reply: string;
  sessionId: string;
}

/**
 * Sends a message to the agent, including the current sessionId (if any) so the backend
 * can maintain conversation memory (AgentSession) across turns. Returns the reply plus the
 * sessionId to persist for the next call.
 */
export async function askAgent(message: string, sessionId: string | null): Promise<AskResponse> {
  const { data } = await apiClient.post<AskResponse>(
    '/agent/ask',
    { message, sessionId },
    { timeout: 60000 },
  );
  return data;
}
