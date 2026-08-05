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
 * can maintain conversation memory (AgentSession) across turns, and the user's display
 * name so every specialist agent can answer identity/profile questions ("¿cómo me
 * llamo?") without guessing. Returns the reply plus the sessionId to persist for the
 * next call.
 */
export async function askAgent(message: string, sessionId: string | null, userName?: string | null): Promise<AskResponse> {
  const { data } = await apiClient.post<AskResponse>(
    '/agent/ask',
    { message, sessionId, userName },
    { timeout: 60000 },
  );
  return data;
}
