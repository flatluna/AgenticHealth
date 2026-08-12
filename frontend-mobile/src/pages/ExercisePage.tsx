import { useEffect, useMemo, useState } from 'react';
import { AlertCircle, ChevronLeft, ChevronRight, Circle, CheckCircle2, Droplets, Footprints, Loader2, Trash2 } from 'lucide-react';
import { getLatestGoalPlan, saveGoalPlanCheckIn, getGoalPlanCheckInHistory, type GoalPlanCheckIn } from '../api/goalsApi';
import { getExerciseHistory, deleteExerciseEntry, type ExerciseEntry } from '../api/exerciseApi';

const CENTRAL_TIME_ZONE = 'America/Chicago';

function startOfDay(date: Date): Date {
  const d = new Date(date);
  d.setHours(0, 0, 0, 0);
  return d;
}

function addDays(date: Date, days: number): Date {
  const next = new Date(date);
  next.setDate(next.getDate() + days);
  return next;
}

function isSameDay(a: Date, b: Date): boolean {
  return a.toDateString() === b.toDateString();
}

function todayIso(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: CENTRAL_TIME_ZONE }).format(new Date());
}

const dateLabelFormatter = new Intl.DateTimeFormat('es', {
  day: 'numeric',
  month: 'long',
  timeZone: CENTRAL_TIME_ZONE,
});

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
  const [anchorDate, setAnchorDate] = useState(() => startOfDay(new Date()));
  const [entries, setEntries] = useState<ExerciseEntry[]>([]);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [planId, setPlanId] = useState<number | null>(null);
  const [isLoadingPlan, setIsLoadingPlan] = useState(true);

  const dayEntries = useMemo(
    () => entries.filter((entry) => isSameDay(new Date(entry.recordedAtUtc), anchorDate)),
    [entries, anchorDate],
  );

  const loadEntries = async () => {
    const daysBack = Math.max(1, Math.ceil((startOfDay(new Date()).getTime() - anchorDate.getTime()) / 86400000) + 2);
    const result = await getExerciseHistory(daysBack);
    setEntries(result.entries);
  };

  useEffect(() => {
    let cancelled = false;
    setSaveError(null);
    loadEntries()
      .catch(() => {
        if (!cancelled) {
          setSaveError('No se pudo cargar la información de ejercicio.');
        }
      });

    return () => {
      cancelled = true;
    };
  }, [anchorDate]);

  useEffect(() => {
    (async () => {
      try {
        const latest = await getLatestGoalPlan();
        setPlanId(latest.planId);
      } catch {
        // no plan yet; this page still works without it
      } finally {
        setIsLoadingPlan(false);
      }
    })();
  }, []);

  const handleDelete = async (id: number) => {
    try {
      await deleteExerciseEntry(id);
      await loadEntries();
    } catch {
      setSaveError('No se pudo borrar este registro.');
    }
  };

  const dateLabel = useMemo(() => {
    const label = dateLabelFormatter.format(anchorDate);
    return isSameDay(anchorDate, new Date()) ? `Hoy · ${label}` : label;
  }, [anchorDate]);

  return (
    <div className="h-full overflow-y-auto bg-[var(--app-bg)] px-3 py-4 sm:px-4">
      <div className="mx-auto max-w-3xl">
        <div className="flex items-center justify-between gap-3 rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-3 shadow-sm">
          <button
            type="button"
            onClick={() => setAnchorDate((prev) => addDays(prev, -1))}
            className="flex h-9 w-9 items-center justify-center rounded-full border border-[var(--card-border)] text-[var(--text-secondary)]"
            aria-label="Día anterior"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <div className="text-center">
            <p className="text-[10px] uppercase tracking-[0.18em] text-[var(--text-muted)]">Ejercicio</p>
            <p className="mt-1 text-sm font-semibold text-[var(--text-primary)] capitalize">{dateLabel}</p>
          </div>
          <button
            type="button"
            onClick={() => setAnchorDate((prev) => addDays(prev, 1))}
            className="flex h-9 w-9 items-center justify-center rounded-full border border-[var(--card-border)] text-[var(--text-secondary)]"
            aria-label="Día siguiente"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>

        {saveError && (
          <div className="mt-4 flex items-center gap-1.5 text-sm text-red-600">
            <AlertCircle className="h-4 w-4 shrink-0" /> {saveError}
          </div>
        )}

        <div className="mt-4 rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
          <div className="flex items-center justify-between gap-2">
            <h2 className="text-sm font-semibold text-[var(--text-secondary)]">Ejercicios del día</h2>
            <div className="flex items-center gap-2">
              <span className="rounded-full bg-[var(--app-bg)] px-2.5 py-1 text-xs font-medium text-[var(--text-muted)]">
                {dayEntries.reduce((sum, item) => sum + item.durationMinutes, 0)} min
              </span>
              <span className="rounded-full bg-[var(--app-bg)] px-2.5 py-1 text-xs font-medium text-[var(--text-muted)]">
                {Math.round(dayEntries.reduce((sum, item) => sum + (item.caloriesBurned ?? 0), 0))} kcal
              </span>
            </div>
          </div>

          {dayEntries.length === 0 ? (
            <p className="mt-3 text-sm text-[var(--text-muted)]">Aún no hay ejercicios agregados para esta fecha.</p>
          ) : (
            <div className="mt-3 space-y-2">
              {dayEntries.map((entry) => (
                <div key={entry.id} className="flex items-center justify-between gap-3 rounded-xl border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium text-[var(--text-primary)]">{entry.description}</p>
                    <p className="text-xs text-[var(--text-muted)]">
                      {dateTimeFormatter.format(new Date(entry.recordedAtUtc))}
                      {entry.caloriesBurned != null ? ` · ${Math.round(entry.caloriesBurned)} kcal` : ''}
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={() => void handleDelete(entry.id)}
                    className="rounded p-1 text-[var(--text-muted)] hover:bg-red-500/10 hover:text-red-600"
                    aria-label="Eliminar ejercicio del día"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>

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

    </div>
  );
}

function PlanCheckInTracker({ planId }: { planId: number }) {
  const [history, setHistory] = useState<GoalPlanCheckIn[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [stepsWalked, setStepsWalked] = useState('');
  const [waterMl, setWaterMl] = useState(0);
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
          setWaterMl(today.waterMl ?? 0);
          setFollowedNutrition(today.followedNutrition);
          setFollowedExercise(today.followedExercise);
          setNotes(today.notes ?? '');
        }
      } catch {
        // no extra UI required if the tracker cannot load
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
        waterMl,
        followedNutrition,
        followedExercise,
        notes: notes.trim() || undefined,
      });
      setWaterMl(saved.waterMl ?? 0);
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

        <div className="rounded-xl border border-sky-200 bg-sky-50/60 p-3">
          <div className="flex items-center justify-between gap-3">
            <div className="flex items-center gap-2 text-sm font-medium text-sky-700">
              <Droplets className="h-4 w-4" />
              Agua
            </div>
            <span className="text-xs font-semibold text-sky-700">{waterMl} ml</span>
          </div>

          <div className="mt-3 grid grid-cols-2 gap-2">
            <button
              type="button"
              onClick={() => setWaterMl((value) => Math.max(0, value - 500))}
              className="rounded-lg border border-sky-300 bg-white px-2 py-1.5 text-xs font-semibold text-sky-700"
            >
              - 500 ml
            </button>
            <button
              type="button"
              onClick={() => setWaterMl((value) => value + 500)}
              className="rounded-lg bg-sky-500 px-2 py-1.5 text-xs font-semibold text-white"
            >
              + 500 ml
            </button>
            <button
              type="button"
              onClick={() => setWaterMl((value) => Math.max(0, value - 250))}
              className="rounded-lg border border-cyan-300 bg-white px-2 py-1.5 text-xs font-semibold text-cyan-700"
            >
              - 250 ml
            </button>
            <button
              type="button"
              onClick={() => setWaterMl((value) => value + 250)}
              className="rounded-lg bg-cyan-500 px-2 py-1.5 text-xs font-semibold text-white"
            >
              + 250 ml
            </button>
          </div>
        </div>

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
