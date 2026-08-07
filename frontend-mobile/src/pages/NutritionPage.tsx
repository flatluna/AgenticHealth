import { useEffect, useMemo, useRef, useState } from 'react';
import {
  ChevronLeft,
  ChevronRight,
  Flame,
  Beef,
  Wheat,
  Droplet,
  Droplets,
  Candy,
  Leaf,
  Waves,
  Zap,
  Bone,
  CircleDot,
  Sparkles,
  Eye,
  X,
  Trash2,
  Camera,
  Loader2,
  type LucideIcon,
} from 'lucide-react';
import { getMeals, deleteMeal, type Meal, type NutritionTotals } from '../api/mealsApi';
import { getLatestGoalPlan, type GoalPlan } from '../api/goalsApi';
import { getExerciseHistory } from '../api/exerciseApi';
import { extractFoodLabel, type FoodLabelExtractionResult } from '../api/foodLabelApi';
import { FoodLabelReviewSheet } from '../components/FoodLabelReviewSheet';
import { PhotoSourceSheet } from '../components/PhotoSourceSheet';
import { CameraCaptureModal } from '../components/CameraCaptureModal';

/** Extra nutrients shown in the meal detail sheet beyond the main 4 (calorías/proteína/
 * carbohidratos/grasa), rendered only when the meal actually has a value for them. */
const EXTRA_MEAL_NUTRIENTS: Array<{ key: keyof Meal; label: string; unit: string; icon: LucideIcon }> = [
  { key: 'sugarGrams', label: 'Azúcar', unit: 'g', icon: Candy },
  { key: 'saturatedFatGrams', label: 'Grasa saturada', unit: 'g', icon: Droplets },
  { key: 'fiberGrams', label: 'Fibra', unit: 'g', icon: Leaf },
  { key: 'sodiumMilligrams', label: 'Sodio', unit: 'mg', icon: Waves },
  { key: 'potassiumMilligrams', label: 'Potasio', unit: 'mg', icon: Zap },
  { key: 'calciumMilligrams', label: 'Calcio', unit: 'mg', icon: Bone },
  { key: 'ironMilligrams', label: 'Hierro', unit: 'mg', icon: CircleDot },
  { key: 'magnesiumMilligrams', label: 'Magnesio', unit: 'mg', icon: Sparkles },
  { key: 'vitaminAMicrograms', label: 'Vitamina A', unit: 'µg', icon: Eye },
];

/** Reference daily values from the FDA Nutrition Facts label (21 CFR 101.9, based on a
 * 2,000-calorie diet) - used as the "recomendado por el gobierno" comparison whenever the
 * user's own goal plan doesn't define a personalized target for that nutrient. */
const FDA_DAILY_VALUES: Record<string, number> = {
  calories: 2000,
  proteinGrams: 50,
  carbsGrams: 275,
  fatGrams: 78,
  saturatedFatGrams: 20,
  sugarGrams: 50,
  fiberGrams: 28,
  sodiumMilligrams: 2300,
  potassiumMilligrams: 4700,
  calciumMilligrams: 1300,
  ironMilligrams: 18,
  magnesiumMilligrams: 420,
  vitaminAMicrograms: 900,
};

/** Nutrients where going over the daily value is undesirable, rather than a floor to reach. */
const LIMIT_NUTRIENTS = new Set(['sugarGrams', 'saturatedFatGrams', 'sodiumMilligrams']);

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

function addDays(date: Date, days: number): Date {
  const d = new Date(date);
  d.setDate(d.getDate() + days);
  return d;
}

function isSameDay(a: Date, b: Date): boolean {
  return a.toDateString() === b.toDateString();
}

const CENTRAL_TIME_ZONE = 'America/Chicago';
const dateLabelFormatter = new Intl.DateTimeFormat('es', { day: 'numeric', month: 'long', timeZone: CENTRAL_TIME_ZONE });
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

