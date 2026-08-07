import { useEffect, useState, type FormEvent } from 'react';
import { Scale, TrendingDown, TrendingUp, Minus, Plus, Trash2, Loader2, AlertCircle, CalendarClock } from 'lucide-react';
import { getWeightHistory, logWeight, deleteWeightEntry, type WeightEntry } from '../api/weightApi';
import { formatLb, lbToKg } from '../utils/units';

type RangeOption = 30 | 90 | 180 | 365;

const RANGE_LABEL: Record<RangeOption, string> = {
  30: '30d',
  90: '90d',
  180: '6m',
  365: '1a',
};

const CENTRAL_TIME_ZONE = 'America/Chicago';
const dateTimeFormatter = new Intl.DateTimeFormat('es', {
  day: 'numeric',
  month: 'short',
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
        setError('No se pudo cargar el historial de peso.');
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [range, refreshKey]);

  const handleAddWeight = async (e: FormEvent) => {
    e.preventDefault();
    const weightLb = Number(weightInput);
    if (!weightInput.trim() || Number.isNaN(weightLb) || weightLb <= 0) {
      setSaveError('Ingresa un peso válido.');
      return;
    }
    setIsSaving(true);
    setSaveError(null);
    try {
      const recordedAtIso =
        dateInput === todayIsoDate() ? new Date().toISOString() : new Date(`${dateInput}T12:00:00`).toISOString();
      await logWeight(lbToKg(weightLb), recordedAtIso);
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
    <div className="h-full overflow-y-auto bg-[var(--app-bg)] px-3 sm:px-4 py-4">
      <div className="flex items-center justify-between">
        <h1 className="text-sm sm:text-base font-semibold text-[var(--text-primary)]\">Peso</h1>
        <div className="flex rounded-lg border border-[var(--card-border)] bg-[var(--card-bg)] p-0.5">
          {(Object.keys(RANGE_LABEL) as unknown as RangeOption[]).map((option) => (
            <button
              key={option}
              type="button"
              onClick={() => setRange(Number(option) as RangeOption)}
              className={`rounded-md px-2 py-1 text-xs font-medium transition-colors ${
                Number(option) === range ? 'bg-[var(--accent)] text-white' : 'text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]'
              }`}
            >
              {RANGE_LABEL[Number(option) as RangeOption]}
            </button>
          ))}
        </div>
      </div>

      {error && (
        <div className="mt-3 flex items-center gap-1.5 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">
          <AlertCircle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      <div className="mt-3 grid grid-cols-3 gap-2">
        <SummaryCard icon={Scale} label="Actual" value={latestWeightKg != null ? `${formatLb(latestWeightKg)} lb` : '—'} />
        <SummaryCard
          icon={trend === 'up' ? TrendingUp : trend === 'down' ? TrendingDown : Minus}
          label="Cambio"
          value={changeKg != null ? `${changeKg > 0 ? '+' : ''}${formatLb(changeKg)} lb` : '—'}
        />
        <SummaryCard icon={CalendarClock} label="Registros" value={String(entries.length)} />
      </div>

      <div className="mt-4 rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
        <h2 className="text-sm font-semibold text-[var(--text-secondary)]">Registrar peso</h2>
        <form onSubmit={handleAddWeight} className="mt-3 flex flex-col gap-3">
          <div className="flex gap-3">
            <label className="flex-1 text-sm text-[var(--text-secondary)]">
              Peso (lb)
              <input
                type="number"
                min={1}
                step="0.1"
                value={weightInput}
                onChange={(e) => setWeightInput(e.target.value)}
                placeholder="160"
                className="mt-1 block w-full rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
              />
            </label>
            <label className="flex-1 text-sm text-[var(--text-secondary)]">
              Fecha
              <input
                type="date"
                value={dateInput}
                onChange={(e) => setDateInput(e.target.value)}
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
        {saveError && (
          <div className="mt-2 flex items-center gap-1.5 text-sm text-red-600">
            <AlertCircle className="h-4 w-4 shrink-0" /> {saveError}
          </div>
        )}
      </div>

      <div className="mt-4 rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
        <h2 className="text-sm font-semibold text-[var(--text-secondary)]">Historial</h2>
        {isLoading ? (
          <div className="mt-3 flex items-center gap-2 text-sm text-[var(--text-muted)]">
            <Loader2 className="h-4 w-4 animate-spin" /> Cargando…
          </div>
        ) : entries.length === 0 ? (
          <p className="mt-3 text-sm text-[var(--text-muted)]">Aún no hay registros en este rango.</p>
        ) : (
          <div className="mt-3 space-y-2">
            {[...entries].reverse().map((entry) => (
              <div
                key={entry.id}
                className="flex items-center justify-between rounded-lg border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2"
              >
                <span className="text-sm text-[var(--text-muted)]">{dateTimeFormatter.format(new Date(entry.recordedAtUtc))}</span>
                <div className="flex items-center gap-2">
                  <span className="text-sm font-semibold text-[var(--text-primary)]">{formatLb(entry.weightKg)} lb</span>
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
    </div>
  );
}

function SummaryCard({ icon: Icon, label, value }: { icon: typeof Scale; label: string; value: string }) {
  return (
    <div className="flex flex-col items-center gap-1 rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] p-3 text-center shadow-sm">
      <Icon className="h-4 w-4 text-[var(--accent-text)]" />
      <p className="text-sm font-semibold text-[var(--text-primary)]">{value}</p>
      <p className="text-[11px] text-[var(--text-muted)]">{label}</p>
    </div>
  );
}
