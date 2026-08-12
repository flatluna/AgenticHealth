import { useEffect, useMemo, useState } from 'react';
import {
  BarChart3,
  Beef,
  Candy,
  Droplets,
  Flame,
  Footprints,
  Gauge,
  Target,
  Wheat,
  Zap,
} from 'lucide-react';
import { getExerciseHistory } from '../api/exerciseApi';
import { getMeals } from '../api/mealsApi';

type ViewMode = 'week' | 'month';

type PeriodBucket = {
  key: string;
  label: string;
  calories: number;
  protein: number;
  carbs: number;
  fat: number;
  sugar: number;
  fiber: number;
  sodium: number;
  potassium: number;
  exerciseMinutes: number;
  exerciseCalories: number;
};

function startOfDay(date: Date): Date {
  const next = new Date(date);
  next.setHours(0, 0, 0, 0);
  return next;
}

function addDays(date: Date, days: number): Date {
  const next = new Date(date);
  next.setDate(next.getDate() + days);
  return next;
}

function getStartOfWeek(date: Date): Date {
  const next = startOfDay(date);
  const day = next.getDay();
  const diff = day === 0 ? -6 : 1 - day;
  next.setDate(next.getDate() + diff);
  return next;
}

function monthFullLabel(date: Date): string {
  return new Intl.DateTimeFormat('es', { month: 'long', year: 'numeric', timeZone: 'America/Chicago' }).format(date);
}

function getWeekNumberInMonth(date: Date): number {
  const firstDay = new Date(date.getFullYear(), date.getMonth(), 1);
  const day = date.getDate();
  return Math.ceil((day + firstDay.getDay()) / 7);
}

function weekLabel(date: Date): string {
  const start = getStartOfWeek(date);
  const end = addDays(start, 6);
  const monthFormatter = new Intl.DateTimeFormat('es', { month: 'long', timeZone: 'America/Chicago' });
  const weekNumber = getWeekNumberInMonth(start);
  const sameMonth = start.getMonth() === end.getMonth();

  if (sameMonth) {
    return `${monthFormatter.format(start)} • Semana ${weekNumber}`;
  }

  return `${monthFormatter.format(start)} / ${monthFormatter.format(end)} • Semana ${weekNumber}`;
}

function buildBuckets(viewMode: ViewMode, anchorDate: Date = new Date()): PeriodBucket[] {
  const today = startOfDay(anchorDate);
  const buckets = new Map<string, PeriodBucket>();

  if (viewMode === 'month') {
    const monthStart = new Date(today.getFullYear(), today.getMonth(), 1);
    const monthEnd = new Date(today.getFullYear(), today.getMonth() + 1, 0);
    let cursor = getStartOfWeek(monthStart);
    let weekIndex = 1;

    while (cursor <= monthEnd) {
      const weekStart = new Date(cursor);
      const key = `month-week-${weekStart.toISOString()}`;
      buckets.set(key, {
        key,
        label: `Semana ${weekIndex}`,
        calories: 0,
        protein: 0,
        carbs: 0,
        fat: 0,
        sugar: 0,
        fiber: 0,
        sodium: 0,
        potassium: 0,
        exerciseMinutes: 0,
        exerciseCalories: 0,
      });

      cursor = addDays(weekStart, 7);
      weekIndex += 1;
      if (weekStart.getMonth() > monthEnd.getMonth() && weekStart.getDate() > 20) break;
    }

    return Array.from(buckets.values());
  }

  const bucketCount = 6;
  for (let i = bucketCount - 1; i >= 0; i -= 1) {
    const start = getStartOfWeek(addDays(today, -(i * 7 + 6)));
    const label = weekLabel(start);
    const key = `week-${start.toISOString()}`;
    buckets.set(key, {
      key,
      label,
      calories: 0,
      protein: 0,
      carbs: 0,
      fat: 0,
      sugar: 0,
      fiber: 0,
      sodium: 0,
      potassium: 0,
      exerciseMinutes: 0,
      exerciseCalories: 0,
    });
  }

  return Array.from(buckets.values());
}

