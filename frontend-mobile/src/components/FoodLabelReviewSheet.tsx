import { useState } from 'react';
import { X, Loader2, Check } from 'lucide-react';
import { saveFoodLabelMeal, type FoodLabelExtractionResult, type MealType } from '../api/foodLabelApi';

const MEAL_TYPE_OPTIONS: Array<{ value: MealType; label: string }> = [
  { value: 'Breakfast', label: 'Desayuno' },
  { value: 'Lunch', label: 'Almuerzo' },
  { value: 'Dinner', label: 'Cena' },
  { value: 'Snack', label: 'Snack' },
];

/** Shows the AI-extracted nutrition label data for review, lets the user tweak the meal
 * type/name before confirming, and only then saves it (logs the meal + stores/reuses the
 * product in the global food database) - the image is never saved without this confirmation.
 * Shared between NutritionPage (camera icon on the Nutrición page) and ChatPanel (photo
 * attach button on the Agente chat page) - both start the same "scan a label" flow. */
export function FoodLabelReviewSheet({
  extraction,
  onClose,
  onSaved,
}: {
  extraction: FoodLabelExtractionResult;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [mealType, setMealType] = useState<MealType>('Snack');
  const [name, setName] = useState(extraction.name);
  const [quantity, setQuantity] = useState('1');
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const parsedQuantity = Number(quantity.replace(',', '.'));
  const effectiveQuantity = Number.isFinite(parsedQuantity) && parsedQuantity > 0 ? parsedQuantity : 1;

  // Blur the focused input first so mobile browsers collapse the on-screen keyboard
  // before the sheet unmounts - otherwise the viewport is left shrunk, showing a blank gap.
  const dismiss = (fn: () => void) => {
    (document.activeElement as HTMLElement | null)?.blur();
    fn();
  };

  const handleConfirm = async () => {
    setIsSaving(true);
    setSaveError(null);
    try {
      const finalName = (name && name.trim()) || (extraction.name && extraction.name.trim()) || 'Unnamed Food';
      await saveFoodLabelMeal({
        mealType,
        name: finalName,
        brand: extraction.brand,
        servingSize: extraction.servingSize,
        calories: extraction.calories,
        proteinGrams: extraction.proteinGrams,
        carbsGrams: extraction.carbsGrams,
        fatGrams: extraction.fatGrams,
        saturatedFatGrams: extraction.saturatedFatGrams,
        sugarGrams: extraction.sugarGrams,
        fiberGrams: extraction.fiberGrams,
        sodiumMilligrams: extraction.sodiumMilligrams,
        potassiumMilligrams: extraction.potassiumMilligrams,
        calciumMilligrams: extraction.calciumMilligrams,
        ironMilligrams: extraction.ironMilligrams,
        magnesiumMilligrams: extraction.magnesiumMilligrams,
        vitaminAMicrograms: extraction.vitaminAMicrograms,
        ingredientsText: extraction.ingredientsText,
        consumedAtIso: null,
        quantity: effectiveQuantity,
      });
      dismiss(onSaved);
    } catch {
      setSaveError('No se pudo guardar este alimento. Intenta de nuevo.');
    } finally {
      setIsSaving(false);
    }
  };

  const nutrientRows: Array<{ label: string; value: number | null; unit: string }> = [
    { label: 'Calorías', value: extraction.calories, unit: 'kcal' },
    { label: 'Proteína', value: extraction.proteinGrams, unit: 'g' },
    { label: 'Carbohidratos', value: extraction.carbsGrams, unit: 'g' },
    { label: 'Grasa total', value: extraction.fatGrams, unit: 'g' },
    { label: 'Grasa saturada', value: extraction.saturatedFatGrams, unit: 'g' },
    { label: 'Azúcares', value: extraction.sugarGrams, unit: 'g' },
    { label: 'Fibra', value: extraction.fiberGrams, unit: 'g' },
    { label: 'Sodio', value: extraction.sodiumMilligrams, unit: 'mg' },
    { label: 'Potasio', value: extraction.potassiumMilligrams, unit: 'mg' },
    { label: 'Calcio', value: extraction.calciumMilligrams, unit: 'mg' },
    { label: 'Hierro', value: extraction.ironMilligrams, unit: 'mg' },
    { label: 'Magnesio', value: extraction.magnesiumMilligrams, unit: 'mg' },
    { label: 'Vitamina A', value: extraction.vitaminAMicrograms, unit: 'µg' },
  ]
    .filter((n) => n.value != null)
    .map((n) => ({ ...n, value: Math.round((n.value as number) * effectiveQuantity * 10) / 10 }));

  return (
    <div className="fixed inset-0 z-50 flex items-end bg-black/40" onClick={() => dismiss(onClose)}>
      <div
        className="max-h-[85vh] w-full overflow-y-auto rounded-t-2xl bg-[var(--card-bg)] p-4 pb-[max(1rem,env(safe-area-inset-bottom))] shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-3 flex items-start justify-between">
          <div>
            <h3 className="text-sm font-semibold text-[var(--text-primary)]">Confirmar alimento escaneado</h3>
            {extraction.brand && <p className="text-xs text-[var(--text-muted)]">{extraction.brand}</p>}
          </div>
          <button type="button" onClick={() => dismiss(onClose)} aria-label="Cerrar" className="rounded-full p-1.5 text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]">
            <X className="h-4 w-4" />
          </button>
        </div>

        <label className="mb-1 block text-xs font-medium text-[var(--text-secondary)]">Nombre</label>
        <input
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          className="mb-3 w-full rounded-lg border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2 text-sm text-[var(--text-primary)]"
        />

        {extraction.servingSize && (
          <p className="mb-3 text-xs text-[var(--text-muted)]">Porción de la etiqueta: {extraction.servingSize}</p>
        )}

        <label className="mb-1 block text-xs font-medium text-[var(--text-secondary)]">¿Cuántas porciones estás consumiendo?</label>
        <input
          type="number"
          inputMode="decimal"
          min="0.1"
          step="0.5"
          value={quantity}
          onChange={(e) => setQuantity(e.target.value)}
          className="mb-3 w-full rounded-lg border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2 text-sm text-[var(--text-primary)]"
        />

        <label className="mb-1 block text-xs font-medium text-[var(--text-secondary)]">Tipo de comida</label>
        <div className="mb-3 grid grid-cols-4 gap-1.5">
          {MEAL_TYPE_OPTIONS.map((opt) => (
            <button
              key={opt.value}
              type="button"
              onClick={() => setMealType(opt.value)}
              className={`rounded-lg border px-2 py-1.5 text-xs font-medium ${
                mealType === opt.value
                  ? 'border-[var(--accent)] bg-[var(--accent-soft)] text-[var(--accent-text)]'
                  : 'border-[var(--card-border)] text-[var(--text-secondary)]'
              }`}
            >
              {opt.label}
            </button>
          ))}
        </div>

        <div className="space-y-1 rounded-xl border border-[var(--card-border)] bg-[var(--app-bg)] p-3">
          {nutrientRows.map((n) => (
            <div key={n.label} className="flex items-center justify-between text-sm">
              <span className="text-[var(--text-muted)]">{n.label}</span>
              <span className="font-medium text-[var(--text-primary)]">
                {n.value} {n.unit}
              </span>
            </div>
          ))}
        </div>

        {extraction.ingredientsText && (
          <p className="mt-3 whitespace-pre-wrap rounded-lg bg-[var(--app-bg)] p-2.5 text-xs text-[var(--text-muted)]">
            Ingredientes: {extraction.ingredientsText}
          </p>
        )}

        {saveError && <p className="mt-3 text-sm text-red-600">{saveError}</p>}

        <div className="mt-4 flex gap-2">
          <button
            type="button"
            onClick={() => dismiss(onClose)}
            className="flex-1 rounded-xl border border-[var(--card-border)] py-2.5 text-sm font-medium text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => void handleConfirm()}
            disabled={isSaving}
            className="flex flex-1 items-center justify-center gap-2 rounded-xl bg-[var(--accent)] py-2.5 text-sm font-medium text-white disabled:opacity-50"
          >
            {isSaving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
            {isSaving ? 'Guardando…' : 'Guardar y sumar a mi consumo'}
          </button>
        </div>
      </div>
    </div>
  );
}
