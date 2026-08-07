import axios from 'axios';
import { apiBaseUrl, attachAuthHeader } from '../config/api';

const apiClient = axios.create({
  baseURL: apiBaseUrl,
  headers: { 'Content-Type': 'application/json' },
});
attachAuthHeader(apiClient);

export interface Meal {
  id: number;
  mealType: 'Breakfast' | 'Lunch' | 'Dinner' | 'Snack';
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
  sourceBreakdown: string | null;
  recordedAtUtc: string;
}

export interface NutritionTotals {
  calories: number;
  proteinGrams: number;
  carbsGrams: number;
  fatGrams: number;
  sugarGrams: number;
  fiberGrams: number;
  sodiumMilligrams: number;
  potassiumMilligrams: number;
}

export interface MealsResponse {
  meals: Meal[];
  totals: NutritionTotals;
}

/**
 * Fetches logged meals + aggregated nutritional totals for the given inclusive LOCAL
 * calendar date range. Converts local-day boundaries to precise UTC instants (instead of
 * sending plain date strings) so meals recorded late in the evening in timezones behind
 * UTC (which land on the *next* UTC calendar day) aren't excluded from "today".
 */
export async function getMeals(from: Date, to: Date): Promise<MealsResponse> {
  const fromInstant = new Date(from.getFullYear(), from.getMonth(), from.getDate(), 0, 0, 0, 0).toISOString();
  const toExclusiveInstant = new Date(to.getFullYear(), to.getMonth(), to.getDate() + 1, 0, 0, 0, 0).toISOString();
  const { data } = await apiClient.get<MealsResponse>('/meals', {
    params: { from: fromInstant, to: toExclusiveInstant },
    timeout: 30000,
  });
  return data;
}

/** Deletes a logged meal by id. */
export async function deleteMeal(id: number): Promise<void> {
  await apiClient.delete(`/meals/${id}`, { timeout: 30000 });
}
