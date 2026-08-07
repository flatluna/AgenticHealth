import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { Loader2, AlertCircle, CheckCircle2, Circle, Footprints, Plus, Trash2, Sparkles, Check, X, RotateCcw } from 'lucide-react';
import { getLatestGoalPlan, saveGoalPlanCheckIn, getGoalPlanCheckInHistory, type GoalPlanCheckIn } from '../api/goalsApi';
import {
  getExerciseHistory,
  logExercise,
  deleteExerciseEntry,
  estimateExercise,
  getPersonalExerciseCatalog,
  saveCustomExercise,
  logPersonalExercise,
  deletePersonalExercise,
  type ExerciseEntry,
  type ExerciseEstimate,
  type PersonalExercise,
} from '../api/exerciseApi';

const CENTRAL_TIME_ZONE = 'America/Chicago';
const EXERCISE_TYPES = ['Pesas', 'Correr/Trotar', 'Nadar', 'Ciclismo', 'Caminar', 'Yoga/Estiramiento', 'Otro'] as const;

function todayIso(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: CENTRAL_TIME_ZONE }).format(new Date());
}

const dateTimeFormatter = new Intl.DateTimeFormat('es', {
  day: 'numeric',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
  timeZone: CENTRAL_TIME_ZONE,
});

function computeStreak(checkIns: GoalPlanCheckIn[]): number {
  const byDate = new Map(checkIns.map((c) => [c.checkInDate, c]));
  let streak = 0;
  const cursor = new Date();
  const todayCheckIn = byDate.get(todayIso());
  if (!todayCheckIn || !(todayCheckIn.followedNutrition && todayCheckIn.followedExercise)) {
    cursor.setDate(cursor.getDate() - 1);
  }
  for (let i = 0; i < 90; i++) {
    const iso = `${cursor.getFullYear()}-${String(cursor.getMonth() + 1).padStart(2, '0')}-${String(cursor.getDate()).padStart(2, '0')}`;
    const entry = byDate.get(iso);
    if (!entry || !(entry.followedNutrition && entry.followedExercise)) break;
    streak += 1;
    cursor.setDate(cursor.getDate() - 1);
  }
  return streak;
}

export function ExercisePage() {
  const [planId, setPlanId] = useState<number | null>(null);
  const [isLoadingPlan, setIsLoadingPlan] = useState(true);

  useEffect(() => {
    (async () => {
      try {
        const latest = await getLatestGoalPlan();
        setPlanId(latest.planId);
      } catch {
        // Silent - the page still works without a plan.
      } finally {
        setIsLoadingPlan(false);
      }
    })();
  }, []);

  return (
    <div className="h-full overflow-y-auto bg-[var(--app-bg)] px-3 sm:px-4 py-4">
      <h1 className="text-sm sm:text-base font-semibold text-[var(--text-primary)]">Ejercicio</h1>
      <p className="mt-1 text-xs text-[var(--text-muted)]">Registra tu actividad y sigue tu racha diaria.</p>

      <ExerciseLogSection />

      {isLoadingPlan ? (
        <div className="mt-4 flex items-center gap-2 text-sm text-[var(--text-muted)]">
          <Loader2 className="h-4 w-4 animate-spin" /> Cargando…
        </div>
      ) : planId != null ? (
        <PlanCheckInTracker planId={planId} />
      ) : (
        <p className="mt-4 rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] p-3 text-sm text-[var(--text-muted)]">
          Crea un plan en Metas para activar tu seguimiento diario.
        </p>
      )}
    </div>
  );
}

