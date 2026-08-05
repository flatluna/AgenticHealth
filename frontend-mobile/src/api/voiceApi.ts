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
export async function requestVoiceChatSession(userName?: string | null): Promise<VoiceChatSession> {
  const { data } = await apiClient.post<VoiceChatSession>('/voice/session', { userName }, { timeout: 30000 });
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
 * Executes the "log_exercise" Realtime tool on behalf of the browser - writes to the
 * same ExerciseLogs table the Ejercicios page and text chat use.
 */
export async function executeLogExerciseTool(args: Record<string, unknown>): Promise<{ confirmation: string }> {
  const { data } = await apiClient.post<{ confirmation: string }>('/voice/tools/log-exercise', args, { timeout: 15000 });
  return data;
}

/**
 * Executes the "ask_health_advisor" Realtime tool - forwards the question to the same
 * AdvisorAgent the text chat uses, so voice mode can answer grounded in the user's real
 * meal/exercise/weight/goals history instead of having zero context.
 */
export async function executeAskAdvisorTool(question: string, userName?: string | null): Promise<{ result: string }> {
  const { data } = await apiClient.post<{ result: string }>(
    '/voice/tools/ask-advisor',
    { question, userName },
    { timeout: 30000 },
  );
  return data;
}

/**
 * Executes the "get_recent_meals" Realtime tool - returns the user's recently logged
 * meals so the model can reuse them when the user references a past meal instead of
 * describing it again (ej. "lo mismo que ayer").
 */
export async function executeGetRecentMealsTool(daysBack?: number): Promise<{ result: string }> {
  const { data } = await apiClient.post<{ result: string }>(
    '/voice/tools/get-recent-meals',
    { daysBack },
    { timeout: 20000 },
  );
  return data;
}

/**
 * Executes the "delete_meal" Realtime tool - deletes a previously logged meal by ID
 * (obtained from "get_recent_meals"), same as the Alimentos page's delete action.
 */
export async function executeDeleteMealTool(mealId: number): Promise<{ confirmation: string }> {
  const { data } = await apiClient.post<{ confirmation: string }>(
    '/voice/tools/delete-meal',
    { mealId },
    { timeout: 15000 },
  );
  return data;
}
