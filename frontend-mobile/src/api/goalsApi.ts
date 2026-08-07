import axios from 'axios';
import { apiBaseUrl, attachAuthHeader } from '../config/api';

const apiClient = axios.create({
  baseURL: apiBaseUrl,
  headers: { 'Content-Type': 'application/json' },
});
attachAuthHeader(apiClient);

export type ActivityLevel = 'Sedentary' | 'Light' | 'Moderate' | 'Active' | 'VeryActive';

export interface GoalsProfile {
  heightCm: number | null;
  weightKg: number | null;
  activityLevel: ActivityLevel | null;
  age: number | null;
}

export interface GoalPlanMacros {
  proteinGrams: number | null;
  carbsGrams: number | null;
  fatGrams: number | null;
}

export interface GoalPlanNutrition {
  description: string;
  mealsPerDay: number | null;
  keyRecommendations: string[];
}

export interface GoalPlanExercise {
  description: string;
  daysPerWeek: number | null;
  minutesPerSession: number | null;
  keyRecommendations: string[];
}

export interface GoalPlanMilestone {
  weekNumber: number;
  description: string;
}

export interface GoalPlan {
  summary: string;
  bmi: number;
  bmiCategory: string;
  targetWeightKg: number | null;
  estimatedWeeksToGoal: number | null;
  dailyCalorieTarget: number | null;
  macros: GoalPlanMacros;
  nutritionPlan: GoalPlanNutrition;
  exercisePlan: GoalPlanExercise;
  milestones: GoalPlanMilestone[];
  tips: string[];
}

export interface GoalPlanResponse {
  planId: number | null;
  plan: GoalPlan;
}

export interface GoalPlanCheckIn {
  id: number;
  checkInDate: string; // yyyy-MM-dd
  stepsWalked: number | null;
  followedNutrition: boolean;
  followedExercise: boolean;
  notes: string | null;
}

/** Fetches the default person's stored profile snapshot (height/weight/activity), used to prefill the form. */
export async function getGoalsProfile(): Promise<GoalsProfile> {
  const { data } = await apiClient.get<GoalsProfile>('/goals/profile', { timeout: 15000 });
  return data;
}

/** Saves weight/height/activity level directly (no AI call) - lets the user persist a stat change without regenerating the whole plan. */
export async function saveGoalsProfile(request: {
  weightKg: number;
  heightCm: number;
  activityLevel: ActivityLevel;
  age: number | null;
}): Promise<GoalsProfile> {
  const { data } = await apiClient.post<GoalsProfile>('/goals/profile', request, { timeout: 15000 });
  return data;
}

/** Generates (and persists) a new research-grounded goal plan from the user's current stats + goals text. */
export async function createGoalPlan(request: {
  weightKg: number;
  heightCm: number;
  activityLevel: ActivityLevel;
  goalsText: string;
  age: number | null;
}): Promise<GoalPlanResponse> {
  const { data } = await apiClient.post<GoalPlanResponse>('/goals/plan', request, { timeout: 60000 });
  return data;
}

/** Fetches the most recently generated goal plan, if any, so the page can restore it on reload. */
export async function getLatestGoalPlan(): Promise<GoalPlanResponse> {
  const { data } = await apiClient.get<GoalPlanResponse>('/goals/plan/latest', { timeout: 15000 });
  return data;
}

/** Saves (creates or updates) today's - or a given day's - check-in against a plan: steps walked, whether nutrition/exercise were followed, and optional notes. */
export async function saveGoalPlanCheckIn(
  planId: number,
  checkIn: {
    checkInDate?: string; // defaults to today (server-side) if omitted
    stepsWalked: number | null;
    followedNutrition: boolean;
    followedExercise: boolean;
    notes?: string;
  },
): Promise<GoalPlanCheckIn> {
  const { data } = await apiClient.post<GoalPlanCheckIn>(`/goals/plan/${planId}/checkin`, checkIn, { timeout: 15000 });
  return data;
}

/** Fetches the check-in history (default last 14 days) for a plan, newest first. */
export async function getGoalPlanCheckInHistory(planId: number, days = 14): Promise<GoalPlanCheckIn[]> {
  const { data } = await apiClient.get<{ checkIns: GoalPlanCheckIn[] }>(`/goals/plan/${planId}/checkins`, {
    params: { days },
    timeout: 15000,
  });
  return data.checkIns;
}