function fillBucketsFromMeals(buckets: PeriodBucket[], meals: any[]): void {
  for (const meal of meals) {
    const current = new Date(meal.recordedAtUtc);

    const target = buckets.find((bucket) => {
      if (bucket.key.startsWith('week-')) {
        const start = new Date(bucket.key.slice(5));
        const end = addDays(start, 6);
        return current >= start && current <= end;
      }

      if (bucket.key.startsWith('month-week-')) {
        const start = new Date(bucket.key.replace('month-week-', ''));
        const end = addDays(start, 6);
        return current >= start && current <= end;
      }

      const [year, month] = bucket.key.replace('month-', '').split('-').map(Number);
      return current.getFullYear() === year && current.getMonth() === month;
    });

    if (!target) continue;

    target.calories += Number(meal.calories ?? 0);
    target.protein += Number(meal.proteinGrams ?? 0);
    target.carbs += Number(meal.carbsGrams ?? 0);
    target.fat += Number(meal.fatGrams ?? 0);
    target.sugar += Number(meal.sugarGrams ?? 0);
    target.fiber += Number(meal.fiberGrams ?? 0);
    target.sodium += Number(meal.sodiumMilligrams ?? 0);
    target.potassium += Number(meal.potassiumMilligrams ?? 0);
  }
}

function fillBucketsFromExercises(buckets: PeriodBucket[], entries: any[]): void {
  for (const entry of entries) {
    const current = new Date(entry.recordedAtUtc);
    const target = buckets.find((bucket) => {
      if (bucket.key.startsWith('week-')) {
        const start = new Date(bucket.key.slice(5));
        const end = addDays(start, 6);
        return current >= start && current <= end;
      }

      if (bucket.key.startsWith('month-week-')) {
        const start = new Date(bucket.key.replace('month-week-', ''));
        const end = addDays(start, 6);
        return current >= start && current <= end;
      }

      const [year, month] = bucket.key.replace('month-', '').split('-').map(Number);
      return current.getFullYear() === year && current.getMonth() === month;
    });

    if (!target) continue;

    target.exerciseMinutes += Number(entry.durationMinutes ?? 0);
    target.exerciseCalories += Number(entry.caloriesBurned ?? 0);
  }
}

