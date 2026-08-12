import axios from 'axios';
import { apiBaseUrl, attachAuthHeader } from '../config/api';

const apiClient = axios.create({
  baseURL: apiBaseUrl,
  headers: { 'Content-Type': 'application/json' },
});
attachAuthHeader(apiClient);

export interface WeightEntry {
  id: number;
  weightKg: number;
  recordedAtUtc: string;
}

export interface WeightHistory {
  entries: WeightEntry[];
  latestWeightKg: number | null;
  changeKg: number | null;
}

/** Fetches the weight history for the last N days (default 90), oldest first. */
export async function getWeightHistory(days = 90): Promise<WeightHistory> {
  const { data } = await apiClient.get<unknown>('/weight', {
    params: { days },
    timeout: 15000,
  });

  const payload = (data ?? {}) as Record<string, unknown>;
  const entries = Array.isArray(payload.entries) ? (payload.entries as WeightEntry[]) : [];
  return {
    entries,
    latestWeightKg: typeof payload.latestWeightKg === 'number' ? payload.latestWeightKg : null,
    changeKg: typeof payload.changeKg === 'number' ? payload.changeKg : null,
  };
}

/** Logs a new weight entry (defaults to now if no date given). */
export async function logWeight(weightKg: number, recordedAtIso?: string): Promise<WeightEntry> {
  const { data } = await apiClient.post<WeightEntry>(
    '/weight',
    { weightKg, recordedAtIso },
    { timeout: 15000 },
  );
  return data;
}

/** Deletes a logged weight entry by id. */
export async function deleteWeightEntry(id: number): Promise<void> {
  await apiClient.delete(`/weight/${id}`, { timeout: 15000 });
}