export function NutritionPage() {
  const [anchorDate, setAnchorDate] = useState(() => startOfDay(new Date()));
  const [meals, setMeals] = useState<Meal[]>([]);
  const [totals, setTotals] = useState<NutritionTotals>(EMPTY_TOTALS);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selectedMeal, setSelectedMeal] = useState<Meal | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [goalPlan, setGoalPlan] = useState<GoalPlan | null>(null);
  const [caloriesBurnedToday, setCaloriesBurnedToday] = useState(0);
  const [selectedNutrient, setSelectedNutrient] = useState<{ key: keyof NutritionTotals; label: string; unit: string } | null>(
    null,
  );
  const [isScanning, setIsScanning] = useState(false);
  const [scanError, setScanError] = useState<string | null>(null);
  const [pendingExtraction, setPendingExtraction] = useState<FoodLabelExtractionResult | null>(null);
  const [showPhotoSource, setShowPhotoSource] = useState(false);
  const [showCamera, setShowCamera] = useState(false);
  const cameraInputRef = useRef<HTMLInputElement | null>(null);
  const galleryInputRef = useRef<HTMLInputElement | null>(null);

  const handleFileSelected = async (file: File | undefined) => {
    if (!file) return;
    setIsScanning(true);
    setScanError(null);
    try {
      const result = await extractFoodLabel(file);
      if (!result.isValidLabel) {
        setScanError(result.reason || 'La imagen no muestra una etiqueta de información nutricional legible.');
        return;
      }
      setPendingExtraction(result);
    } catch {
      setScanError('No se pudo analizar la imagen. Intenta de nuevo con una foto más clara de la etiqueta.');
    } finally {
      setIsScanning(false);
    }
  };

  useEffect(() => {
    let cancelled = false;
    getLatestGoalPlan()
      .then((res) => {
        if (!cancelled) setGoalPlan(res.plan);
      })
      .catch(() => {
        /* no plan yet, or backend unavailable - page just won't show targets */
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    setIsLoading(true);
    setError(null);

    getMeals(anchorDate, anchorDate)
      .then((data) => {
        if (cancelled) return;
        setMeals(data.meals);
        setTotals(data.totals);
      })
      .catch(() => {
        if (cancelled) return;
        setError('No se pudieron cargar las comidas.');
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [anchorDate, refreshKey]);

  useEffect(() => {
    let cancelled = false;
    // Exercise has no day-range query like meals, so fetch a window covering
    // anchorDate and filter client-side to the exact day.
    const daysBack = Math.max(1, Math.ceil((startOfDay(new Date()).getTime() - anchorDate.getTime()) / 86400000) + 2);

    getExerciseHistory(daysBack)
      .then((data) => {
        if (cancelled) return;
        const burned = data.entries
          .filter((entry) => isSameDay(new Date(entry.recordedAtUtc), anchorDate))
          .reduce((sum, entry) => sum + (entry.caloriesBurned ?? 0), 0);
        setCaloriesBurnedToday(burned);
      })
      .catch(() => {
        if (!cancelled) setCaloriesBurnedToday(0);
      });

    return () => {
      cancelled = true;
    };
  }, [anchorDate, refreshKey]);

  const dayLabel = useMemo(() => {
    const label = dateLabelFormatter.format(anchorDate);
    return isSameDay(anchorDate, new Date()) ? `Hoy · ${label}` : label;
  }, [anchorDate]);

  return (
    <div className="flex h-full flex-col overflow-hidden bg-[var(--app-bg)]">
      <div className="flex items-center justify-between border-b border-[var(--card-border)] bg-[var(--card-bg)] px-3 sm:px-4 py-2 sm:py-3">
        <div>
          <h1 className="text-sm sm:text-base font-semibold text-[var(--text-primary)]">Nutrición</h1>
          <p className="text-xs capitalize text-[var(--text-muted)]">{dayLabel}</p>
        </div>
        <div className="flex items-center gap-1.5">
          <input
            ref={cameraInputRef}
            type="file"
            accept="image/*"
            capture="environment"
            className="hidden"
            onChange={(e) => {
              void handleFileSelected(e.target.files?.[0]);
              e.target.value = '';
            }}
          />
          <input
            ref={galleryInputRef}
            type="file"
            accept="image/*"
            className="hidden"
            onChange={(e) => {
              void handleFileSelected(e.target.files?.[0]);
              e.target.value = '';
            }}
          />
          <button
            type="button"
            onClick={() => setShowPhotoSource(true)}
            disabled={isScanning}
            className="rounded-lg border border-[var(--card-border)] p-2 text-[var(--text-secondary)] hover:bg-[var(--hover-bg)] disabled:opacity-50"
            aria-label="Escanear etiqueta de alimento"
          >
            {isScanning ? <Loader2 className="h-4 w-4 animate-spin" /> : <Camera className="h-4 w-4" />}
          </button>
          <button
            type="button"
            onClick={() => setAnchorDate((d) => addDays(d, -1))}
            className="rounded-lg border border-[var(--card-border)] p-2 text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]"
            aria-label="Día anterior"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <button
            type="button"
            onClick={() => setAnchorDate(startOfDay(new Date()))}
            className="rounded-lg border border-[var(--card-border)] px-2.5 py-2 text-xs font-medium text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]"
          >
            Hoy
          </button>
          <button
            type="button"
            onClick={() => setAnchorDate((d) => addDays(d, 1))}
            className="rounded-lg border border-[var(--card-border)] p-2 text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]"
            aria-label="Día siguiente"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-4 py-4">
        {error && <div className="mb-4 rounded-lg bg-red-50 px-4 py-2 text-sm text-red-600">{error}</div>}
        {scanError && (
          <div className="mb-4 flex items-start justify-between gap-2 rounded-lg bg-amber-50 px-4 py-2 text-sm text-amber-700">
            <span>{scanError}</span>
            <button type="button" onClick={() => setScanError(null)} aria-label="Cerrar aviso" className="shrink-0">
              <X className="h-4 w-4" />
            </button>
          </div>
        )}

        {goalPlan && <PlanVsRealCard totals={totals} plan={goalPlan} caloriesBurnedToday={caloriesBurnedToday} />}

        <StatsGrid
          totals={totals}
          plan={goalPlan}
          caloriesBurnedToday={caloriesBurnedToday}
          onSelectNutrient={(key, label, unit) => setSelectedNutrient({ key, label, unit })}
        />
        <p className="mt-1.5 text-[11px] text-[var(--text-muted)]">
          {goalPlan
            ? 'Calorías, proteína, carbohidratos y grasa comparadas con tu plan de metas. El resto, con los valores diarios recomendados por la FDA (dieta de 2,000 kcal).'
            : 'Comparado con los valores diarios recomendados por la FDA (dieta de 2,000 kcal).'}
        </p>

        <h2 className="mt-5 mb-2 text-sm font-semibold text-[var(--text-secondary)]">Comidas del día</h2>

        {isLoading ? (
          <div className="text-sm text-[var(--text-muted)]">Cargando…</div>
        ) : meals.length === 0 ? (
          <div className="rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 text-sm text-[var(--text-muted)]">
            Sin comidas registradas este día.
          </div>
        ) : (
          <div className="space-y-2">
            {meals.map((meal) => (
              <MealRow key={meal.id} meal={meal} onSelect={setSelectedMeal} />
            ))}
          </div>
        )}
      </div>

      {selectedMeal && (
        <MealDetailSheet
          meal={selectedMeal}
          plan={goalPlan}
          onClose={() => setSelectedMeal(null)}
          onDeleted={() => {
            setSelectedMeal(null);
            setRefreshKey((k) => k + 1);
          }}
        />
      )}

      {selectedNutrient && (
        <NutrientBreakdownSheet
          meals={meals}
          nutrientKey={selectedNutrient.key}
          label={selectedNutrient.label}
          unit={selectedNutrient.unit}
          total={totals[selectedNutrient.key]}
          onClose={() => setSelectedNutrient(null)}
        />
      )}

      {showPhotoSource && (
        <PhotoSourceSheet
          onClose={() => setShowPhotoSource(false)}
          onTakePhoto={() => {
            setShowPhotoSource(false);
            setShowCamera(true);
          }}
          onChooseGallery={() => {
            setShowPhotoSource(false);
            galleryInputRef.current?.click();
          }}
        />
      )}

      {showCamera && (
        <CameraCaptureModal
          onClose={() => setShowCamera(false)}
          onCapture={(file) => {
            setShowCamera(false);
            void handleFileSelected(file);
          }}
          onUnavailable={() => {
            setShowCamera(false);
            cameraInputRef.current?.click();
          }}
        />
      )}

      {pendingExtraction && (
        <FoodLabelReviewSheet
          extraction={pendingExtraction}
          onClose={() => setPendingExtraction(null)}
          onSaved={() => {
            setPendingExtraction(null);
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
  target,
  isLimit,
  onClick,
}: {
  icon: LucideIcon;
  label: string;
  value: number;
  unit: string;
  target?: number | null;
  isLimit?: boolean;
  onClick?: () => void;
}) {
  const percent = target != null && target > 0 ? Math.round((value / target) * 100) : null;
  const overLimit = isLimit === true && percent != null && percent > 100;
  const Container = onClick ? 'button' : 'div';
  return (
    <Container
      type={onClick ? 'button' : undefined}
      onClick={onClick}
      className={`flex w-full flex-col gap-1.5 rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] p-3 text-left shadow-sm ${
        onClick ? 'transition-colors hover:bg-[var(--accent-soft)]' : ''
      }`}
    >
      <div className="flex items-center gap-2">
        <div className="rounded-full bg-[var(--accent-soft)] p-1.5 text-[var(--accent-text)]">
          <Icon className="h-4 w-4" />
        </div>
        <div>
          <p className="text-[11px] text-[var(--text-muted)]">{label}</p>
          <p className="text-sm font-semibold text-[var(--text-primary)]">
            {Math.round(value)}
            <span className="ml-1 text-[11px] font-normal text-[var(--text-muted)]">
              {unit}
              {target != null ? ` / ${Math.round(target)} ${unit}` : ''}
            </span>
          </p>
        </div>
      </div>
      {percent != null && (
        <div className="h-1.5 w-full overflow-hidden rounded-full bg-[var(--app-bg)]">
          <div
            className={`h-full rounded-full ${overLimit ? 'bg-red-500' : 'bg-[var(--accent)]'}`}
            style={{ width: `${Math.min(percent, 100)}%` }}
          />
        </div>
      )}
    </Container>
  );
}

function StatsGrid({
  totals,
  plan,
  caloriesBurnedToday,
  onSelectNutrient,
}: {
  totals: NutritionTotals;
  plan: GoalPlan | null;
  caloriesBurnedToday: number;
  onSelectNutrient: (key: keyof NutritionTotals, label: string, unit: string) => void;
}) {
  // Exercise calories are ADDED to the allowed budget (not subtracted from consumed) -
  // matches the MyFitnessPal/Fitbit convention: Remaining = Goal + Exercise - Food.
  const calorieTarget = (plan?.dailyCalorieTarget ?? FDA_DAILY_VALUES.calories) + caloriesBurnedToday;
  const proteinTarget = plan?.macros.proteinGrams ?? FDA_DAILY_VALUES.proteinGrams;
  const carbsTarget = plan?.macros.carbsGrams ?? FDA_DAILY_VALUES.carbsGrams;
  const fatTarget = plan?.macros.fatGrams ?? FDA_DAILY_VALUES.fatGrams;

  return (
    <div className="grid grid-cols-2 gap-2">
      <StatCard
        icon={Flame}
        label="Calorías"
        value={totals.calories}
        unit="kcal"
        target={calorieTarget}
        onClick={() => onSelectNutrient('calories', 'Calorías', 'kcal')}
      />
      <StatCard
        icon={Beef}
        label="Proteína"
        value={totals.proteinGrams}
        unit="g"
        target={proteinTarget}
        onClick={() => onSelectNutrient('proteinGrams', 'Proteína', 'g')}
      />
      <StatCard
        icon={Wheat}
        label="Carbohidratos"
        value={totals.carbsGrams}
        unit="g"
        target={carbsTarget}
        onClick={() => onSelectNutrient('carbsGrams', 'Carbohidratos', 'g')}
      />
      <StatCard
        icon={Droplet}
        label="Grasa"
        value={totals.fatGrams}
        unit="g"
        target={fatTarget}
        onClick={() => onSelectNutrient('fatGrams', 'Grasa', 'g')}
      />
      <StatCard
        icon={Candy}
        label="Azúcar"
        value={totals.sugarGrams}
        unit="g"
        target={FDA_DAILY_VALUES.sugarGrams}
        isLimit
        onClick={() => onSelectNutrient('sugarGrams', 'Azúcar', 'g')}
      />
      <StatCard
        icon={Leaf}
        label="Fibra"
        value={totals.fiberGrams}
        unit="g"
        target={FDA_DAILY_VALUES.fiberGrams}
        onClick={() => onSelectNutrient('fiberGrams', 'Fibra', 'g')}
      />
      <StatCard
        icon={Waves}
        label="Sodio"
        value={totals.sodiumMilligrams}
        unit="mg"
        target={FDA_DAILY_VALUES.sodiumMilligrams}
        isLimit
        onClick={() => onSelectNutrient('sodiumMilligrams', 'Sodio', 'mg')}
      />
      <StatCard
        icon={Zap}
        label="Potasio"
        value={totals.potassiumMilligrams}
        unit="mg"
        target={FDA_DAILY_VALUES.potassiumMilligrams}
        onClick={() => onSelectNutrient('potassiumMilligrams', 'Potasio', 'mg')}
      />
    </div>
  );
}

function PlanVsRealCard({
  totals,
  plan,
  caloriesBurnedToday,
}: {
  totals: NutritionTotals;
  plan: GoalPlan;
  caloriesBurnedToday: number;
}) {
  // Exercise calories are ADDED to the allowed budget (not subtracted from consumed) -
  // matches the MyFitnessPal/Fitbit convention: Remaining = Goal + Exercise - Food.
  const baseTarget = plan.dailyCalorieTarget;
  const adjustedTarget = baseTarget != null ? baseTarget + caloriesBurnedToday : null;
  const percent = adjustedTarget && adjustedTarget > 0 ? Math.round((totals.calories / adjustedTarget) * 100) : null;

  return (
    <div className="mb-3 rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] p-3 shadow-sm">
      <p className="text-xs font-medium uppercase tracking-wide text-[var(--text-muted)]">Plan vs. realidad</p>
      <div className="mt-1 flex items-baseline justify-between">
        <p className="text-sm text-[var(--text-primary)]">
          {Math.round(totals.calories)}
          <span className="text-[var(--text-muted)]"> / {adjustedTarget != null ? Math.round(adjustedTarget) : '—'} kcal</span>
        </p>
        {percent != null && <p className="text-xs font-semibold text-[var(--accent-text)]">{percent}%</p>}
      </div>
      {caloriesBurnedToday > 0 && baseTarget != null && (
        <p className="mt-1 text-[11px] text-[var(--text-muted)]">
          {Math.round(baseTarget)} base + {Math.round(caloriesBurnedToday)} por ejercicio
        </p>
      )}
    </div>
  );
}

function NutrientBreakdownSheet({
  meals,
  nutrientKey,
  label,
  unit,
  total,
  onClose,
}: {
  meals: Meal[];
  nutrientKey: keyof NutritionTotals;
  label: string;
  unit: string;
  total: number;
  onClose: () => void;
}) {
  const contributions = useMemo(
    () =>
      meals
        .map((meal) => ({ meal, value: meal[nutrientKey] ?? 0 }))
        .filter((c) => c.value > 0)
        .sort((a, b) => b.value - a.value),
    [meals, nutrientKey],
  );

  return (
    <div className="fixed inset-0 z-50 flex items-end bg-black/40" onClick={onClose}>
      <div
        className="max-h-[80vh] w-full overflow-y-auto rounded-t-2xl bg-[var(--card-bg)] p-4 pb-[max(1rem,env(safe-area-inset-bottom))] shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-3 flex items-start justify-between">
          <div>
            <h3 className="text-sm font-semibold text-[var(--text-primary)]">{label}</h3>
            <p className="text-xs text-[var(--text-muted)]">
              Total del día: {Math.round(total)} {unit}
            </p>
          </div>
          <button type="button" onClick={onClose} aria-label="Cerrar" className="rounded-full p-1.5 text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]">
            <X className="h-4 w-4" />
          </button>
        </div>

        {contributions.length === 0 ? (
          <p className="text-sm text-[var(--text-muted)]">Ninguna comida registrada este día aportó {label.toLowerCase()}.</p>
        ) : (
          <div className="space-y-2">
            {contributions.map(({ meal, value }) => {
              const percent = total > 0 ? Math.round((value / total) * 100) : 0;
              return (
                <div key={meal.id} className="rounded-xl border border-[var(--card-border)] bg-[var(--app-bg)] p-3">
                  <div className="flex items-center justify-between gap-3">
                    <div className="min-w-0">
                      <p className="truncate text-sm font-medium text-[var(--text-primary)]">
                        {MEAL_TYPE_LABEL[meal.mealType]} · {meal.description}
                      </p>
                      <p className="text-xs text-[var(--text-muted)]">{timeFormatter.format(new Date(meal.recordedAtUtc))}</p>
                    </div>
                    <div className="shrink-0 text-right">
                      <p className="text-sm font-semibold text-[var(--text-primary)]">
                        {Math.round(value)} {unit}
                      </p>
                      <p className="text-xs text-[var(--accent-text)]">{percent}%</p>
                    </div>
                  </div>
                  <div className="mt-2 h-1.5 w-full overflow-hidden rounded-full bg-[var(--card-border)]">
                    <div className="h-full rounded-full bg-[var(--accent)]" style={{ width: `${Math.min(percent, 100)}%` }} />
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}

function MealRow({ meal, onSelect }: { meal: Meal; onSelect: (meal: Meal) => void }) {
  return (
    <button
      type="button"
      onClick={() => onSelect(meal)}
      className="flex w-full items-center justify-between rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] px-3 py-2.5 text-left shadow-sm transition-colors hover:bg-[var(--accent-soft)]"
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
      <p className="text-sm font-semibold text-[var(--accent-text)]">
        {meal.calories != null ? `${Math.round(meal.calories)} kcal` : '—'}
      </p>
    </button>
  );
}

function MealDetailSheet({
  meal,
  plan,
  onClose,
  onDeleted,
}: {
  meal: Meal;
  plan: GoalPlan | null;
  onClose: () => void;
  onDeleted: () => void;
}) {
  const [isDeleting, setIsDeleting] = useState(false);

  const handleDelete = async () => {
    setIsDeleting(true);
    try {
      await deleteMeal(meal.id);
      onDeleted();
    } finally {
      setIsDeleting(false);
    }
  };

  const calorieTarget = plan?.dailyCalorieTarget ?? FDA_DAILY_VALUES.calories;
  const proteinTarget = plan?.macros.proteinGrams ?? FDA_DAILY_VALUES.proteinGrams;
  const carbsTarget = plan?.macros.carbsGrams ?? FDA_DAILY_VALUES.carbsGrams;
  const fatTarget = plan?.macros.fatGrams ?? FDA_DAILY_VALUES.fatGrams;

  return (
    <div className="fixed inset-0 z-50 flex items-end bg-black/40" onClick={onClose}>
      <div
        className="w-full rounded-t-2xl bg-[var(--card-bg)] p-4 pb-[max(1rem,env(safe-area-inset-bottom))] shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-3 flex items-start justify-between">
          <div>
            <h3 className="text-sm font-semibold text-[var(--text-primary)]">
              {MEAL_TYPE_LABEL[meal.mealType]} · {meal.description}
            </h3>
            <p className="text-xs text-[var(--text-muted)]">
              {timeFormatter.format(new Date(meal.recordedAtUtc))}
              {meal.servingSize ? ` · ${meal.servingSize}` : ''}
            </p>
          </div>
          <button type="button" onClick={onClose} aria-label="Cerrar" className="rounded-full p-1.5 text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="grid grid-cols-2 gap-2">
          <StatCard icon={Flame} label="Calorías" value={meal.calories ?? 0} unit="kcal" target={calorieTarget} />
          <StatCard icon={Beef} label="Proteína" value={meal.proteinGrams ?? 0} unit="g" target={proteinTarget} />
          <StatCard icon={Wheat} label="Carbohidratos" value={meal.carbsGrams ?? 0} unit="g" target={carbsTarget} />
          <StatCard icon={Droplet} label="Grasa" value={meal.fatGrams ?? 0} unit="g" target={fatTarget} />
          {EXTRA_MEAL_NUTRIENTS.filter((n) => meal[n.key] != null).map((n) => (
            <StatCard
              key={n.key}
              icon={n.icon}
              label={n.label}
              value={meal[n.key] as number}
              unit={n.unit}
              target={FDA_DAILY_VALUES[n.key]}
              isLimit={LIMIT_NUTRIENTS.has(n.key)}
            />
          ))}
        </div>

        <p className="mt-2 text-[11px] text-[var(--text-muted)]">
          Porcentajes respecto a tu meta diaria (o al valor diario recomendado por la FDA si no hay meta).
        </p>

        {meal.sourceBreakdown && (
          <p className="mt-3 whitespace-pre-wrap rounded-lg bg-[var(--app-bg)] p-2.5 text-xs text-[var(--text-muted)]">{meal.sourceBreakdown}</p>
        )}

        <button
          type="button"
          onClick={() => void handleDelete()}
          disabled={isDeleting}
          className="mt-4 flex w-full items-center justify-center gap-2 rounded-xl border border-red-200 py-2.5 text-sm font-medium text-red-600 hover:bg-red-50 disabled:opacity-50"
        >
          <Trash2 className="h-4 w-4" />
          {isDeleting ? 'Eliminando…' : 'Eliminar comida'}
        </button>
      </div>
    </div>
  );
}