function ExerciseLogSection() {
  const [entries, setEntries] = useState<ExerciseEntry[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [mode, setMode] = useState<'preset' | 'custom'>('preset');

  const [exerciseType, setExerciseType] = useState<(typeof EXERCISE_TYPES)[number]>('Pesas');
  const [customType, setCustomType] = useState('');
  const [durationInput, setDurationInput] = useState('');
  const [caloriesInput, setCaloriesInput] = useState('');
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setIsLoading(true);
    setError(null);
    getExerciseHistory(30)
      .then((data) => {
        if (cancelled) return;
        setEntries(data.entries);
      })
      .catch(() => {
        if (cancelled) return;
        setError('No se pudo cargar el historial de ejercicio.');
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [refreshKey]);

  const handleSave = async (e: FormEvent) => {
    e.preventDefault();
    const description = exerciseType === 'Otro' ? customType.trim() : exerciseType;
    const durationMinutes = Number(durationInput);
    if (!description) {
      setSaveError('Escribe el tipo de ejercicio.');
      return;
    }
    if (!durationInput.trim() || Number.isNaN(durationMinutes) || durationMinutes <= 0) {
      setSaveError('Ingresa una duración válida en minutos.');
      return;
    }
    const caloriesBurned = caloriesInput.trim() ? Number(caloriesInput) : null;
    if (caloriesBurned != null && (Number.isNaN(caloriesBurned) || caloriesBurned < 0)) {
      setSaveError('Ingresa calorías válidas.');
      return;
    }

    setIsSaving(true);
    setSaveError(null);
    try {
      await logExercise(description, durationMinutes, caloriesBurned);
      setCustomType('');
      setDurationInput('');
      setCaloriesInput('');
      setRefreshKey((k) => k + 1);
    } catch {
      setSaveError('No se pudo guardar el ejercicio.');
    } finally {
      setIsSaving(false);
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await deleteExerciseEntry(id);
      setRefreshKey((k) => k + 1);
    } catch {
      setError('No se pudo borrar el registro.');
    }
  };

  return (
    <div className="mt-4 rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
      <h2 className="text-sm font-semibold text-[var(--text-secondary)]">Registrar ejercicio</h2>

      <div className="mt-3 grid grid-cols-2 gap-1.5">
        <button
          type="button"
          onClick={() => setMode('preset')}
          className={`rounded-lg border px-2 py-1.5 text-xs font-medium ${
            mode === 'preset'
              ? 'border-[var(--accent)] bg-[var(--accent-soft)] text-[var(--accent-text)]'
              : 'border-[var(--card-border)] text-[var(--text-secondary)]'
          }`}
        >
          Tipo predefinido
        </button>
        <button
          type="button"
          onClick={() => setMode('custom')}
          className={`flex items-center justify-center gap-1 rounded-lg border px-2 py-1.5 text-xs font-medium ${
            mode === 'custom'
              ? 'border-[var(--accent)] bg-[var(--accent-soft)] text-[var(--accent-text)]'
              : 'border-[var(--card-border)] text-[var(--text-secondary)]'
          }`}
        >
          <Sparkles className="h-3.5 w-3.5" />
          Crea tu propio ejercicio
        </button>
      </div>

      {mode === 'preset' ? (
        <form onSubmit={handleSave} className="mt-3 flex flex-col gap-3">
          <label className="text-sm text-[var(--text-secondary)]">
            Tipo
            <select
              value={exerciseType}
              onChange={(e) => setExerciseType(e.target.value as (typeof EXERCISE_TYPES)[number])}
              className="mt-1 block w-full rounded-lg border border-[var(--input-border)] bg-[var(--card-bg)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
            >
              {EXERCISE_TYPES.map((type) => (
                <option key={type} value={type}>
                  {type}
                </option>
              ))}
            </select>
          </label>
          {exerciseType === 'Otro' && (
            <label className="text-sm text-[var(--text-secondary)]">
              Especifica
              <input
                type="text"
                value={customType}
                onChange={(e) => setCustomType(e.target.value)}
                placeholder="Ej. Boxeo"
                className="mt-1 block w-full rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
              />
            </label>
          )}
          <div className="flex gap-3">
            <label className="flex-1 text-sm text-[var(--text-secondary)]">
              Duración (min)
              <input
                type="number"
                min={1}
                value={durationInput}
                onChange={(e) => setDurationInput(e.target.value)}
                placeholder="30"
                className="mt-1 block w-full rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
              />
            </label>
            <label className="flex-1 text-sm text-[var(--text-secondary)]">
              Calorías (opc.)
              <input
                type="number"
                min={0}
                value={caloriesInput}
                onChange={(e) => setCaloriesInput(e.target.value)}
                placeholder="200"
                className="mt-1 block w-full rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
              />
            </label>
          </div>
          <button
            type="submit"
            disabled={isSaving}
            className="flex items-center justify-center gap-2 rounded-full bg-[var(--accent)] py-2.5 text-sm font-medium text-white disabled:opacity-50"
          >
            {isSaving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
            Guardar
          </button>
        </form>
      ) : (
        <CustomExerciseForm onLogged={() => setRefreshKey((k) => k + 1)} />
      )}

      {saveError && mode === 'preset' && (
        <div className="mt-2 flex items-center gap-1.5 text-sm text-red-600">
          <AlertCircle className="h-4 w-4 shrink-0" /> {saveError}
        </div>
      )}
      {error && (
        <div className="mt-2 flex items-center gap-1.5 text-sm text-red-600">
          <AlertCircle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      <PersonalExerciseCatalogList refreshKey={refreshKey} onLogged={() => setRefreshKey((k) => k + 1)} />

      {isLoading ? (
        <div className="mt-4 flex items-center gap-2 text-sm text-[var(--text-muted)]">
          <Loader2 className="h-4 w-4 animate-spin" /> Cargando…
        </div>
      ) : entries.length === 0 ? (
        <p className="mt-4 text-sm text-[var(--text-muted)]">Aún no tienes ejercicios registrados.</p>
      ) : (
        <div className="mt-4 space-y-2">
          {entries.map((entry) => (
            <div
              key={entry.id}
              className="flex items-center justify-between rounded-lg border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2"
            >
              <div className="flex flex-col">
                <span className="text-sm font-medium text-[var(--text-secondary)]">{entry.description}</span>
                <span className="text-xs text-[var(--text-muted)]">{dateTimeFormatter.format(new Date(entry.recordedAtUtc))}</span>
              </div>
              <div className="flex items-center gap-2">
                <span className="text-xs text-[var(--text-secondary)]">
                  {entry.durationMinutes} min{entry.caloriesBurned != null ? ` · ${entry.caloriesBurned} kcal` : ''}
                </span>
                <button
                  type="button"
                  onClick={() => void handleDelete(entry.id)}
                  className="rounded p-1 text-[var(--text-muted)] hover:bg-red-500/10 hover:text-red-600"
                  aria-label="Borrar registro"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/** "Crea tu propio ejercicio": describe any activity in free text, the AI estimates
 * calories burned (and suggests a name) as a preview, and it's only added to today's
 * exercise log if the user explicitly accepts it. */
function CustomExerciseForm({ onLogged }: { onLogged: () => void }) {
  const [description, setDescription] = useState('');
  const [durationInput, setDurationInput] = useState('');
  const [isEstimating, setIsEstimating] = useState(false);
  const [estimateError, setEstimateError] = useState<string | null>(null);
  const [estimate, setEstimate] = useState<ExerciseEstimate | null>(null);
  const [nameInput, setNameInput] = useState('');
  const [caloriesInput, setCaloriesInput] = useState('');
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const handleEstimate = async (e: FormEvent) => {
    e.preventDefault();
    const durationMinutes = Number(durationInput);
    if (!description.trim()) {
      setEstimateError('Describe qué ejercicio hiciste.');
      return;
    }
    if (!durationInput.trim() || Number.isNaN(durationMinutes) || durationMinutes <= 0) {
      setEstimateError('Ingresa una duración válida en minutos.');
      return;
    }

    setIsEstimating(true);
    setEstimateError(null);
    try {
      const result = await estimateExercise(description.trim(), durationMinutes);
      setEstimate(result);
      setNameInput(result.suggestedName);
      setCaloriesInput(String(Math.round(result.estimatedCaloriesBurned)));
    } catch {
      setEstimateError('No se pudo calcular las calorías. Intenta de nuevo.');
    } finally {
      setIsEstimating(false);
    }
  };

  const handleAccept = async () => {
    const durationMinutes = Number(durationInput);
    const caloriesBurned = caloriesInput.trim() ? Number(caloriesInput) : null;
    setIsSaving(true);
    setSaveError(null);
    try {
      await saveCustomExercise(nameInput.trim() || description.trim(), durationMinutes, caloriesBurned);
      setDescription('');
      setDurationInput('');
      setEstimate(null);
      setNameInput('');
      setCaloriesInput('');
      onLogged();
    } catch {
      setSaveError('No se pudo guardar el ejercicio.');
    } finally {
      setIsSaving(false);
    }
  };

  if (estimate) {
    return (
      <div className="mt-3 rounded-xl border border-[var(--card-border)] bg-[var(--app-bg)] p-3">
        <p className="mb-2 text-xs text-[var(--text-muted)]">Revisa y ajusta si quieres antes de agregarlo:</p>
        <label className="mb-2 block text-sm text-[var(--text-secondary)]">
          Nombre
          <input
            type="text"
            value={nameInput}
            onChange={(e) => setNameInput(e.target.value)}
            className="mt-1 block w-full rounded-lg border border-[var(--input-border)] bg-[var(--card-bg)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
          />
        </label>
        <div className="flex gap-3">
          <label className="flex-1 text-sm text-[var(--text-secondary)]">
            Duración (min)
            <input
              type="number"
              min={1}
              value={durationInput}
              onChange={(e) => setDurationInput(e.target.value)}
              className="mt-1 block w-full rounded-lg border border-[var(--input-border)] bg-[var(--card-bg)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
            />
          </label>
          <label className="flex-1 text-sm text-[var(--text-secondary)]">
            Calorías (IA)
            <input
              type="number"
              min={0}
              value={caloriesInput}
              onChange={(e) => setCaloriesInput(e.target.value)}
              className="mt-1 block w-full rounded-lg border border-[var(--input-border)] bg-[var(--card-bg)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
            />
          </label>
        </div>

        {saveError && (
          <div className="mt-2 flex items-center gap-1.5 text-sm text-red-600">
            <AlertCircle className="h-4 w-4 shrink-0" /> {saveError}
          </div>
        )}

        <div className="mt-3 flex gap-2">
          <button
            type="button"
            onClick={() => setEstimate(null)}
            className="flex flex-1 items-center justify-center gap-1.5 rounded-full border border-[var(--card-border)] py-2 text-sm font-medium text-[var(--text-secondary)]"
          >
            <X className="h-4 w-4" />
            Descartar
          </button>
          <button
            type="button"
            onClick={() => void handleAccept()}
            disabled={isSaving}
            className="flex flex-1 items-center justify-center gap-1.5 rounded-full bg-[var(--accent)] py-2 text-sm font-semibold text-white disabled:opacity-60"
          >
            {isSaving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
            Agregar a mi día
          </button>
        </div>
      </div>
    );
  }

  return (
    <form onSubmit={handleEstimate} className="mt-3 flex flex-col gap-3">
      <label className="text-sm text-[var(--text-secondary)]">
        Describe tu ejercicio
        <input
          type="text"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="Ej. Caminé 40 minutos por el parque"
          className="mt-1 block w-full rounded-lg border border-[var(--input-border)] bg-[var(--card-bg)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
        />
      </label>
      <label className="text-sm text-[var(--text-secondary)]">
        Duración (min)
        <input
          type="number"
          min={1}
          value={durationInput}
          onChange={(e) => setDurationInput(e.target.value)}
          placeholder="40"
          className="mt-1 block w-full rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
        />
      </label>
      {estimateError && (
        <div className="flex items-center gap-1.5 text-sm text-red-600">
          <AlertCircle className="h-4 w-4 shrink-0" /> {estimateError}
        </div>
      )}
      <button
        type="submit"
        disabled={isEstimating}
        className="flex items-center justify-center gap-2 rounded-full bg-[var(--accent)] py-2.5 text-sm font-medium text-white disabled:opacity-50"
      >
        {isEstimating ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
        Calcular calorías con IA
      </button>
    </form>
  );
}

function PersonalExerciseCatalogList({ refreshKey, onLogged }: { refreshKey: number; onLogged: () => void }) {
  const [items, setItems] = useState<PersonalExercise[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [loggingId, setLoggingId] = useState<number | null>(null);
  const [deletingId, setDeletingId] = useState<number | null>(null);

  const reload = async () => {
    try {
      const data = await getPersonalExerciseCatalog();
      setItems(data);
      setError(null);
    } catch {
      setError('No se pudo cargar tu catálogo de ejercicios.');
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    setIsLoading(true);
    void reload();
  }, [refreshKey]);

  const handleQuickLog = async (id: number) => {
    setLoggingId(id);
    try {
      await logPersonalExercise(id);
      onLogged();
    } catch {
      setError('No se pudo registrar el ejercicio.');
    } finally {
      setLoggingId(null);
    }
  };

  const handleDelete = async (id: number) => {
    setDeletingId(id);
    try {
      await deletePersonalExercise(id);
      await reload();
    } catch {
      setError('No se pudo eliminar el ejercicio.');
    } finally {
      setDeletingId(null);
    }
  };

  if (isLoading || items.length === 0) {
    return null;
  }

  return (
    <div className="mt-4 border-t border-[var(--card-border)] pt-3">
      <p className="mb-2 text-xs font-medium text-[var(--text-muted)]">Mis ejercicios guardados</p>
      {error && (
        <div className="mb-2 flex items-center gap-1.5 text-sm text-red-600">
          <AlertCircle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}
      <div className="flex flex-col gap-2">
        {items.map((item) => (
          <div
            key={item.id}
            className="flex items-center justify-between gap-2 rounded-xl border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2"
          >
            <div className="min-w-0">
              <p className="truncate text-sm font-medium text-[var(--text-primary)]">{item.name}</p>
              <p className="text-xs text-[var(--text-muted)]">
                {item.durationMinutes} min{item.caloriesBurned != null ? ` · ${Math.round(item.caloriesBurned)} kcal` : ''}
              </p>
            </div>
            <div className="flex shrink-0 items-center gap-1.5">
              <button
                type="button"
                onClick={() => void handleQuickLog(item.id)}
                disabled={loggingId === item.id}
                className="flex items-center gap-1 rounded-full bg-[var(--accent)] px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-60"
              >
                {loggingId === item.id ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <RotateCcw className="h-3.5 w-3.5" />}
                Agregar
              </button>
              <button
                type="button"
                onClick={() => void handleDelete(item.id)}
                disabled={deletingId === item.id}
                aria-label="Eliminar de mi catálogo"
                className="flex items-center justify-center rounded-full border border-[var(--card-border)] p-1.5 text-[var(--text-muted)] disabled:opacity-60"
              >
                {deletingId === item.id ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Trash2 className="h-3.5 w-3.5" />}
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function PlanCheckInTracker({ planId }: { planId: number }) {
  const [history, setHistory] = useState<GoalPlanCheckIn[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [stepsWalked, setStepsWalked] = useState('');
  const [followedNutrition, setFollowedNutrition] = useState(false);
  const [followedExercise, setFollowedExercise] = useState(false);
  const [notes, setNotes] = useState('');

  useEffect(() => {
    setIsLoading(true);
    (async () => {
      try {
        const checkIns = await getGoalPlanCheckInHistory(planId, 14);
        setHistory(checkIns);
        const today = checkIns.find((c) => c.checkInDate === todayIso());
        if (today) {
          setStepsWalked(today.stepsWalked != null ? String(today.stepsWalked) : '');
          setFollowedNutrition(today.followedNutrition);
          setFollowedExercise(today.followedExercise);
          setNotes(today.notes ?? '');
        }
      } catch {
        // Silent - tracker is a nice-to-have.
      } finally {
        setIsLoading(false);
      }
    })();
  }, [planId]);

  const streak = useMemo(() => computeStreak(history), [history]);

  const handleSave = async () => {
    setIsSaving(true);
    setError(null);
    try {
      const saved = await saveGoalPlanCheckIn(planId, {
        checkInDate: todayIso(),
        stepsWalked: stepsWalked.trim() ? Number(stepsWalked) : null,
        followedNutrition,
        followedExercise,
        notes: notes.trim() || undefined,
      });
      setHistory((prev) => {
        const withoutToday = prev.filter((c) => c.checkInDate !== saved.checkInDate);
        return [saved, ...withoutToday].sort((a, b) => (a.checkInDate < b.checkInDate ? 1 : -1));
      });
    } catch {
      setError('No se pudo guardar el seguimiento de hoy.');
    } finally {
      setIsSaving(false);
    }
  };

  if (isLoading) {
    return (
      <div className="mt-4 flex items-center gap-2 text-sm text-[var(--text-muted)]">
        <Loader2 className="h-4 w-4 animate-spin" /> Cargando seguimiento…
      </div>
    );
  }

  return (
    <div className="mt-4 rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-semibold text-[var(--text-secondary)]">Seguimiento diario</h2>
        {streak > 0 && (
          <span className="flex items-center gap-1 rounded-full bg-orange-100 px-2.5 py-1 text-xs font-medium text-orange-700">
            🔥 {streak} {streak === 1 ? 'día' : 'días'}
          </span>
        )}
      </div>

      <div className="mt-3 flex flex-col gap-2">
        <label className="flex items-center gap-2 text-sm text-[var(--text-secondary)]">
          <Footprints className="h-4 w-4 shrink-0 text-[var(--text-muted)]" />
          Pasos
          <input
            type="number"
            min={0}
            value={stepsWalked}
            onChange={(e) => setStepsWalked(e.target.value)}
            placeholder="0"
            className="ml-auto w-24 rounded-lg border border-[var(--input-border)] px-2 py-1.5 text-sm outline-none focus:border-[var(--accent)]"
          />
        </label>
        <button
          type="button"
          onClick={() => setFollowedNutrition((v) => !v)}
          className="flex items-center gap-2 rounded-lg border border-[var(--card-border)] px-3 py-2 text-left text-sm text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]"
        >
          {followedNutrition ? <CheckCircle2 className="h-4 w-4 text-green-600" /> : <Circle className="h-4 w-4 text-[var(--text-muted)]" />}
          Seguí la nutrición hoy
        </button>
        <button
          type="button"
          onClick={() => setFollowedExercise((v) => !v)}
          className="flex items-center gap-2 rounded-lg border border-[var(--card-border)] px-3 py-2 text-left text-sm text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]"
        >
          {followedExercise ? <CheckCircle2 className="h-4 w-4 text-green-600" /> : <Circle className="h-4 w-4 text-[var(--text-muted)]" />}
          Hice ejercicio hoy
        </button>
      </div>

      <textarea
        value={notes}
        onChange={(e) => setNotes(e.target.value)}
        placeholder="Notas de hoy (opcional)"
        rows={2}
        className="mt-3 w-full rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm text-[var(--text-primary)] outline-none focus:border-[var(--accent)]"
      />

      {error && (
        <div className="mt-2 flex items-center gap-1.5 text-sm text-red-600">
          <AlertCircle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      <button
        type="button"
        onClick={() => void handleSave()}
        disabled={isSaving}
        className="mt-3 flex w-full items-center justify-center gap-2 rounded-full bg-[var(--accent)] py-2.5 text-sm font-medium text-white disabled:opacity-50"
      >
        {isSaving ? <Loader2 className="h-4 w-4 animate-spin" /> : 'Guardar seguimiento'}
      </button>
    </div>
  );
}
