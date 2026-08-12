import { useEffect, useState } from 'react';
import { CalendarClock, X } from 'lucide-react';

export function ExerciseAddModal({
  exerciseName,
  durationMinutes,
  caloriesBurned,
  initialDate,
  initialTime,
  onConfirm,
  onCancel,
  isSaving,
}: {
  exerciseName: string;
  durationMinutes: number;
  caloriesBurned: number | null;
  initialDate: string;
  initialTime: string;
  onConfirm: (date: string, time: string) => Promise<void> | void;
  onCancel: () => void;
  isSaving: boolean;
}) {
  const [selectedDate, setSelectedDate] = useState(initialDate);
  const [selectedTime, setSelectedTime] = useState(initialTime);

  useEffect(() => {
    setSelectedDate(initialDate);
    setSelectedTime(initialTime);
  }, [initialDate, initialTime]);

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/50 px-2 pb-2 pt-10 backdrop-blur-sm sm:px-4">
      <div className="w-full max-w-md rounded-t-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-3 shadow-xl sm:rounded-2xl">
        <div className="mb-3 flex items-center justify-between gap-3">
          <div>
            <p className="text-[9px] uppercase tracking-[0.18em] text-[var(--text-muted)]">Confirmar</p>
            <h2 className="mt-1 text-base font-semibold text-[var(--text-primary)]">Añadir ejercicio</h2>
          </div>
          <button
            type="button"
            onClick={onCancel}
            className="flex h-7 w-7 items-center justify-center rounded-full border border-[var(--card-border)] text-[var(--text-secondary)]"
            aria-label="Cerrar"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>

        <div className="rounded-xl border border-[var(--card-border)] bg-[var(--app-bg)] p-2.5">
          <div className="flex items-center justify-between gap-3">
            <div className="min-w-0">
              <p className="truncate text-sm font-semibold text-[var(--text-primary)]">{exerciseName}</p>
              <p className="text-[11px] text-[var(--text-muted)]">
                {durationMinutes} min{caloriesBurned != null ? ` · ${Math.round(caloriesBurned)} kcal` : ''}
              </p>
            </div>
            <div className="rounded-full bg-[var(--accent-soft)] p-1.5 text-[var(--accent)]">
              <CalendarClock className="h-3.5 w-3.5" />
            </div>
          </div>
        </div>

        <div className="mt-3 space-y-2.5">
          <label className="block text-xs text-[var(--text-secondary)]">
            Día
            <input
              type="date"
              value={selectedDate}
              onChange={(e) => setSelectedDate(e.target.value)}
              className="mt-1 block w-full rounded-lg border border-[var(--input-border)] bg-[var(--app-bg)] px-2.5 py-2 text-sm outline-none focus:border-[var(--accent)]"
            />
          </label>

          <label className="block text-xs text-[var(--text-secondary)]">
            Hora
            <input
              type="time"
              value={selectedTime}
              onChange={(e) => setSelectedTime(e.target.value)}
              className="mt-1 block w-full rounded-lg border border-[var(--input-border)] bg-[var(--app-bg)] px-2.5 py-2 text-sm outline-none focus:border-[var(--accent)]"
            />
          </label>
        </div>

        <div className="mt-4 flex gap-2">
          <button
            type="button"
            onClick={onCancel}
            className="flex-1 rounded-full border border-[var(--card-border)] px-3 py-2 text-sm font-medium text-[var(--text-secondary)]"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => void onConfirm(selectedDate, selectedTime)}
            disabled={isSaving}
            className="flex-1 rounded-full bg-[var(--accent)] px-3 py-2 text-sm font-semibold text-white disabled:opacity-60"
          >
            {isSaving ? 'Guardando…' : 'Confirmar'}
          </button>
        </div>
      </div>
    </div>
  );
}
