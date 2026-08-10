import axios from 'axios';
import { apiBaseUrl, attachAuthHeader } from '../config/api';

const apiClient = axios.create({
  baseURL: apiBaseUrl,
  headers: { 'Content-Type': 'application/json' },
});
attachAuthHeader(apiClient);

export interface PendingMeal {
  mealType: string;
  description: string;
  servingSize: string | null;
  calories: number | null;
  proteinGrams: number | null;
  carbsGrams: number | null;
  fatGrams: number | null;
  saturatedFatGrams: number | null;
  sugarGrams: number | null;
  fiberGrams: number | null;
  sodiumMilligrams: number | null;
  potassiumMilligrams: number | null;
  calciumMilligrams: number | null;
  ironMilligrams: number | null;
  magnesiumMilligrams: number | null;
  vitaminAMicrograms: number | null;
  consumedAtIso: string | null;
  sourceBreakdown: string | null;
}

export interface AskResponse {
  reply: string;
  sessionId: string;
  /** Present only right after the agent presents a nutrition breakdown - lets the UI show "Agregar a comida de hoy"/"Guardar en mi catálogo" buttons instead of requiring the user to type "sí". */
  pendingMeal: PendingMeal | null;
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
    // Multi-ingredient meals can trigger several Bing lookups + a confirmation step,
    // observed taking just over 60s - give it real headroom instead of aborting client-side.
    { timeout: 120000 },
  );
  return data;
}

/**
 * Searches for food nutrition by a specific source (catalog, edamam, or internet).
 * Called when user clicks one of the 3 search mode buttons.
 */
export async function searchFoodBySource(
  message: string,
  source: 'catalog' | 'global' | 'edamam' | 'internet',
): Promise<AskResponse> {
  const { data } = await apiClient.post<AskResponse>(
    '/foods/search-source-direct',
    { message, source },
    { timeout: 120000 },
  );
  return data;
}

interface AgentProgressResponse {
  messages: string[];
}

/**
 * Drains any short status lines published so far for this session (e.g. one per
 * ingredient as DietAgent's parallel Bing searches resolve). Meant to be polled while
 * askAgent's request is still in flight to show live progress to the user.
 */
export async function getAgentProgress(sessionId: string): Promise<string[]> {
  const { data } = await apiClient.get<AgentProgressResponse>('/agent/progress', {
    params: { sessionId },
  });
  return data.messages;
}

/** Logs a PendingMeal (from the chat's "Agregar a comida de hoy" button) directly as a
 * meal for today, reusing the same voice-tools endpoint the voice mode already uses -
 * its request shape matches PendingMeal exactly. No LLM round-trip needed. */
export async function logPendingMealToday(meal: PendingMeal): Promise<{ confirmation: string }> {
  const { data } = await apiClient.post<{ confirmation: string }>('/voice/tools/log-meal', meal, { timeout: 15000 });
  return data;
}

/** Saves a PendingMeal (from the chat's "Guardar en mi catálogo" button) into the calling
 * user's own personal food catalog (find-or-create by name). */
export async function savePendingMealToCatalog(meal: PendingMeal): Promise<{ id: number }> {
  const { data } = await apiClient.post<{ id: number }>(
    '/foods/personal/save',
    {
      name: meal.description,
      servingSize: meal.servingSize,
      calories: meal.calories,
      proteinGrams: meal.proteinGrams,
      carbsGrams: meal.carbsGrams,
      fatGrams: meal.fatGrams,
      saturatedFatGrams: meal.saturatedFatGrams,
      sugarGrams: meal.sugarGrams,
      fiberGrams: meal.fiberGrams,
      sodiumMilligrams: meal.sodiumMilligrams,
      potassiumMilligrams: meal.potassiumMilligrams,
      calciumMilligrams: meal.calciumMilligrams,
      ironMilligrams: meal.ironMilligrams,
      magnesiumMilligrams: meal.magnesiumMilligrams,
      vitaminAMicrograms: meal.vitaminAMicrograms,
    },
    { timeout: 15000 },
  );
  return data;
}

/** Saves a PendingMeal as a product (global or local). */
export async function savePendingMealAsProduct(
  meal: PendingMeal,
  scope: 'global' | 'local',
): Promise<{ id: number }> {
  const endpoint = scope === 'global' ? '/foods/items' : '/foods/personal/save';
  const { data } = await apiClient.post<{ id: number }>(
    endpoint,
    {
      name: meal.description,
      servingSize: meal.servingSize,
      calories: meal.calories,
      proteinGrams: meal.proteinGrams,
      carbsGrams: meal.carbsGrams,
      fatGrams: meal.fatGrams,
      saturatedFatGrams: meal.saturatedFatGrams,
      sugarGrams: meal.sugarGrams,
      fiberGrams: meal.fiberGrams,
      sodiumMilligrams: meal.sodiumMilligrams,
      potassiumMilligrams: meal.potassiumMilligrams,
      calciumMilligrams: meal.calciumMilligrams,
      ironMilligrams: meal.ironMilligrams,
      magnesiumMilligrams: meal.magnesiumMilligrams,
      vitaminAMicrograms: meal.vitaminAMicrograms,
    },
    { timeout: 15000 },
  );
  return data;
}
