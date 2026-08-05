import axios from 'axios';
import { apiBaseUrl } from '../config/api';

const apiClient = axios.create({
  baseURL: apiBaseUrl,
  headers: { 'Content-Type': 'application/json' },
});

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

/** Deletes a logged exercise entry by id. */
export async function deleteExerciseEntry(id: number): Promise<void> {
  await apiClient.delete(`/exercise/${id}`, { timeout: 15000 });
}
