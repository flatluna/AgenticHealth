import axios from 'axios';
import { apiBaseUrl, attachAuthHeader } from '../config/api';

const apiClient = axios.create({
  baseURL: apiBaseUrl,
  headers: { 'Content-Type': 'application/json' },
});
attachAuthHeader(apiClient);

export interface ExerciseEntry {
  id: number;
  description: string;
  durationMinutes: number;
  caloriesBurned: number | null;
  recordedAtUtc: string;
}

export interface ExerciseHistory {
  entries: ExerciseEntry[];
  totalMinutes: number;
}

/** Fetches the exercise log history for the last N days (default 90), newest first. */
export async function getExerciseHistory(days = 90): Promise<ExerciseHistory> {
  const { data } = await apiClient.get<ExerciseHistory>('/exercise', {
    params: { days },
    timeout: 15000,
  });
  return data;
}

/** Logs a new exercise entry (defaults to now if no date given). */
export async function logExercise(
  description: string,
  durationMinutes: number,
  caloriesBurned?: number | null,
  recordedAtIso?: string,
): Promise<ExerciseEntry> {
  const { data } = await apiClient.post<ExerciseEntry>(
    '/exercise',
    { description, durationMinutes, caloriesBurned, recordedAtIso },
    { timeout: 15000 },
  );
  return data;
}

export interface ExerciseEstimate {
  suggestedName: string;
  estimatedCaloriesBurned: number;
}

/** Asks the AI to estimate calories burned + suggest a name for a free-text activity
 * description, WITHOUT saving anything - preview only for the "crea tu propio ejercicio" flow. */
export async function estimateExercise(description: string, durationMinutes: number): Promise<ExerciseEstimate> {
  const { data } = await apiClient.post<ExerciseEstimate>(
    '/exercise/estimate',
    { description, durationMinutes },
    { timeout: 20000 },
  );
  return data;
}

/** Deletes a logged exercise entry by id. */
export async function deleteExerciseEntry(id: number): Promise<void> {
  await apiClient.delete(`/exercise/${id}`, { timeout: 15000 });
}

export interface PersonalExercise {
  id: number;
  name: string;
  durationMinutes: number;
  caloriesBurned: number | null;
  timesLogged: number;
}

/** Fetches THIS user's own saved custom exercises (personal catalog, not shared globally
 * like Productos), most-logged first. */
export async function getPersonalExerciseCatalog(): Promise<PersonalExercise[]> {
  const { data } = await apiClient.get<PersonalExercise[]>('/exercise/catalog', { timeout: 15000 });
  return data;
}

/** Saves an AI-estimated "crea tu propio ejercicio" activity to the user's own catalog AND
 * logs it for today in one step - only called once the user accepts the preview. */
export async function saveCustomExercise(
  name: string,
  durationMinutes: number,
  caloriesBurned: number | null,
  recordedAtIso?: string,
): Promise<{ exerciseLogId: number; personalExerciseId: number }> {
  const { data } = await apiClient.post<{ exerciseLogId: number; personalExerciseId: number }>(
    '/exercise/custom/save',
    { name, durationMinutes, caloriesBurned, recordedAtIso },
    { timeout: 15000 },
  );
  return data;
}

/** Re-logs an existing entry from the user's own catalog (optionally with a different
 * duration, scaling calories proportionally) - no AI re-estimation needed. */
export async function logPersonalExercise(
  id: number,
  durationMinutes?: number,
  recordedAtIso?: string,
): Promise<{ exerciseLogId: number }> {
  const { data } = await apiClient.post<{ exerciseLogId: number }>(
    `/exercise/catalog/${id}/log`,
    { durationMinutes, recordedAtIso },
    { timeout: 15000 },
  );
  return data;
}

/** Removes a saved custom exercise from the user's own catalog (past logged entries are unaffected). */
export async function deletePersonalExercise(id: number): Promise<void> {
  await apiClient.delete(`/exercise/catalog/${id}`, { timeout: 15000 });
}
