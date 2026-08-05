import { useEffect, useMemo, useState } from 'react';
import { ChevronLeft, ChevronRight, Flame, Beef, Wheat, Droplet, X, Trash2 } from 'lucide-react';
import { getMeals, deleteMeal, type Meal, type NutritionTotals } from '../api/mealsApi';
import { getLatestGoalPlan, type GoalPlan } from '../api/goalsApi';

type ViewMode = 'day' | 'week' | 'month';

const MEAL_TYPE_LABEL: Record<Meal['mealType'], string> = {
  Breakfast: 'Desayuno',
  Lunch: 'Almuerzo',
  Dinner: 'Cena',
  Snack: 'Snack',
};

function startOfDay(date: Date): Date {
  const d = new Date(date);
  d.setHours(0, 0, 0, 0);
  return d;
}

function startOfWeek(date: Date): Date {
  const d = startOfDay(date);
  const day = (d.getDay() + 6) % 7; // Monday = 0
  d.setDate(d.getDate() - day);
  return d;
}

function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

function endOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth() + 1, 0);
}

function addDays(date: Date, days: number): Date {
  const d = new Date(date);
  d.setDate(d.getDate() + days);
  return d;
}

function isSameDay(a: Date, b: Date): boolean {
  return a.toDateString() === b.toDateString();
}

