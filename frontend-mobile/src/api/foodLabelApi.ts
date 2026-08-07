import axios from 'axios';
import { apiBaseUrl, attachAuthHeader } from '../config/api';

const apiClient = axios.create({
  baseURL: apiBaseUrl,
});
attachAuthHeader(apiClient);

export interface FoodLabelExtractionResult {
  isValidLabel: boolean;
  reason: string | null;
  name: string;
  brand: string | null;
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
  ingredientsText: string | null;
}

export type MealType = 'Breakfast' | 'Lunch' | 'Dinner' | 'Snack';

export interface SaveFoodLabelRequest {
  mealType: MealType;
  name: string;
  brand: string | null;
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
  ingredientsText: string | null;
  consumedAtIso: string | null;
  quantity: number;
}

export interface SaveFoodLabelResponse {
  mealLogId: number;
  foodItemId: number;
}

/**
 * Uploads a photo of a food's nutrition label as raw bytes (Content-Type set to the file's
 * own mime type, no multipart wrapping - matches FoodLabelFunction's contract) and returns
 * the AI-extracted nutrition data for the user to review. Nothing is saved/logged yet.
 */
export async function extractFoodLabel(imageFile: File): Promise<FoodLabelExtractionResult> {
  const { data } = await apiClient.post<FoodLabelExtractionResult>('/foods/label/extract', imageFile, {
    headers: { 'Content-Type': imageFile.type || 'image/jpeg' },
    timeout: 60000,
  });
  return data;
}

/** Confirms the (possibly user-edited) extracted label data: logs it as a meal and stores/reuses it in the global food database. */
export async function saveFoodLabelMeal(request: SaveFoodLabelRequest): Promise<SaveFoodLabelResponse> {
  const { data } = await apiClient.post<SaveFoodLabelResponse>('/foods/label/save', request, {
    headers: { 'Content-Type': 'application/json' },
    timeout: 30000,
  });
  return data;
}

export interface FoodItem {
  id: number;
  name: string;
  brand: string | null;
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
  ingredientsText: string | null;
  timesLogged: number;
}

/** Lists products from the global food database (created by any user via the label-scan
 * feature), optionally filtered by name/brand - backs the "Productos" page. */
export async function getFoodItems(search?: string): Promise<FoodItem[]> {
  const { data } = await apiClient.get<FoodItem[]>('/foods/items', {
    params: search ? { q: search } : undefined,
    timeout: 15000,
  });
  return data;
}

/** Logs an already-known global product as a meal for the current user, no re-scanning needed. */
export async function logFoodItem(
  foodItemId: number,
  request: { mealType: MealType; consumedAtIso: string | null; quantity: number },
): Promise<{ mealLogId: number }> {
  const { data } = await apiClient.post<{ mealLogId: number }>(`/foods/items/${foodItemId}/log`, request, {
    headers: { 'Content-Type': 'application/json' },
    timeout: 15000,
  });
  return data;
}

export interface PersonalFoodItem {
  id: number;
  name: string;
  description: string | null;
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
  timesLogged: number;
}

/** Lists THIS user's own saved catalog entries (Data/PersonalFoodItem.cs), populated via the
 * chat's "Guardar en mi catálogo" button - unlike getFoodItems, this is per-person, not shared. */
export async function getPersonalFoodItems(): Promise<PersonalFoodItem[]> {
  const { data } = await apiClient.get<PersonalFoodItem[]>('/foods/personal', { timeout: 15000 });
  return data;
}

/** Logs an existing entry from the user's personal catalog as a meal, no re-computation needed. */
export async function logPersonalFoodItem(
  personalFoodItemId: number,
  request: { mealType: MealType; consumedAtIso: string | null; quantity: number },
): Promise<{ mealLogId: number }> {
  const { data } = await apiClient.post<{ mealLogId: number }>(`/foods/personal/${personalFoodItemId}/log`, request, {
    headers: { 'Content-Type': 'application/json' },
    timeout: 15000,
  });
  return data;
}
