import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { Loader2, AlertCircle, CheckCircle2, Circle, Footprints, Save, Apple, Dumbbell, Plus, Trash2 } from 'lucide-react';
import {
  getLatestGoalPlan,
  saveGoalPlanCheckIn,
  getGoalPlanCheckInHistory,
  type GoalPlanCheckIn,
} from '../api/goalsApi';
import { getExerciseHistory, logExercise, deleteExerciseEntry, type ExerciseEntry } from '../api/exerciseApi';

const CENTRAL_TIME_ZONE = 'America/Chicago';

const EXERCISE_TYPES = ['Pesas', 'Correr/Trotar', 'Nadar', 'Ciclismo', 'Caminar', 'Yoga/Estiramiento', 'Otro'] as const;

function todayIso(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: CENTRAL_TIME_ZONE }).format(new Date());
}

const dayLabelFormatter = new Intl.DateTimeFormat('es', { weekday: 'short', day: 'numeric', timeZone: CENTRAL_TIME_ZONE });
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
  // Allow today to be "in progress" (not yet checked) without breaking a streak built through yesterday.
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

export function ExercisesPage() {
  const [planId, setPlanId] = useState<number | null>(null);
  const [isLoadingPlan, setIsLoadingPlan] = useState(true);

  useEffect(() => {
    (async () => {
      try {
        const latest = await getLatestGoalPlan();
        setPlanId(latest.planId);
      } catch {
        // Silent - the page still explains how to get a plan if this fails.
      } finally {
        setIsLoadingPlan(false);
      }
    })();
  }, []);

  return (
    <div className="h-full overflow-y-auto bg-[var(--app-bg)] p-6">
      <h2 className="text-xl font-semibold text-[var(--text-primary)]">Ejercicios</h2>
      <p className="mt-1 text-sm text-[var(--text-muted)]">
        Aquí verás tu historial de ejercicio registrado y tu seguimiento diario.
      </p>

      <ExerciseLogSection />

      {isLoadingPlan ? (
        <div className="mt-6 flex items-center gap-2 text-sm text-[var(--text-muted)]">
          <Loader2 className="h-4 w-4 animate-spin" /> Cargando…
        </div>
      ) : planId != null ? (
        <PlanCheckInTracker planId={planId} />
      ) : (
        <p className="mt-6 text-sm text-[var(--text-muted)]">
          Aún no tienes un plan. Crea uno en la página de Objetivos para empezar tu seguimiento diario.
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
    <div className="mt-6 max-w-2xl rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-5 shadow-sm">
      <h4 className="text-sm font-semibold text-[var(--text-secondary)]">Registrar ejercicio</h4>
      <p className="mt-1 text-xs text-[var(--text-muted)]">Elige el tipo, cuánto tiempo y (opcional) calorías quemadas.</p>

      <form onSubmit={handleSave} className="mt-4 flex flex-wrap items-end gap-3">
        <label className="text-sm text-[var(--text-secondary)]">
          Tipo
          <select
            value={exerciseType}
            onChange={(e) => setExerciseType(e.target.value as (typeof EXERCISE_TYPES)[number])}
            className="mt-1 block rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
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
              className="mt-1 block w-32 rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
            />
          </label>
        )}
        <label className="text-sm text-[var(--text-secondary)]">
          Duración (min)
          <input
            type="number"
            min={1}
            value={durationInput}
            onChange={(e) => setDurationInput(e.target.value)}
            placeholder="30"
            className="mt-1 block w-24 rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
          />
        </label>
        <label className="text-sm text-[var(--text-secondary)]">
          Calorías (opcional)
          <input
            type="number"
            min={0}
            value={caloriesInput}
            onChange={(e) => setCaloriesInput(e.target.value)}
            placeholder="200"
            className="mt-1 block w-28 rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
          />
        </label>
        <button
          type="submit"
          disabled={isSaving}
          className="flex items-center gap-2 rounded-full bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
        >
          {isSaving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
          Guardar
        </button>
      </form>

      {saveError && (
        <div className="mt-2 flex items-center gap-1.5 text-sm text-red-600">
          <AlertCircle className="h-4 w-4 shrink-0" /> {saveError}
        </div>
      )}
      {error && (
        <div className="mt-2 flex items-center gap-1.5 text-sm text-red-600">
          <AlertCircle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}

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
              <div className="flex items-center gap-3">
                <span className="text-sm text-[var(--text-secondary)]">
                  {entry.durationMinutes} min{entry.caloriesBurned != null ? ` · ${entry.caloriesBurned} kcal` : ''}
                </span>
                <button
                  type="button"
                  onClick={() => handleDelete(entry.id)}
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
        // Silent - tracker is a nice-to-have, plan is still usable without it.
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
      <div className="mt-6 flex items-center gap-2 text-sm text-[var(--text-muted)]">
        <Loader2 className="h-4 w-4 animate-spin" /> Cargando seguimiento…
      </div>
    );
  }

  return (
    <div className="mt-6 max-w-2xl rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-5 shadow-sm">
      <div className="flex items-center justify-between">
        <h4 className="text-sm font-semibold text-[var(--text-secondary)]">Seguimiento diario</h4>
        {streak > 0 && (
          <span className="flex items-center gap-1 rounded-full bg-orange-100 px-2.5 py-1 text-xs font-medium text-orange-700">
            🔥 {streak} {streak === 1 ? 'día seguido' : 'días seguidos'}
          </span>
        )}
      </div>
      <p className="mt-1 text-xs text-[var(--text-muted)]">Marca hoy si seguiste el plan y cuántos pasos caminaste.</p>

      <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
        <label className="flex items-center gap-2 text-sm text-[var(--text-secondary)]">
          <Footprints className="h-4 w-4 text-[var(--text-muted)]" />
          Pasos caminados
          <input
            type="number"
            min={0}
            value={stepsWalked}
            onChange={(e) => setStepsWalked(e.target.value)}
            placeholder="0"
            className="w-28 rounded-lg border border-[var(--input-border)] px-2 py-1.5 text-sm outline-none focus:border-[var(--accent)]"
          />
        </label>
        <button
          type="button"
          onClick={() => setFollowedNutrition((v) => !v)}
          className="flex items-center gap-2 rounded-lg border border-[var(--card-border)] px-3 py-1.5 text-left text-sm text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]"
        >
          {followedNutrition ? (
            <CheckCircle2 className="h-4 w-4 text-green-600" />
          ) : (
            <Circle className="h-4 w-4 text-[var(--text-muted)]" />
          )}
          Seguí la nutrición hoy
        </button>
        <button
          type="button"
          onClick={() => setFollowedExercise((v) => !v)}
          className="flex items-center gap-2 rounded-lg border border-[var(--card-border)] px-3 py-1.5 text-left text-sm text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]"
        >
          {followedExercise ? (
            <CheckCircle2 className="h-4 w-4 text-green-600" />
          ) : (
            <Circle className="h-4 w-4 text-[var(--text-muted)]" />
          )}
          Hice ejercicio hoy
        </button>
      </div>

      <textarea
        value={notes}
        onChange={(e) => setNotes(e.target.value)}
        placeholder="Notas de hoy (opcional)"
        rows={2}
        className="mt-3 w-full rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
      />

      {error && (
        <div className="mt-2 flex items-center gap-1.5 text-sm text-red-600">
          <AlertCircle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      <button
        type="button"
        onClick={handleSave}
        disabled={isSaving}
        className="mt-3 flex items-center gap-2 rounded-full bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
      >
        {isSaving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
        Guardar hoy
      </button>

      {history.length > 0 && (
        <div className="mt-5 border-t border-[var(--card-border)] pt-4">
          <p className="text-xs font-medium text-[var(--text-muted)]">Últimos días</p>
          <div className="mt-2 flex flex-wrap gap-2">
            {history.map((c) => (
              <div
                key={c.checkInDate}
                className={`flex min-w-[68px] flex-col items-center rounded-lg border px-2 py-1.5 text-xs ${
                  c.followedNutrition && c.followedExercise
                    ? 'border-green-200 bg-green-50 text-green-700'
                    : 'border-[var(--card-border)] bg-[var(--app-bg)] text-[var(--text-muted)]'
                }`}
              >
                <span className="font-medium">{dayLabelFormatter.format(new Date(`${c.checkInDate}T00:00:00`))}</span>
                <span className="mt-0.5 flex items-center gap-1">
                  {c.followedNutrition ? <Apple className="h-3 w-3" /> : null}
                  {c.followedExercise ? <Dumbbell className="h-3 w-3" /> : null}
                </span>
                {c.stepsWalked != null && <span className="mt-0.5">{c.stepsWalked} pasos</span>}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