function formatDayKey(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

const CENTRAL_TIME_ZONE = 'America/Chicago';
const dateLabelFormatter = new Intl.DateTimeFormat('es', { day: 'numeric', month: 'long', year: 'numeric', timeZone: CENTRAL_TIME_ZONE });
const weekdayFormatter = new Intl.DateTimeFormat('es', { weekday: 'short', timeZone: CENTRAL_TIME_ZONE });
const monthYearFormatter = new Intl.DateTimeFormat('es', { month: 'long', year: 'numeric', timeZone: CENTRAL_TIME_ZONE });
const timeFormatter = new Intl.DateTimeFormat('es', { hour: '2-digit', minute: '2-digit', timeZone: CENTRAL_TIME_ZONE });

const EMPTY_TOTALS: NutritionTotals = {
  calories: 0,
  proteinGrams: 0,
  carbsGrams: 0,
  fatGrams: 0,
  sugarGrams: 0,
  fiberGrams: 0,
  sodiumMilligrams: 0,
  potassiumMilligrams: 0,
};

export function FoodPage() {
  const [viewMode, setViewMode] = useState<ViewMode>('day');
  const [anchorDate, setAnchorDate] = useState(() => startOfDay(new Date()));
  const [meals, setMeals] = useState<Meal[]>([]);
  const [totals, setTotals] = useState<NutritionTotals>(EMPTY_TOTALS);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selectedMeal, setSelectedMeal] = useState<Meal | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [goalPlan, setGoalPlan] = useState<GoalPlan | null>(null);

  useEffect(() => {
    let cancelled = false;
    getLatestGoalPlan()
      .then((res) => {
        if (!cancelled) setGoalPlan(res.plan);
      })
      .catch(() => {
        /* no plan yet, or backend unavailable — dashboard just won't show targets */
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const range = useMemo(() => {
    if (viewMode === 'day') {
      return { from: anchorDate, to: anchorDate };
    }
    if (viewMode === 'week') {
      const from = startOfWeek(anchorDate);
      return { from, to: addDays(from, 6) };
    }
    const from = startOfMonth(anchorDate);
    return { from, to: endOfMonth(anchorDate) };
  }, [viewMode, anchorDate]);

  useEffect(() => {
    let cancelled = false;
    setIsLoading(true);
    setError(null);

    getMeals(range.from, range.to)
      .then((data) => {
        if (cancelled) return;
        setMeals(data.meals);
        setTotals(data.totals);
      })
      .catch(() => {
        if (cancelled) return;
        setError('No se pudieron cargar las comidas. Verifica que el backend esté corriendo.');
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [range.from, range.to, refreshKey]);

  const mealsByDay = useMemo(() => {
    const map = new Map<string, Meal[]>();
    for (const meal of meals) {
      const key = formatDayKey(new Date(meal.recordedAtUtc));
      const list = map.get(key) ?? [];
      list.push(meal);
      map.set(key, list);
    }
    return map;
  }, [meals]);

  const goPrev = () => {
    if (viewMode === 'day') setAnchorDate((d) => addDays(d, -1));
    else if (viewMode === 'week') setAnchorDate((d) => addDays(d, -7));
    else setAnchorDate((d) => new Date(d.getFullYear(), d.getMonth() - 1, 1));
  };

  const goNext = () => {
    if (viewMode === 'day') setAnchorDate((d) => addDays(d, 1));
    else if (viewMode === 'week') setAnchorDate((d) => addDays(d, 7));
    else setAnchorDate((d) => new Date(d.getFullYear(), d.getMonth() + 1, 1));
  };

  const goToday = () => setAnchorDate(startOfDay(new Date()));

  const headerLabel = useMemo(() => {
    if (viewMode === 'day') return dateLabelFormatter.format(anchorDate);
    if (viewMode === 'week') {
      return `${range.from.getDate()} - ${dateLabelFormatter.format(range.to)}`;
    }
    return monthYearFormatter.format(anchorDate);
  }, [viewMode, anchorDate, range]);

  const numDays = useMemo(
    () => Math.round((startOfDay(range.to).getTime() - startOfDay(range.from).getTime()) / 86_400_000) + 1,
    [range],
  );

  return (
    <div className="flex h-full flex-col overflow-hidden">
      <div className="flex flex-col gap-3 border-b border-[var(--card-border)] bg-[var(--card-bg)] px-6 py-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h2 className="text-xl font-semibold text-[var(--text-primary)]">Nutrición</h2>
          <p className="text-sm capitalize text-[var(--text-muted)]">{headerLabel}</p>
        </div>

        <div className="flex items-center gap-2">
          <div className="flex rounded-lg border border-[var(--card-border)] bg-[var(--app-bg)] p-0.5">
            {(['day', 'week', 'month'] as ViewMode[]).map((mode) => (
              <button
                key={mode}
                type="button"
                onClick={() => setViewMode(mode)}
                className={`rounded-md px-3 py-1.5 text-sm font-medium transition-colors ${
                  viewMode === mode ? 'bg-[var(--accent)] text-white' : 'text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]'
                }`}
              >
                {mode === 'day' ? 'Día' : mode === 'week' ? 'Semana' : 'Mes'}
              </button>
            ))}
          </div>
          <button
            type="button"
            onClick={goPrev}
            className="rounded-lg border border-[var(--card-border)] p-2 text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]"
            aria-label="Anterior"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <button
            type="button"
            onClick={goToday}
            className="rounded-lg border border-[var(--card-border)] px-3 py-2 text-sm font-medium text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]"
          >
            Hoy
          </button>
          <button
            type="button"
            onClick={goNext}
            className="rounded-lg border border-[var(--card-border)] p-2 text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]"
            aria-label="Siguiente"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto p-6">
        {error && <div className="mb-4 rounded-lg bg-red-50 px-4 py-2 text-sm text-red-600">{error}</div>}

        <PlanVsRealDashboard totals={totals} plan={goalPlan} numDays={numDays} />

        <StatsGrid totals={totals} />

        {isLoading ? (
          <div className="mt-6 text-sm text-[var(--text-muted)]">Cargando…</div>
        ) : viewMode === 'month' ? (
          <MonthCalendar
            anchorDate={anchorDate}
            mealsByDay={mealsByDay}
            onSelectDay={(day) => {
              setAnchorDate(day);
              setViewMode('day');
            }}
          />
        ) : (
          <DayGroupedList
            fromDate={range.from}
            toDate={range.to}
            mealsByDay={mealsByDay}
            onSelectMeal={setSelectedMeal}
          />
        )}
      </div>

      {selectedMeal && (
        <MealDetailModal
          meal={selectedMeal}
          onClose={() => setSelectedMeal(null)}
          onDeleted={() => {
            setSelectedMeal(null);
            setRefreshKey((k) => k + 1);
          }}
        />
      )}
    </div>
  );
}

function StatCard({
  icon: Icon,
  label,
  value,
  unit,
}: {
  icon: typeof Flame;
  label: string;
  value: number;
  unit: string;
}) {  return (
    <div className="flex items-center gap-3 rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
      <div className="rounded-full bg-[var(--accent-soft)] p-2 text-[var(--accent-text)]">
        <Icon className="h-5 w-5" />
      </div>
      <div>
        <p className="text-xs text-[var(--text-muted)]">{label}</p>
        <p className="text-lg font-semibold text-[var(--text-primary)]">
          {Math.round(value)}
          <span className="ml-1 text-xs font-normal text-[var(--text-muted)]">{unit}</span>
        </p>
      </div>
    </div>
  );
}

function PlanVsRealDashboard({ totals, plan, numDays }: { totals: NutritionTotals; plan: GoalPlan | null; numDays: number }) {
  const targetCalories = plan?.dailyCalorieTarget ?? null;
  const targetProtein = plan?.macros.proteinGrams ?? null;
  const targetCarbs = plan?.macros.carbsGrams ?? null;
  const targetFat = plan?.macros.fatGrams ?? null;

  return (
    <div className="mb-6 rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-sm font-semibold text-[var(--text-primary)]">Comparativa plan vs realidad</h3>
          <p className="text-sm text-[var(--text-muted)]">Vista rápida para revisar tu progreso en los últimos {numDays} días.</p>
        </div>
      </div>
      <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        <MetricCard label="Calorías" actual={totals.calories} target={targetCalories} unit="kcal" />
        <MetricCard label="Proteína" actual={totals.proteinGrams} target={targetProtein} unit="g" />
        <MetricCard label="Carbs" actual={totals.carbsGrams} target={targetCarbs} unit="g" />
        <MetricCard label="Grasa" actual={totals.fatGrams} target={targetFat} unit="g" />
      </div>
    </div>
  );
}

function MetricCard({ label, actual, target, unit }: { label: string; actual: number; target: number | null; unit: string }) {
  const percent = target != null && target > 0 ? Math.round((actual / target) * 100) : null;
  return (
    <div className="rounded-xl border border-[var(--card-border)] bg-[var(--app-bg)] p-3">
      <p className="text-xs font-medium uppercase tracking-wide text-[var(--text-muted)]">{label}</p>
      <p className="mt-1 text-lg font-semibold text-[var(--text-primary)]">{Math.round(actual)} {unit}</p>
      <p className="text-sm text-[var(--text-muted)]">Meta: {target != null ? `${Math.round(target)} ${unit}` : '—'}</p>
      {percent != null && <p className="mt-2 text-xs text-[var(--accent-text)]">{percent}% de la meta</p>}
    </div>
  );
}

function StatsGrid({ totals }: { totals: NutritionTotals }) {
  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
      <StatCard icon={Flame} label="Calorías" value={totals.calories} unit="kcal" />
      <StatCard icon={Beef} label="Proteína" value={totals.proteinGrams} unit="g" />
      <StatCard icon={Wheat} label="Carbohidratos" value={totals.carbsGrams} unit="g" />
      <StatCard icon={Droplet} label="Grasa" value={totals.fatGrams} unit="g" />
      <StatCard icon={Flame} label="Azúcares" value={totals.sugarGrams} unit="g" />
      <StatCard icon={Wheat} label="Fibra" value={totals.fiberGrams} unit="g" />
      <StatCard icon={Droplet} label="Sodio" value={totals.sodiumMilligrams} unit="mg" />
      <StatCard icon={Beef} label="Potasio" value={totals.potassiumMilligrams} unit="mg" />
    </div>
  );
}

function MealRow({ meal, onSelect }: { meal: Meal; onSelect: (meal: Meal) => void }) {
  return (
    <button
      type="button"
      onClick={() => onSelect(meal)}
      className="flex w-full items-center justify-between rounded-lg border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2 text-left transition-colors hover:bg-[var(--accent-soft)]"
    >
      <div>
        <p className="text-sm font-medium text-[var(--text-secondary)]">
          {MEAL_TYPE_LABEL[meal.mealType]} · {meal.description}
        </p>
        <p className="text-xs text-[var(--text-muted)]">
          {timeFormatter.format(new Date(meal.recordedAtUtc))}
          {meal.servingSize ? ` · ${meal.servingSize}` : ''}
        </p>
      </div>
      <p className="text-sm font-semibold text-[var(--accent-text)]">{meal.calories != null ? `${Math.round(meal.calories)} kcal` : '—'}</p>
    </button>
  );
}

function DayGroupedList({
  fromDate,
  toDate,
  mealsByDay,
  onSelectMeal,
}: {
  fromDate: Date;
  toDate: Date;
  mealsByDay: Map<string, Meal[]>;
  onSelectMeal: (meal: Meal) => void;
}) {
  const days: Date[] = [];
  for (let d = startOfDay(fromDate); d <= toDate; d = addDays(d, 1)) {
    days.push(d);
  }

  return (
    <div className="mt-6 space-y-4">
      {days.map((day) => {
        const key = formatDayKey(day);
        const dayMeals = mealsByDay.get(key) ?? [];
        return (
          <div key={key} className="rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
            <h3 className="mb-2 text-sm font-semibold capitalize text-[var(--text-secondary)]">
              {isSameDay(day, new Date()) ? 'Hoy · ' : ''}
              {dateLabelFormatter.format(day)}
            </h3>
            {dayMeals.length === 0 ? (
              <p className="text-sm text-[var(--text-muted)]">Sin comidas registradas.</p>
            ) : (
              <div className="space-y-2">
                {dayMeals.map((meal) => (
                  <MealRow key={meal.id} meal={meal} onSelect={onSelectMeal} />
                ))}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

function MonthCalendar({
  anchorDate,
  mealsByDay,
  onSelectDay,
}: {
  anchorDate: Date;
  mealsByDay: Map<string, Meal[]>;
  onSelectDay: (day: Date) => void;
}) {
  const monthStart = startOfMonth(anchorDate);
  const gridStart = startOfWeek(monthStart);
  const weeks: Date[][] = [];
  let cursor = gridStart;
  for (let w = 0; w < 6; w++) {
    const week: Date[] = [];
    for (let i = 0; i < 7; i++) {
      week.push(cursor);
      cursor = addDays(cursor, 1);
    }
    weeks.push(week);
  }

  const weekdayLabels = weeks[0].map((d) => weekdayFormatter.format(d));

  return (
    <div className="mt-6 overflow-hidden rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] shadow-sm">
      <div className="grid grid-cols-7 border-b border-[var(--card-border)] bg-[var(--app-bg)]">
        {weekdayLabels.map((label) => (
          <div key={label} className="px-2 py-2 text-center text-xs font-medium capitalize text-[var(--text-muted)]">
            {label}
          </div>
        ))}
      </div>
      {weeks.map((week, wi) => (
        <div key={wi} className="grid grid-cols-7 border-b border-[var(--card-border)] last:border-b-0">
          {week.map((day) => {
            const key = formatDayKey(day);
            const dayMeals = mealsByDay.get(key) ?? [];
            const dayCalories = dayMeals.reduce((sum, m) => sum + (m.calories ?? 0), 0);
            const inMonth = day.getMonth() === anchorDate.getMonth();
            return (
              <button
                key={key}
                type="button"
                onClick={() => onSelectDay(day)}
                className={`flex min-h-20 flex-col items-start gap-1 border-r border-[var(--card-border)] p-2 text-left last:border-r-0 hover:bg-[var(--accent-soft)] ${
                  inMonth ? '' : 'bg-[var(--app-bg)] text-[var(--text-muted)]'
                }`}
              >
                <span
                  className={`text-xs font-medium ${
                    isSameDay(day, new Date()) ? 'rounded-full bg-[var(--accent)] px-1.5 py-0.5 text-white' : 'text-[var(--text-secondary)]'
                  }`}
                >
                  {day.getDate()}
                </span>
                {dayCalories > 0 && (
                  <span className="rounded bg-[var(--accent-soft)] px-1.5 py-0.5 text-[11px] font-medium text-[var(--accent-text)]">
                    {Math.round(dayCalories)} kcal
                  </span>
                )}
              </button>
            );
          })}
        </div>
      ))}
    </div>
  );
}

function NutrientRow({ label, value, unit }: { label: string; value: number | null; unit: string }) {
  return (
    <tr>
      <th className="px-3 py-1.5 text-left font-medium text-[var(--text-secondary)]">{label}</th>
      <td className="px-3 py-1.5 text-right text-[var(--text-primary)]">{value != null ? `${value} ${unit}` : '—'}</td>
    </tr>
  );
}

function MealDetailModal({
  meal,
  onClose,
  onDeleted,
}: {
  meal: Meal;
  onClose: () => void;
  onDeleted: () => void;
}) {
  const [isDeleting, setIsDeleting] = useState(false);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const handleDelete = async () => {
    setIsDeleting(true);
    setDeleteError(null);
    try {
      await deleteMeal(meal.id);
      onDeleted();
    } catch {
      setDeleteError('No se pudo borrar la comida.');
      setIsDeleting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div
        className="max-h-[85vh] w-full max-w-md overflow-y-auto rounded-xl bg-[var(--card-bg)] p-5 shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-3 flex items-start justify-between gap-3">
          <div>
            <h3 className="text-lg font-semibold text-[var(--text-primary)]">{meal.description}</h3>
            <p className="text-sm text-[var(--text-muted)]">
              {MEAL_TYPE_LABEL[meal.mealType]}
              {meal.servingSize ? ` · ${meal.servingSize}` : ''} ·{' '}
              {timeFormatter.format(new Date(meal.recordedAtUtc))}
            </p>
          </div>
          <div className="flex items-center gap-1">
            <button
              type="button"
              onClick={() => setConfirmingDelete(true)}
              className="rounded p-1 text-[var(--text-muted)] hover:bg-red-500/10 hover:text-red-600"
              aria-label="Borrar"
            >
              <Trash2 className="h-5 w-5" />
            </button>
            <button
              type="button"
              onClick={onClose}
              className="rounded p-1 text-[var(--text-muted)] hover:bg-[var(--hover-bg)] hover:text-[var(--text-secondary)]"
              aria-label="Cerrar"
            >
              <X className="h-5 w-5" />
            </button>
          </div>
        </div>

        {confirmingDelete && (
          <div className="mb-3 rounded-lg border border-red-200 bg-red-50 p-3">
            <p className="text-sm text-red-700">¿Borrar este registro de comida? Esta acción no se puede deshacer.</p>
            {deleteError && <p className="mt-1 text-xs text-red-600">{deleteError}</p>}
            <div className="mt-2 flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setConfirmingDelete(false)}
                disabled={isDeleting}
                className="rounded-md px-3 py-1 text-xs font-medium text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]"
              >
                Cancelar
              </button>
              <button
                type="button"
                onClick={handleDelete}
                disabled={isDeleting}
                className="rounded-md bg-red-600 px-3 py-1 text-xs font-medium text-white hover:bg-red-700 disabled:opacity-60"
              >
                {isDeleting ? 'Borrando…' : 'Borrar'}
              </button>
            </div>
          </div>
        )}

        <h4 className="mb-1 mt-3 text-xs font-semibold uppercase tracking-wide text-[var(--text-muted)]">Macronutrientes</h4>
        <table className="w-full border-collapse overflow-hidden rounded-lg border border-[var(--card-border)] text-sm">
          <tbody>
            <NutrientRow label="Calorías" value={meal.calories} unit="kcal" />
            <NutrientRow label="Proteína" value={meal.proteinGrams} unit="g" />
            <NutrientRow label="Carbohidratos" value={meal.carbsGrams} unit="g" />
            <NutrientRow label="Azúcares" value={meal.sugarGrams} unit="g" />
            <NutrientRow label="Fibra" value={meal.fiberGrams} unit="g" />
            <NutrientRow label="Grasa total" value={meal.fatGrams} unit="g" />
            <NutrientRow label="Grasa saturada" value={meal.saturatedFatGrams} unit="g" />
          </tbody>
        </table>

        <h4 className="mb-1 mt-4 text-xs font-semibold uppercase tracking-wide text-[var(--text-muted)]">Minerales y vitaminas</h4>
        <table className="w-full border-collapse overflow-hidden rounded-lg border border-[var(--card-border)] text-sm">
          <tbody>
            <NutrientRow label="Sodio" value={meal.sodiumMilligrams} unit="mg" />
            <NutrientRow label="Potasio" value={meal.potassiumMilligrams} unit="mg" />
            <NutrientRow label="Calcio" value={meal.calciumMilligrams} unit="mg" />
            <NutrientRow label="Hierro" value={meal.ironMilligrams} unit="mg" />
            <NutrientRow label="Magnesio" value={meal.magnesiumMilligrams} unit="mg" />
            <NutrientRow label="Vitamina A" value={meal.vitaminAMicrograms} unit="µg" />
          </tbody>
        </table>

        {meal.sourceBreakdown && (
          <>
            <h4 className="mb-1 mt-4 text-xs font-semibold uppercase tracking-wide text-[var(--text-muted)]">Fuente / desglose</h4>
            <p className="rounded-lg border border-[var(--card-border)] p-2 text-sm text-[var(--text-secondary)]">
              {meal.sourceBreakdown}
            </p>
          </>
        )}
      </div>
    </div>
  );
}
