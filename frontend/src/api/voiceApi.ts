import axios from 'axios';
import { apiBaseUrl } from '../config/api';

const apiClient = axios.create({
  baseURL: apiBaseUrl,
  headers: { 'Content-Type': 'application/json' },
});

export interface VoiceChatSession {
  clientSecret: string;
  realtimeCallsUrl: string;
  model: string;
  voice: string;
  expiresAtUnixSeconds: number | null;
}

/** Mints a short-lived Azure OpenAI Realtime ephemeral session for voice chat mode. */
export async function requestVoiceChatSession(): Promise<VoiceChatSession> {
  const { data } = await apiClient.post<VoiceChatSession>('/voice/session', null, { timeout: 30000 });
  return data;
}

/**
 * Executes the "log_meal" Realtime tool on behalf of the browser - Azure's Realtime API
 * sends the tool call to the client over the WebRTC data channel, and the client (this
 * function) calls back into the backend to actually run it and report the result back.
 */
export async function executeLogMealTool(args: Record<string, unknown>): Promise<{ confirmation: string }> {
  const { data } = await apiClient.post<{ confirmation: string }>('/voice/tools/log-meal', args, { timeout: 15000 });
  return data;
}

/** Executes the "search_food_nutrition" Realtime tool on behalf of the browser. */
export async function executeSearchFoodTool(foodDescription: string): Promise<{ result: string }> {
  const { data } = await apiClient.post<{ result: string }>(
    '/voice/tools/search-food',
    { foodDescription },
    { timeout: 20000 },
  );
  return data;
}

/**
 * Executes the "ask_health_advisor" Realtime tool - forwards the question to the same
 * AdvisorAgent the text chat uses, so voice mode can answer grounded in the user's real
 * meal/exercise/weight/goals history instead of having zero context.
 */
export async function executeAskAdvisorTool(question: string): Promise<{ result: string }> {
  const { data } = await apiClient.post<{ result: string }>(
    '/voice/tools/ask-advisor',
    { question },
    { timeout: 30000 },
  );
  return data;
}
