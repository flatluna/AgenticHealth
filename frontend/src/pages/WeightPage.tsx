import { useEffect, useMemo, useState } from 'react';
import { Scale, TrendingDown, TrendingUp, Minus, Plus, Trash2, Loader2, AlertCircle, CalendarClock } from 'lucide-react';
import { getWeightHistory, logWeight, deleteWeightEntry, type WeightEntry } from '../api/weightApi';

type RangeOption = 30 | 90 | 180 | 365;

const RANGE_LABEL: Record<RangeOption, string> = {
  30: '30 días',
  90: '90 días',
  180: '6 meses',
  365: '1 año',
};

const CENTRAL_TIME_ZONE = 'America/Chicago';
const dateFormatter = new Intl.DateTimeFormat('es', { day: 'numeric', month: 'short', timeZone: CENTRAL_TIME_ZONE });
const dateTimeFormatter = new Intl.DateTimeFormat('es', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  timeZone: CENTRAL_TIME_ZONE,
});

function todayIsoDate(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: CENTRAL_TIME_ZONE }).format(new Date());
}

export function WeightPage() {
  const [range, setRange] = useState<RangeOption>(90);
  const [entries, setEntries] = useState<WeightEntry[]>([]);
  const [latestWeightKg, setLatestWeightKg] = useState<number | null>(null);
  const [changeKg, setChangeKg] = useState<number | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);

  const [weightInput, setWeightInput] = useState('');
  const [dateInput, setDateInput] = useState(todayIsoDate());
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setIsLoading(true);
    setError(null);
    getWeightHistory(range)
      .then((data) => {
        if (cancelled) return;
        setEntries(data.entries);
        setLatestWeightKg(data.latestWeightKg);
        setChangeKg(data.changeKg);
      })
      .catch(() => {
        if (cancelled) return;
        setError('No se pudo cargar el historial de peso. Verifica que el backend esté corriendo.');
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [range, refreshKey]);

  const handleAddWeight = async (e: React.FormEvent) => {
    e.preventDefault();
    const weightKg = Number(weightInput);
    if (!weightInput.trim() || Number.isNaN(weightKg) || weightKg <= 0) {
      setSaveError('Ingresa un peso válido.');
      return;
    }
    setIsSaving(true);
    setSaveError(null);
    try {
      // Use the real current instant when logging for today; only fall back to a fixed
      // time-of-day when backdating to a past date (exact time is unknown then).
      const recordedAtIso =
        dateInput === todayIsoDate() ? new Date().toISOString() : new Date(`${dateInput}T12:00:00`).toISOString();
      await logWeight(weightKg, recordedAtIso);
      setWeightInput('');
      setDateInput(todayIsoDate());
      setRefreshKey((k) => k + 1);
    } catch {
      setSaveError('No se pudo guardar el peso.');
    } finally {
      setIsSaving(false);
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await deleteWeightEntry(id);
      setRefreshKey((k) => k + 1);
    } catch {
      setError('No se pudo borrar el registro.');
    }
  };

  const trend = changeKg == null ? null : changeKg > 0 ? 'up' : changeKg < 0 ? 'down' : 'flat';

  return (
    <div className="h-full overflow-y-auto p-6">
      <div className="flex flex-col gap-1 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h2 className="text-xl font-semibold text-[var(--text-primary)]">Peso</h2>
          <p className="mt-1 text-sm text-[var(--text-muted)]">Historial de peso registrado, separado de tu plan de Objetivos.</p>
        </div>
        <div className="flex rounded-lg border border-[var(--card-border)] bg-[var(--app-bg)] p-0.5">
          {(Object.keys(RANGE_LABEL) as unknown as RangeOption[]).map((option) => (
            <button
              key={option}
              type="button"
              onClick={() => setRange(Number(option) as RangeOption)}
              className={`rounded-md px-3 py-1.5 text-sm font-medium transition-colors ${
                Number(option) === range ? 'bg-[var(--accent)] text-white' : 'text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]'
              }`}
            >
              {RANGE_LABEL[Number(option) as RangeOption]}
            </button>
          ))}
        </div>
      </div>

      {error && (
        <div className="mt-4 flex items-center gap-1.5 rounded-lg bg-red-50 px-4 py-2 text-sm text-red-600">
          <AlertCircle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      <div className="mt-6 grid gap-4 sm:grid-cols-3">
        <SummaryCard
          icon={Scale}
          label="Peso actual"
          value={latestWeightKg != null ? `${latestWeightKg} kg` : '—'}
        />
        <SummaryCard
          icon={trend === 'up' ? TrendingUp : trend === 'down' ? TrendingDown : Minus}
          label={`Cambio en ${RANGE_LABEL[range]}`}
          value={changeKg != null ? `${changeKg > 0 ? '+' : ''}${changeKg} kg` : '—'}
          tone={trend === 'up' ? 'warn' : trend === 'down' ? 'good' : 'neutral'}
        />
        <SummaryCard icon={CalendarClock} label="Registros" value={String(entries.length)} />
      </div>

      <div className="mt-6 rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-5 shadow-sm">
        <h3 className="text-sm font-semibold text-[var(--text-secondary)]">Tendencia</h3>
        {isLoading ? (
          <div className="mt-6 flex items-center gap-2 text-sm text-[var(--text-muted)]">
            <Loader2 className="h-4 w-4 animate-spin" /> Cargando…
          </div>
        ) : entries.length === 0 ? (
          <p className="mt-3 text-sm text-[var(--text-muted)]">Aún no hay registros de peso en este rango.</p>
        ) : (
          <WeightChart entries={entries} />
        )}
      </div>

      <div className="mt-6 rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-5 shadow-sm">
        <h3 className="text-sm font-semibold text-[var(--text-secondary)]">Registrar peso</h3>
        <form onSubmit={handleAddWeight} className="mt-3 flex flex-wrap items-end gap-3">
          <label className="text-sm text-[var(--text-secondary)]">
            Peso (kg)
            <input
              type="number"
              min={1}
              step="0.1"
              value={weightInput}
              onChange={(e) => setWeightInput(e.target.value)}
              placeholder="72.5"
              className="mt-1 block w-28 rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
            />
          </label>
          <label className="text-sm text-[var(--text-secondary)]">
            Fecha
            <input
              type="date"
              value={dateInput}
              onChange={(e) => setDateInput(e.target.value)}
              className="mt-1 block rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
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
      </div>

      {entries.length > 0 && (
        <div className="mt-6 rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-5 shadow-sm">
          <h3 className="text-sm font-semibold text-[var(--text-secondary)]">Historial</h3>
          <div className="mt-3 space-y-2">
            {[...entries].reverse().map((entry) => (
              <div
                key={entry.id}
                className="flex items-center justify-between rounded-lg border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2"
              >
                <span className="text-sm text-[var(--text-secondary)]">{dateTimeFormatter.format(new Date(entry.recordedAtUtc))}</span>
                <div className="flex items-center gap-3">
                  <span className="text-sm font-semibold text-[var(--text-primary)]">{entry.weightKg} kg</span>
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
        </div>
      )}
    </div>
  );
}

function SummaryCard({
  icon: Icon,
  label,
  value,
  tone = 'neutral',
}: {
  icon: typeof Scale;
  label: string;
  value: string;
  tone?: 'good' | 'warn' | 'neutral';
}) {
  const toneClass = tone === 'good' ? 'bg-green-500/15 text-green-500' : tone === 'warn' ? 'bg-orange-500/15 text-orange-500' : 'bg-[var(--accent-soft)] text-[var(--accent-text)]';
  return (
    <div className="flex items-center gap-3 rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
      <div className={`rounded-full p-2 ${toneClass}`}>
        <Icon className="h-5 w-5" />
      </div>
      <div>
        <p className="text-xs text-[var(--text-muted)]">{label}</p>
        <p className="text-lg font-semibold text-[var(--text-primary)]">{value}</p>
      </div>
    </div>
  );
}

function WeightChart({ entries }: { entries: WeightEntry[] }) {
  const width = 640;
  const height = 200;
  const paddingX = 16;
  const paddingY = 16;

  const weights = entries.map((e) => e.weightKg);
  const minWeight = Math.min(...weights);
  const maxWeight = Math.max(...weights);
  const span = maxWeight - minWeight || 1;

  const points = useMemo(
    () =>
      entries.map((entry, index) => {
        const x = entries.length === 1 ? width / 2 : paddingX + (index / (entries.length - 1)) * (width - paddingX * 2);
        const y = height - paddingY - ((entry.weightKg - minWeight) / span) * (height - paddingY * 2);
        return { x, y, entry };
      }),
    [entries, minWeight, span],
  );

  const linePath = points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${p.x.toFixed(1)} ${p.y.toFixed(1)}`).join(' ');

  return (
    <div className="mt-4 overflow-x-auto">
      <svg viewBox={`0 0 ${width} ${height}`} className="h-48 w-full min-w-[480px]">
        <path d={linePath} fill="none" stroke="#9333ea" strokeWidth={2} />
        {points.map((p, i) => (
          <circle key={i} cx={p.x} cy={p.y} r={3.5} fill="#9333ea">
            <title>
              {dateFormatter.format(new Date(p.entry.recordedAtUtc))} · {p.entry.weightKg} kg
            </title>
          </circle>
        ))}
      </svg>
      <div className="mt-1 flex justify-between text-xs text-[var(--text-muted)]">
        <span>{dateFormatter.format(new Date(entries[0].recordedAtUtc))}</span>
        <span>{dateFormatter.format(new Date(entries[entries.length - 1].recordedAtUtc))}</span>
      </div>
    </div>
  );
}