function DonutChart({ values }: { values: Array<{ label: string; value: number; color: string }> }) {
  const total = values.reduce((sum, item) => sum + item.value, 0) || 1;

  let accumulated = 0;
  const segments = values.map((item) => {
    const start = accumulated;
    accumulated += (item.value / total) * 100;
    return {
      ...item,
      start,
      end: accumulated,
    };
  });

  const gradient = segments.length
    ? `conic-gradient(${segments.map((segment) => `${segment.color} ${segment.start}% ${segment.end}%`).join(', ')})`
    : 'conic-gradient(#e2e8f0 0% 100%)';

  return (
    <div className="flex items-center gap-4">
      <div className="relative h-28 w-28 shrink-0 rounded-full" style={{ background: gradient }}>
        <div className="absolute inset-[18%] rounded-full bg-[var(--card-bg)]" />
        <div className="absolute inset-0 flex items-center justify-center text-sm font-semibold text-[var(--text-primary)]">
          {Math.round(total)}
        </div>
      </div>

      <div className="space-y-2 text-xs text-[var(--text-secondary)]">
        {segments.map((segment) => (
          <div key={segment.label} className="flex items-center gap-2">
            <span className="h-2.5 w-2.5 rounded-full" style={{ backgroundColor: segment.color }} />
            <span className="flex-1">{segment.label}</span>
            <span className="font-medium text-[var(--text-primary)]">{Math.round(segment.value)}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function BarHistogram({ bars }: { bars: Array<{ label: string; value: number; color: string }> }) {
  const maxValue = Math.max(...bars.map((bar) => bar.value), 1);

  return (
    <div className="mt-4 grid grid-cols-6 gap-2 sm:gap-3">
      {bars.map((bar) => (
        <div key={bar.label} className="flex flex-col items-center gap-2 text-center">
          <div className="flex h-28 w-full items-end justify-center">
            <div
              className="w-full rounded-t-xl transition-all"
              style={{
                height: `${Math.max((bar.value / maxValue) * 100, 8)}%`,
                background: `linear-gradient(180deg, ${bar.color} 0%, ${bar.color}dd 100%)`,
              }}
            />
          </div>
          <span className="text-[10px] text-[var(--text-muted)]">{bar.label}</span>
          <span className="text-[10px] font-medium text-[var(--text-primary)]">{Math.round(bar.value)}</span>
        </div>
      ))}
    </div>
  );
}

export function DashboardPage() {
  const [viewMode, setViewMode] = useState<ViewMode>('month');
  const [loading, setLoading] = useState(true);
  const [allMeals, setAllMeals] = useState<any[]>([]);
  const [allExercises, setAllExercises] = useState<any[]>([]);
  const [selectedMonth, setSelectedMonth] = useState<Date>(new Date());

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      try {
        const start = startOfDay(addDays(new Date(), -180));
        const end = startOfDay(new Date());

        const [mealsResult, exerciseResult] = await Promise.all([
          getMeals(start, end),
          getExerciseHistory(180),
        ]);

        if (!cancelled) {
          setAllMeals(mealsResult.meals ?? []);
          setAllExercises(exerciseResult.entries ?? []);
        }
      } catch {
        if (!cancelled) {
          setAllMeals([]);
          setAllExercises([]);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };

    void load();

    return () => {
      cancelled = true;
    };
  }, []);

  const monthLabelText = monthFullLabel(selectedMonth);

  const activeBuckets = useMemo(() => {
    const result = buildBuckets(viewMode, selectedMonth);
    fillBucketsFromMeals(result, allMeals);
    fillBucketsFromExercises(result, allExercises);
    return result;
  }, [allMeals, allExercises, selectedMonth, viewMode]);

  const totals = useMemo(() => {
    return activeBuckets.reduce(
      (acc, bucket) => {
        acc.calories += bucket.calories;
        acc.protein += bucket.protein;
        acc.carbs += bucket.carbs;
        acc.fat += bucket.fat;
        acc.sugar += bucket.sugar;
        acc.fiber += bucket.fiber;
        acc.sodium += bucket.sodium;
        acc.potassium += bucket.potassium;
        acc.exerciseMinutes += bucket.exerciseMinutes;
        acc.exerciseCalories += bucket.exerciseCalories;
        return acc;
      },
      {
        calories: 0,
        protein: 0,
        carbs: 0,
        fat: 0,
        sugar: 0,
        fiber: 0,
        sodium: 0,
        potassium: 0,
        exerciseMinutes: 0,
        exerciseCalories: 0,
      },
    );
  }, [activeBuckets]);

  const activeDaysWithRecords = useMemo(() => {
    const uniqueDays = new Set(
      allMeals
        .filter((meal) => {
          const date = new Date(meal.recordedAtUtc);
          if (viewMode === 'week') {
            const weekStart = getStartOfWeek(new Date());
            const weekEnd = addDays(weekStart, 6);
            return date >= weekStart && date <= weekEnd;
          }

          const monthStart = new Date(selectedMonth.getFullYear(), selectedMonth.getMonth(), 1);
          const monthEnd = new Date(selectedMonth.getFullYear(), selectedMonth.getMonth() + 1, 0, 23, 59, 59, 999);
          return date >= monthStart && date <= monthEnd;
        })
        .map((meal) => {
          const date = new Date(meal.recordedAtUtc);
          return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
        }),
    );

    return Math.max(uniqueDays.size, 1);
  }, [allMeals, selectedMonth, viewMode]);

  const averagePerBucket = useMemo(() => {
    return {
      calories: totals.calories / activeDaysWithRecords,
      protein: totals.protein / activeDaysWithRecords,
      carbs: totals.carbs / activeDaysWithRecords,
      exerciseCalories: totals.exerciseCalories / activeDaysWithRecords,
    };
  }, [activeDaysWithRecords, totals]);

  const macroSegments = [
    { label: 'Proteína', value: totals.protein, color: '#f97316' },
    { label: 'Carbs', value: totals.carbs, color: '#3b82f6' },
    { label: 'Grasa', value: totals.fat, color: '#8b5cf6' },
  ];

  const calorieBars = activeBuckets.map((bucket) => ({
    label: bucket.label,
    value: bucket.calories,
    color: '#38bdf8',
  }));

  const exerciseBars = activeBuckets.map((bucket) => ({
    label: bucket.label,
    value: bucket.exerciseMinutes,
    color: '#a78bfa',
  }));

  const nutrientBars = [
    { label: 'Azúcar', value: totals.sugar, color: '#f472b6' },
    { label: 'Fibra', value: totals.fiber, color: '#34d399' },
    { label: 'Sodio', value: totals.sodium, color: '#fbbf24' },
    { label: 'Potasio', value: totals.potassium, color: '#60a5fa' },
  ];

  const getDayKey = (input: string | Date): string => {
    const date = new Date(input);
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  };

  const dailyAverageWeek = useMemo(() => {
    const weekStart = getStartOfWeek(new Date());
    const weekEnd = addDays(weekStart, 6);
    const weekMeals = allMeals.filter((meal) => {
      const date = new Date(meal.recordedAtUtc);
      return date >= weekStart && date <= weekEnd;
    });

    const daysWithData = new Set(weekMeals.map((meal) => getDayKey(meal.recordedAtUtc))).size || 1;
    const totalsInRange = weekMeals.reduce(
      (acc, meal) => {
        acc.calories += Number(meal.calories ?? 0);
        acc.protein += Number(meal.proteinGrams ?? 0);
        acc.carbs += Number(meal.carbsGrams ?? 0);
        acc.fat += Number(meal.fatGrams ?? 0);
        acc.sugar += Number(meal.sugarGrams ?? 0);
        acc.fiber += Number(meal.fiberGrams ?? 0);
        acc.sodium += Number(meal.sodiumMilligrams ?? 0);
        acc.potassium += Number(meal.potassiumMilligrams ?? 0);
        return acc;
      },
      { calories: 0, protein: 0, carbs: 0, fat: 0, sugar: 0, fiber: 0, sodium: 0, potassium: 0 },
    );

    return [
      { label: 'Calorías', value: totalsInRange.calories / daysWithData, unit: 'kcal' },
      { label: 'Proteína', value: totalsInRange.protein / daysWithData, unit: 'g' },
      { label: 'Carbs', value: totalsInRange.carbs / daysWithData, unit: 'g' },
      { label: 'Grasa', value: totalsInRange.fat / daysWithData, unit: 'g' },
      { label: 'Azúcar', value: totalsInRange.sugar / daysWithData, unit: 'g' },
      { label: 'Fibra', value: totalsInRange.fiber / daysWithData, unit: 'g' },
      { label: 'Sodio', value: totalsInRange.sodium / daysWithData, unit: 'mg' },
      { label: 'Potasio', value: totalsInRange.potassium / daysWithData, unit: 'mg' },
    ];
  }, [allMeals]);

  const dailyAverageMonth = useMemo(() => {
    const monthStart = new Date(selectedMonth.getFullYear(), selectedMonth.getMonth(), 1);
    const monthEnd = new Date(selectedMonth.getFullYear(), selectedMonth.getMonth() + 1, 0, 23, 59, 59, 999);

    const monthMeals = allMeals.filter((meal) => {
      const mealDate = new Date(meal.recordedAtUtc);
      return mealDate >= monthStart && mealDate <= monthEnd;
    });

    const daysWithData = new Set(monthMeals.map((meal) => getDayKey(meal.recordedAtUtc))).size || 1;
    const monthTotals = monthMeals.reduce(
      (acc, meal) => {
        acc.calories += Number(meal.calories ?? 0);
        acc.protein += Number(meal.proteinGrams ?? 0);
        acc.carbs += Number(meal.carbsGrams ?? 0);
        acc.fat += Number(meal.fatGrams ?? 0);
        acc.sugar += Number(meal.sugarGrams ?? 0);
        acc.fiber += Number(meal.fiberGrams ?? 0);
        acc.sodium += Number(meal.sodiumMilligrams ?? 0);
        acc.potassium += Number(meal.potassiumMilligrams ?? 0);
        return acc;
      },
      { calories: 0, protein: 0, carbs: 0, fat: 0, sugar: 0, fiber: 0, sodium: 0, potassium: 0 },
    );

    return [
      { label: 'Calorías', value: monthTotals.calories / daysWithData, unit: 'kcal' },
      { label: 'Proteína', value: monthTotals.protein / daysWithData, unit: 'g' },
      { label: 'Carbs', value: monthTotals.carbs / daysWithData, unit: 'g' },
      { label: 'Grasa', value: monthTotals.fat / daysWithData, unit: 'g' },
      { label: 'Azúcar', value: monthTotals.sugar / daysWithData, unit: 'g' },
      { label: 'Fibra', value: monthTotals.fiber / daysWithData, unit: 'g' },
      { label: 'Sodio', value: monthTotals.sodium / daysWithData, unit: 'mg' },
      { label: 'Potasio', value: monthTotals.potassium / daysWithData, unit: 'mg' },
    ];
  }, [allMeals, selectedMonth]);

  return (
    <div className="h-full overflow-y-auto bg-[var(--app-bg)] px-3 py-4 sm:px-4">
      <div className="mx-auto max-w-5xl space-y-4">
        <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-3 shadow-sm">
          <div className="flex items-center justify-between gap-3">
            <div>
              <p className="text-[10px] uppercase tracking-[0.18em] text-[var(--text-muted)]">Dashboard</p>
              <h1 className="mt-1 text-base font-semibold text-[var(--text-primary)]">{monthLabelText}</h1>
            </div>
            <div className="inline-flex rounded-full bg-[var(--app-bg)] p-1">
              {(['week', 'month'] as ViewMode[]).map((mode) => (
                <button
                  key={mode}
                  type="button"
                  onClick={() => setViewMode(mode)}
                  className={`rounded-full px-3 py-1.5 text-xs font-medium transition ${
                    viewMode === mode ? 'bg-[var(--accent)] text-white' : 'text-[var(--text-muted)]'
                  }`}
                >
                  {mode === 'week' ? 'Semana' : 'Mes'}
                </button>
              ))}
            </div>
          </div>

          <div className="mt-3 flex items-center justify-between gap-2 rounded-xl border border-[var(--card-border)] bg-[var(--app-bg)] px-2 py-1.5">
            <button
              type="button"
              onClick={() => setSelectedMonth(new Date(selectedMonth.getFullYear(), selectedMonth.getMonth() - 1, 1))}
              className="rounded-full border border-[var(--card-border)] px-2 py-1 text-xs text-[var(--text-secondary)]"
            >
              ←
            </button>
            <h2 className="text-xs font-medium text-[var(--text-secondary)]">Mes</h2>
            <button
              type="button"
              onClick={() => setSelectedMonth(new Date(selectedMonth.getFullYear(), selectedMonth.getMonth() + 1, 1))}
              className="rounded-full border border-[var(--card-border)] px-2 py-1 text-xs text-[var(--text-secondary)]"
            >
              →
            </button>
          </div>
        </div>

        {loading ? (
          <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-6 text-sm text-[var(--text-muted)]">
            Cargando métricas…
          </div>
        ) : activeBuckets.length === 0 ? (
          <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-6 text-sm text-[var(--text-muted)]">
            No hay datos suficientes para mostrar el dashboard.
          </div>
        ) : (
          <>
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              <MetricCard icon={<Flame className="h-4 w-4" />} label="Global • calorías" value={`${Math.round(totals.calories)} kcal`} accent="text-orange-500" />
              <MetricCard icon={<Beef className="h-4 w-4" />} label="Global • proteína" value={`${Math.round(totals.protein)} g`} accent="text-red-500" />
              <MetricCard icon={<Footprints className="h-4 w-4" />} label="Prom. día con datos • semana" value={`${Math.round(dailyAverageWeek[0]?.value ?? 0)} kcal`} accent="text-violet-500" />
              <MetricCard icon={<Candy className="h-4 w-4" />} label="Prom. día con datos • mes" value={`${Math.round(dailyAverageMonth[0]?.value ?? 0)} kcal`} accent="text-pink-500" />
            </div>

            <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
              <div className="flex items-center gap-2">
                <Target className="h-4 w-4 text-emerald-500" />
                <h2 className="text-sm font-semibold text-[var(--text-secondary)]">Promedios diarios</h2>
              </div>
              <div className="mt-4 grid gap-4 lg:grid-cols-2">
                <DailyAveragePanel title={`Promedio por día con datos • esta semana • ${activeDaysWithRecords} día${activeDaysWithRecords === 1 ? '' : 's'} con registro`} values={dailyAverageWeek} />
                <DailyAveragePanel title={`Promedio por día con datos • ${monthLabelText} • ${new Set(allMeals.filter((meal) => { const date = new Date(meal.recordedAtUtc); const monthStart = new Date(selectedMonth.getFullYear(), selectedMonth.getMonth(), 1); const monthEnd = new Date(selectedMonth.getFullYear(), selectedMonth.getMonth() + 1, 0, 23, 59, 59, 999); return date >= monthStart && date <= monthEnd; }).map((meal) => getDayKey(meal.recordedAtUtc))).size} días con registro`} values={dailyAverageMonth} />
              </div>
            </div>

            <div className="grid gap-4 lg:grid-cols-2">
              <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
                <div className="flex items-center gap-2">
                  <Gauge className="h-4 w-4 text-sky-500" />
                  <h2 className="text-sm font-semibold text-[var(--text-secondary)]">Macros</h2>
                </div>
                <div className="mt-4">
                  <DonutChart values={macroSegments} />
                </div>
              </div>

              <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
                <div className="flex items-center gap-2">
                  <BarChart3 className="h-4 w-4 text-violet-500" />
                  <h2 className="text-sm font-semibold text-[var(--text-secondary)]">Nutrientes clave</h2>
                </div>
                <div className="mt-3 space-y-3">
                  {nutrientBars.map((item) => (
                    <div key={item.label}>
                      <div className="mb-1 flex justify-between text-[11px] text-[var(--text-muted)]">
                        <span>{item.label}</span>
                        <span className="font-medium text-[var(--text-primary)]">{Math.round(item.value)}</span>
                      </div>
                      <div className="h-2.5 overflow-hidden rounded-full bg-[var(--app-bg)]">
                        <div
                          className="h-full rounded-full"
                          style={{
                            width: `${Math.min(100, (item.value / Math.max(...nutrientBars.map((n) => n.value), 1)) * 100)}%`,
                            backgroundColor: item.color,
                          }}
                        />
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </div>

            <div className="grid gap-4 lg:grid-cols-2">
              <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
                <div className="flex items-center gap-2">
                  <Flame className="h-4 w-4 text-orange-500" />
                  <h2 className="text-sm font-semibold text-[var(--text-secondary)]">
                    {viewMode === 'week' ? 'Calorías por semana' : `Semanas de ${monthLabelText}`}
                  </h2>
                </div>
                <div className="mt-2 text-[11px] text-[var(--text-muted)]">
                  Promedio por semana: {Math.round(averagePerBucket.calories)} kcal • Promedio por mes: {Math.round(averagePerBucket.calories * Math.max(4, activeBuckets.length))} kcal
                </div>
                <BarHistogram bars={calorieBars} />
              </div>

              <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
                <div className="flex items-center gap-2">
                  <Footprints className="h-4 w-4 text-violet-500" />
                  <h2 className="text-sm font-semibold text-[var(--text-secondary)]">
                    {viewMode === 'week' ? 'Ejercicio por semana' : `Ejercicio en ${monthLabelText}`}
                  </h2>
                </div>
                <div className="mt-2 text-[11px] text-[var(--text-muted)]">
                  Promedio por semana: {Math.round(averagePerBucket.exerciseCalories)} kcal • Promedio por mes: {Math.round(averagePerBucket.exerciseCalories * Math.max(4, activeBuckets.length))} kcal
                </div>
                <BarHistogram bars={exerciseBars} />
              </div>
            </div>

            <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
              <div className="flex items-center gap-2">
                <Target className="h-4 w-4 text-emerald-500" />
                <h2 className="text-sm font-semibold text-[var(--text-secondary)]">Resumen y objetivos</h2>
              </div>
              <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                <SummaryRow icon={<Beef className="h-4 w-4 text-red-500" />} label="Proteína" value={`${Math.round(totals.protein)} g`} />
                <SummaryRow icon={<Wheat className="h-4 w-4 text-amber-500" />} label="Carbohidratos" value={`${Math.round(totals.carbs)} g`} />
                <SummaryRow icon={<Droplets className="h-4 w-4 text-cyan-500" />} label="Fibra" value={`${Math.round(totals.fiber)} g`} />
                <SummaryRow icon={<Zap className="h-4 w-4 text-yellow-500" />} label="Potasio" value={`${Math.round(totals.potassium)} mg`} />
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

function MetricCard({ icon, label, value, accent }: { icon: React.ReactNode; label: string; value: string; accent: string }) {
  return (
    <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-3 shadow-sm">
      <div className={`flex h-9 w-9 items-center justify-center rounded-xl bg-[var(--app-bg)] ${accent}`}>{icon}</div>
      <p className="mt-3 text-[10px] uppercase tracking-[0.18em] text-[var(--text-muted)]">{label}</p>
      <p className="mt-1 text-lg font-semibold text-[var(--text-primary)]">{value}</p>
    </div>
  );
}

function SummaryRow({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <div className="flex items-center gap-3 rounded-xl border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2">
      <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-[var(--card-bg)]">{icon}</div>
      <div>
        <div className="text-[10px] uppercase tracking-[0.14em] text-[var(--text-muted)]">{label}</div>
        <div className="text-sm font-semibold text-[var(--text-primary)]">{value}</div>
      </div>
    </div>
  );
}

function DailyAveragePanel({ title, values }: { title: string; values: Array<{ label: string; value: number; unit: string }> }) {
  return (
    <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--app-bg)] p-3">
      <h3 className="text-[11px] font-semibold uppercase tracking-[0.14em] text-[var(--text-muted)]">{title}</h3>
      <div className="mt-3 space-y-2">
        {values.map((item) => (
          <div key={item.label} className="flex items-center justify-between gap-3 rounded-lg border border-[var(--card-border)] bg-[var(--card-bg)] px-2.5 py-1.5">
            <span className="text-[11px] text-[var(--text-secondary)]">{item.label}</span>
            <span className="text-[11px] font-semibold text-[var(--text-primary)]">
              {item.unit === 'kcal' ? `${Math.round(item.value)}${item.unit}` : `${Math.round(item.value)} ${item.unit}`}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}
