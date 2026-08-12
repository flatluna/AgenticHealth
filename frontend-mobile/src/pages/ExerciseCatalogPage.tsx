import { useEffect, useState, type FormEvent } from 'react';
import { AlertCircle, Check, CheckCircle2, Loader2, Plus, Sparkles, X } from 'lucide-react';
import {
  estimateExercise,
  getGlobalExerciseCatalog,
  getPersonalExerciseCatalog,
  logExercise,
  saveCustomExercise,
  saveGlobalExercise,
  type ExerciseEstimate,
  type GlobalExercise,
  type PersonalExercise,
} from '../api/exerciseApi';
import { ExerciseAddModal } from '../components/ExerciseAddModal';

type SaveLocation = 'local' | 'global' | 'both';

export function ExerciseCatalogPage() {
  const [personalItems, setPersonalItems] = useState<PersonalExercise[]>([]);
  const [globalItems, setGlobalItems] = useState<GlobalExercise[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [name, setName] = useState('');
  const [duration, setDuration] = useState('30');
  const [calories, setCalories] = useState('');
  const [category, setCategory] = useState('');
  const [description, setDescription] = useState('');
  const [saveLocation, setSaveLocation] = useState<SaveLocation>('both');
  const [isSaving, setIsSaving] = useState(false);
  const [search, setSearch] = useState('');
  const [pendingAdd, setPendingAdd] = useState<{
    name: string;
    durationMinutes: number;
    caloriesBurned: number | null;
  } | null>(null);
  const [isAdding, setIsAdding] = useState(false);

  const [isEstimating, setIsEstimating] = useState(false);
  const [estimate, setEstimate] = useState<ExerciseEstimate | null>(null);
  const [estimateError, setEstimateError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const query = search.trim().toLowerCase();
  const filteredPersonalItems = personalItems.filter((item) =>
    item.name.toLowerCase().includes(query),
  );
  const filteredGlobalItems = globalItems.filter((item) =>
    item.name.toLowerCase().includes(query),
  );

  const reload = async () => {
    try {
      const [personal, global] = await Promise.all([
        getPersonalExerciseCatalog(),
        getGlobalExerciseCatalog(),
      ]);
      setPersonalItems(personal);
      setGlobalItems(global);
      setError(null);
    } catch {
      setError('No se pudo cargar el catálogo de ejercicios.');
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    void reload();
  }, []);

  const handleEstimate = async (e: FormEvent) => {
    e.preventDefault();
    const durationValue = Number(duration);
    if (!name.trim() && !description.trim()) {
      setEstimateError('Escribe un nombre o describe el ejercicio.');
      return;
    }
    if (!duration.trim() || Number.isNaN(durationValue) || durationValue <= 0) {
      setEstimateError('Ingresa una duración válida en minutos.');
      return;
    }

    const prompt = name.trim() || description.trim();
    setIsEstimating(true);
    setEstimateError(null);
    try {
      const result = await estimateExercise(prompt, durationValue);
      setEstimate(result);
      if (!name.trim()) setName(result.suggestedName);
      if (!calories.trim()) setCalories(String(Math.round(result.estimatedCaloriesBurned)));
    } catch {
      setEstimateError('No se pudo calcular la estimación con IA.');
    } finally {
      setIsEstimating(false);
    }
  };

  const handleSave = async () => {
    const trimmedName = name.trim();
    if (!trimmedName) {
      setSaveError('Escribe el nombre del ejercicio.');
      return;
    }

    const durationValue = Number(duration) || 30;
    const caloriesValue = calories.trim() ? Number(calories) : null;
    setIsSaving(true);
    setSaveError(null);
    try {
      if (saveLocation === 'local' || saveLocation === 'both') {
        await saveCustomExercise(trimmedName, durationValue, caloriesValue);
      }
      if (saveLocation === 'global' || saveLocation === 'both') {
        await saveGlobalExercise(
          trimmedName,
          durationValue,
          caloriesValue,
          category.trim() || null,
          description.trim() || null,
        );
      }
      setName('');
      setDuration('30');
      setCalories('');
      setCategory('');
      setDescription('');
      setEstimate(null);
      await reload();
    } catch {
      setSaveError('No se pudo guardar el ejercicio en la ubicación seleccionada.');
    } finally {
      setIsSaving(false);
    }
  };

  const handleQuickAdd = (item: { name: string; durationMinutes: number; caloriesBurned: number | null }) => {
    setPendingAdd(item);
  };

  const confirmQuickAdd = async (date: string, time: string) => {
    if (!pendingAdd) return;
    setIsAdding(true);
    setSaveError(null);
    try {
      const dateObject = new Date(`${date}T${time || '12:00'}:00`);
      if (Number.isNaN(dateObject.getTime())) {
        throw new Error('Fecha inválida');
      }
      await logExercise(pendingAdd.name, pendingAdd.durationMinutes, pendingAdd.caloriesBurned, dateObject.toISOString());
      setPendingAdd(null);
    } catch {
      setSaveError('No se pudo agregar el ejercicio a la fecha y hora seleccionadas.');
    } finally {
      setIsAdding(false);
    }
  };

  const modalInitialDate = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Chicago' }).format(new Date());

  return (
    <div className="h-full overflow-y-auto bg-[var(--app-bg)] px-3 py-4 sm:px-4">
      <div className="mx-auto max-w-4xl space-y-4">
        <div>
          <h1 className="text-sm sm:text-base font-semibold text-[var(--text-primary)]">Catálogo de ejercicios</h1>
          <p className="mt-1 text-xs text-[var(--text-muted)]">
            Crea nuevos ejercicios con IA o manualmente y guárdalos en tu catálogo local, global o en ambos.
          </p>
        </div>

        <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
          <form onSubmit={handleEstimate} className="space-y-3">
            <label className="block text-sm text-[var(--text-secondary)]">
              Nombre del ejercicio
              <input
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Ej. Sentadilla goblet"
                className="mt-1 block w-full rounded-lg border border-[var(--input-border)] bg-[var(--card-bg)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
              />
            </label>

            <div className="grid gap-3 sm:grid-cols-2">
              <label className="block text-sm text-[var(--text-secondary)]">
                Duración (min)
                <input
                  type="number"
                  min={1}
                  value={duration}
                  onChange={(e) => setDuration(e.target.value)}
                  className="mt-1 block w-full rounded-lg border border-[var(--input-border)] bg-[var(--card-bg)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
                />
              </label>
              <label className="block text-sm text-[var(--text-secondary)]">
                Calorías estimadas (opcional)
                <input
                  type="number"
                  min={0}
                  value={calories}
                  onChange={(e) => setCalories(e.target.value)}
                  className="mt-1 block w-full rounded-lg border border-[var(--input-border)] bg-[var(--card-bg)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
                />
              </label>
            </div>

            <label className="block text-sm text-[var(--text-secondary)]">
              Categoría (opcional)
              <input
                type="text"
                value={category}
                onChange={(e) => setCategory(e.target.value)}
                placeholder="Resistencia, cardio, fuerza..."
                className="mt-1 block w-full rounded-lg border border-[var(--input-border)] bg-[var(--card-bg)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
              />
            </label>

            <label className="block text-sm text-[var(--text-secondary)]">
              Descripción (opcional)
              <textarea
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                rows={2}
                placeholder="Ej. 3 series, 12 repeticiones, descanso 60s"
                className="mt-1 block w-full rounded-lg border border-[var(--input-border)] bg-[var(--card-bg)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
              />
            </label>

            {estimateError && (
              <div className="flex items-center gap-1.5 text-sm text-red-600">
                <AlertCircle className="h-4 w-4 shrink-0" /> {estimateError}
              </div>
            )}

            <div className="flex gap-2">
              <button
                type="submit"
                disabled={isEstimating}
                className="flex flex-1 items-center justify-center gap-2 rounded-full bg-[var(--accent)] px-3 py-2.5 text-sm font-semibold text-white disabled:opacity-60"
              >
                {isEstimating ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
                Calcular con IA
              </button>
              <button
                type="button"
                onClick={() => {
                  setName('');
                  setDuration('30');
                  setCalories('');
                  setCategory('');
                  setDescription('');
                  setEstimate(null);
                  setEstimateError(null);
                  setSaveError(null);
                }}
                className="rounded-full border border-[var(--card-border)] px-3 py-2 text-sm font-medium text-[var(--text-secondary)]"
              >
                Limpiar
              </button>
            </div>
          </form>

          {estimate && (
            <div className="mt-4 rounded-xl border border-[var(--card-border)] bg-[var(--app-bg)] p-3">
              <p className="text-xs font-medium text-[var(--text-muted)]">Vista previa de IA</p>
              <div className="mt-2 flex items-center justify-between gap-3">
                <div>
                  <p className="text-sm font-semibold text-[var(--text-primary)]">{name.trim() || estimate.suggestedName}</p>
                  <p className="text-xs text-[var(--text-muted)]">{duration} min · {Math.round(estimate.estimatedCaloriesBurned)} kcal</p>
                </div>
                <CheckCircle2 className="h-5 w-5 text-green-600" />
              </div>
            </div>
          )}

          <div className="mt-4 rounded-xl border border-[var(--card-border)] bg-[var(--app-bg)] p-3">
            <p className="text-xs font-medium text-[var(--text-muted)]">Guardar en</p>
            <div className="mt-2 grid gap-2 sm:grid-cols-3">
              {(['local', 'global', 'both'] as SaveLocation[]).map((option) => (
                <button
                  key={option}
                  type="button"
                  onClick={() => setSaveLocation(option)}
                  className={`rounded-lg border px-2 py-2 text-xs font-medium ${
                    saveLocation === option
                      ? 'border-[var(--accent)] bg-[var(--accent-soft)] text-[var(--accent-text)]'
                      : 'border-[var(--card-border)] text-[var(--text-secondary)]'
                  }`}
                >
                  {option === 'local' ? 'Local' : option === 'global' ? 'Global' : 'Local + global'}
                </button>
              ))}
            </div>

            {saveError && (
              <div className="mt-3 flex items-center gap-1.5 text-sm text-red-600">
                <AlertCircle className="h-4 w-4 shrink-0" /> {saveError}
              </div>
            )}

            <button
              type="button"
              onClick={() => void handleSave()}
              disabled={isSaving}
              className="mt-3 flex w-full items-center justify-center gap-2 rounded-full bg-[var(--accent)] px-3 py-2.5 text-sm font-semibold text-white disabled:opacity-60"
            >
              {isSaving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
              Guardar ejercicio
            </button>
          </div>
        </div>

        {error && (
          <div className="flex items-center gap-1.5 text-sm text-red-600">
            <AlertCircle className="h-4 w-4 shrink-0" /> {error}
          </div>
        )}

        {isLoading ? (
          <div className="flex items-center gap-2 text-sm text-[var(--text-muted)]">
            <Loader2 className="h-4 w-4 animate-spin" /> Cargando catálogo…
          </div>
        ) : (
          <>
            <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-3 shadow-sm">
              <input
                type="text"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Buscar ejercicios..."
                className="w-full rounded-lg border border-[var(--input-border)] bg-[var(--app-bg)] px-3 py-2 text-sm text-[var(--text-primary)] outline-none placeholder:text-[var(--text-muted)] focus:border-[var(--accent)]"
              />
            </div>

            <div className="grid gap-4 lg:grid-cols-2">
              <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
                <h2 className="text-sm font-semibold text-[var(--text-secondary)]">Mi catálogo</h2>
                <div className="mt-3 space-y-2">
                  {filteredPersonalItems.length === 0 ? (
                    <p className="text-sm text-[var(--text-muted)]">
                      {query ? 'No hay ejercicios locales que coincidan.' : 'Todavía no tienes ejercicios locales guardados.'}
                    </p>
                  ) : (
                    filteredPersonalItems.map((item) => (
                      <div key={item.id} className="rounded-lg border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2">
                        <div className="flex items-center justify-between gap-2">
                          <div>
                            <p className="text-sm font-medium text-[var(--text-primary)]">{item.name}</p>
                            <p className="text-xs text-[var(--text-muted)]">{item.durationMinutes} min{item.caloriesBurned != null ? ` · ${Math.round(item.caloriesBurned)} kcal` : ''}</p>
                          </div>
                          <button
                            type="button"
                            onClick={() => handleQuickAdd({ name: item.name, durationMinutes: item.durationMinutes, caloriesBurned: item.caloriesBurned })}
                            className="rounded-full border border-[var(--card-border)] bg-[var(--accent-soft)] px-2 py-1 text-[10px] font-medium text-[var(--accent-text)]"
                          >
                            Usar
                          </button>
                        </div>
                      </div>
                    ))
                  )}
                </div>
              </div>

              <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm">
                <h2 className="text-sm font-semibold text-[var(--text-secondary)]">Global</h2>
                <div className="mt-3 space-y-2">
                  {filteredGlobalItems.length === 0 ? (
                    <p className="text-sm text-[var(--text-muted)]">
                      {query ? 'No hay ejercicios globales que coincidan.' : 'Aún no hay ejercicios globales.'}
                    </p>
                  ) : (
                    filteredGlobalItems.map((item) => (
                      <div key={item.id} className="rounded-lg border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2">
                        <div className="flex items-center justify-between gap-2">
                          <div>
                            <p className="text-sm font-medium text-[var(--text-primary)]">{item.name}</p>
                            <p className="text-xs text-[var(--text-muted)]">{item.defaultDurationMinutes} min{item.defaultCaloriesBurned != null ? ` · ${Math.round(item.defaultCaloriesBurned)} kcal` : ''}</p>
                          </div>
                          <button
                            type="button"
                            onClick={() => handleQuickAdd({ name: item.name, durationMinutes: item.defaultDurationMinutes, caloriesBurned: item.defaultCaloriesBurned })}
                            className="rounded-full border border-[var(--card-border)] bg-[var(--accent-soft)] px-2 py-1 text-[10px] font-medium text-[var(--accent-text)]"
                          >
                            Usar
                          </button>
                        </div>
                      </div>
                    ))
                  )}
                </div>
              </div>
            </div>
          </>
        )}

        {estimate && (
          <div className="flex items-center justify-end gap-2">
            <button type="button" onClick={() => setEstimate(null)} className="flex items-center gap-1 rounded-full border border-[var(--card-border)] px-3 py-2 text-sm font-medium text-[var(--text-secondary)]">
              <X className="h-4 w-4" /> Descartar
            </button>
            <button type="button" onClick={() => void handleSave()} className="flex items-center gap-1 rounded-full bg-[var(--accent)] px-3 py-2 text-sm font-semibold text-white">
              <Check className="h-4 w-4" /> Guardar
            </button>
          </div>
        )}
      </div>

      {pendingAdd && (
        <ExerciseAddModal
          exerciseName={pendingAdd.name}
          durationMinutes={pendingAdd.durationMinutes}
          caloriesBurned={pendingAdd.caloriesBurned}
          initialDate={modalInitialDate}
          initialTime="12:00"
          isSaving={isAdding}
          onConfirm={confirmQuickAdd}
          onCancel={() => setPendingAdd(null)}
        />
      )}
    </div>
  );
}
