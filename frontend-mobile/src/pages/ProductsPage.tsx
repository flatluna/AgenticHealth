import { useEffect, useState } from 'react';
import { Search, Package, X, Loader2, Check, Eye, Plus, BookMarked } from 'lucide-react';
import { getFoodItems, logFoodItem, getPersonalFoodItems, logPersonalFoodItem, type FoodItem, type MealType, type PersonalFoodItem } from '../api/foodLabelApi';

const MEAL_TYPE_OPTIONS: Array<{ value: MealType; label: string }> = [
  { value: 'Breakfast', label: 'Desayuno' },
  { value: 'Lunch', label: 'Almuerzo' },
  { value: 'Dinner', label: 'Cena' },
  { value: 'Snack', label: 'Snack' },
];

const NUTRIENT_ROWS: Array<{ key: keyof FoodItem; label: string; unit: string }> = [
  { key: 'calories', label: 'Calorías', unit: 'kcal' },
  { key: 'proteinGrams', label: 'Proteína', unit: 'g' },
  { key: 'carbsGrams', label: 'Carbohidratos', unit: 'g' },
  { key: 'fatGrams', label: 'Grasa total', unit: 'g' },
  { key: 'saturatedFatGrams', label: 'Grasa saturada', unit: 'g' },
  { key: 'sugarGrams', label: 'Azúcares', unit: 'g' },
  { key: 'fiberGrams', label: 'Fibra', unit: 'g' },
  { key: 'sodiumMilligrams', label: 'Sodio', unit: 'mg' },
  { key: 'potassiumMilligrams', label: 'Potasio', unit: 'mg' },
  { key: 'calciumMilligrams', label: 'Calcio', unit: 'mg' },
  { key: 'ironMilligrams', label: 'Hierro', unit: 'mg' },
  { key: 'magnesiumMilligrams', label: 'Magnesio', unit: 'mg' },
  { key: 'vitaminAMicrograms', label: 'Vitamina A', unit: 'µg' },
];

/** Read-only bottom sheet showing every nutrient stored for a product (opened via the "Ver"
 * button), with a shortcut to jump straight into the add-to-nutrition flow. */
function ProductDetailSheet({
  product,
  onClose,
  onRequestAdd,
}: {
  product: FoodItem;
  onClose: () => void;
  onRequestAdd: () => void;
}) {
  const rows = NUTRIENT_ROWS.filter((row) => product[row.key] != null);

  useEffect(() => {
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = 'unset';
    };
  }, []);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4" onClick={onClose}>
      <div
        className="max-h-[90vh] w-full max-w-md overflow-y-auto rounded-2xl bg-[var(--card-bg)] p-4 pb-[max(1rem,env(safe-area-inset-bottom))] shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-3 flex items-start justify-between">
          <div>
            <h3 className="text-sm font-semibold text-[var(--text-primary)]">{product.name}</h3>
            {product.brand && <p className="text-xs text-[var(--text-muted)]">{product.brand}</p>}
          </div>
          <button type="button" onClick={onClose} aria-label="Cerrar" className="rounded-full p-1.5 text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]">
            <X className="h-4 w-4" />
          </button>
        </div>

        {product.servingSize && <p className="mb-3 text-xs text-[var(--text-muted)]">Porción: {product.servingSize}</p>}

        <div className="mb-3 divide-y divide-[var(--card-border)] rounded-lg border border-[var(--card-border)]">
          {rows.map((row) => (
            <div key={row.label} className="flex items-center justify-between px-3 py-2 text-sm">
              <span className="text-[var(--text-secondary)]">{row.label}</span>
              <span className="font-medium text-[var(--text-primary)]">
                {Math.round((product[row.key] as number) * 10) / 10} {row.unit}
              </span>
            </div>
          ))}
        </div>

        {product.ingredientsText && (
          <div className="mb-3">
            <p className="mb-1 text-xs font-medium text-[var(--text-secondary)]">Ingredientes</p>
            <p className="text-xs text-[var(--text-muted)]">{product.ingredientsText}</p>
          </div>
        )}

        <p className="mb-3 text-[11px] text-[var(--text-muted)]">Registrado {product.timesLogged} {product.timesLogged === 1 ? 'vez' : 'veces'}.</p>

        <button
          type="button"
          onClick={onRequestAdd}
          className="flex w-full items-center justify-center gap-1.5 rounded-lg bg-[var(--accent)] py-2 text-sm font-semibold text-white"
        >
          <Plus className="h-4 w-4" />
          Adicionar a Nutrición
        </button>
      </div>
    </div>
  );
}

/** Bottom sheet to pick a meal type and confirm logging an already-known global product,
 * without re-scanning its label since the nutrition data is already stored. */
function LogProductSheet({ product, onClose, onLogged }: { product: FoodItem; onClose: () => void; onLogged: () => void }) {
  const [mealType, setMealType] = useState<MealType>('Snack');
  const [quantity, setQuantity] = useState('1');
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = 'unset';
    };
  }, []);

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
    setError(null);
    try {
      await logFoodItem(product.id, { mealType, consumedAtIso: null, quantity: effectiveQuantity });
      dismiss(onLogged);
    } catch {
      setError('No se pudo registrar este producto. Intenta de nuevo.');
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4" onClick={() => dismiss(onClose)}>
      <div
        className="max-h-[90vh] w-full max-w-md overflow-y-auto rounded-2xl bg-[var(--card-bg)] p-4 pb-[max(1rem,env(safe-area-inset-bottom))] shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-3 flex items-start justify-between">
          <div>
            <h3 className="text-sm font-semibold text-[var(--text-primary)]">{product.name}</h3>
            {product.brand && <p className="text-xs text-[var(--text-muted)]">{product.brand}</p>}
          </div>
          <button type="button" onClick={() => dismiss(onClose)} aria-label="Cerrar" className="rounded-full p-1.5 text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]">
            <X className="h-4 w-4" />
          </button>
        </div>

        {product.servingSize && <p className="mb-3 text-xs text-[var(--text-muted)]">Porción del producto: {product.servingSize}</p>}

        <label className="mb-1 block text-xs font-medium text-[var(--text-secondary)]">¿Cuántas porciones estás consumiendo?</label>
        <input
          type="number"
          inputMode="decimal"
          min="0.1"
          step="0.5"
          value={quantity}
          onChange={(e) => setQuantity(e.target.value)}
          className="mb-4 w-full rounded-lg border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2 text-sm text-[var(--text-primary)] outline-none"
        />

        <label className="mb-1 block text-xs font-medium text-[var(--text-secondary)]">Tipo de comida</label>
        <div className="mb-4 grid grid-cols-4 gap-1.5">
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

        {error && <p className="mb-3 text-xs text-red-600">{error}</p>}

        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => dismiss(onClose)}
            className="flex-1 rounded-lg border border-[var(--card-border)] py-2 text-sm font-medium text-[var(--text-secondary)]"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => void handleConfirm()}
            disabled={isSaving}
            className="flex flex-1 items-center justify-center gap-1.5 rounded-lg bg-[var(--accent)] py-2 text-sm font-semibold text-white disabled:opacity-60"
          >
            {isSaving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
            Registrar
          </button>
        </div>
      </div>
    </div>
  );
}

const PERSONAL_NUTRIENT_ROWS: Array<{ key: keyof PersonalFoodItem; label: string; unit: string }> = [
  { key: 'calories', label: 'Calorías', unit: 'kcal' },
  { key: 'proteinGrams', label: 'Proteína', unit: 'g' },
  { key: 'carbsGrams', label: 'Carbohidratos', unit: 'g' },
  { key: 'fatGrams', label: 'Grasa total', unit: 'g' },
  { key: 'saturatedFatGrams', label: 'Grasa saturada', unit: 'g' },
  { key: 'sugarGrams', label: 'Azúcares', unit: 'g' },
  { key: 'fiberGrams', label: 'Fibra', unit: 'g' },
  { key: 'sodiumMilligrams', label: 'Sodio', unit: 'mg' },
  { key: 'potassiumMilligrams', label: 'Potasio', unit: 'mg' },
  { key: 'calciumMilligrams', label: 'Calcio', unit: 'mg' },
  { key: 'ironMilligrams', label: 'Hierro', unit: 'mg' },
  { key: 'magnesiumMilligrams', label: 'Magnesio', unit: 'mg' },
  { key: 'vitaminAMicrograms', label: 'Vitamina A', unit: 'µg' },
];

/** Read-only nutrient view (name, LLM-generated description, full nutrients) for one personal
 * catalog entry, with a shortcut to log it as consumed - same pattern as ProductDetailSheet. */
function PersonalFoodDetailSheet({
  item,
  onClose,
  onRequestAdd,
}: {
  item: PersonalFoodItem;
  onClose: () => void;
  onRequestAdd: () => void;
}) {
  const rows = PERSONAL_NUTRIENT_ROWS.filter((row) => item[row.key] != null);

  useEffect(() => {
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = 'unset';
    };
  }, []);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4" onClick={onClose}>
      <div
        className="max-h-[90vh] w-full max-w-md overflow-y-auto rounded-2xl bg-[var(--card-bg)] p-4 pb-[max(1rem,env(safe-area-inset-bottom))] shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-3 flex items-start justify-between">
          <div>
            <h3 className="text-sm font-semibold text-[var(--text-primary)]">{item.name}</h3>
            {item.description && <p className="text-xs text-[var(--text-muted)]">{item.description}</p>}
          </div>
          <button type="button" onClick={onClose} aria-label="Cerrar" className="rounded-full p-1.5 text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]">
            <X className="h-4 w-4" />
          </button>
        </div>

        {item.servingSize && <p className="mb-3 text-xs text-[var(--text-muted)]">Porción: {item.servingSize}</p>}

        <div className="mb-3 divide-y divide-[var(--card-border)] rounded-lg border border-[var(--card-border)]">
          {rows.map((row) => (
            <div key={row.label} className="flex items-center justify-between px-3 py-2 text-sm">
              <span className="text-[var(--text-secondary)]">{row.label}</span>
              <span className="font-medium text-[var(--text-primary)]">
                {Math.round((item[row.key] as number) * 10) / 10} {row.unit}
              </span>
            </div>
          ))}
        </div>

        <p className="mb-3 text-[11px] text-[var(--text-muted)]">Guardado {item.timesLogged} {item.timesLogged === 1 ? 'vez' : 'veces'}.</p>

        <button
          type="button"
          onClick={onRequestAdd}
          className="flex w-full items-center justify-center gap-1.5 rounded-lg bg-[var(--accent)] py-2 text-sm font-semibold text-white"
        >
          <Plus className="h-4 w-4" />
          Adicionar a Nutrición
        </button>
      </div>
    </div>
  );
}

/** Bottom sheet to pick a meal type/quantity and confirm logging an existing personal catalog
 * entry - same pattern as LogProductSheet, but posts to the personal-catalog log endpoint. */
function LogPersonalFoodSheet({ item, onClose, onLogged }: { item: PersonalFoodItem; onClose: () => void; onLogged: () => void }) {
  const [mealType, setMealType] = useState<MealType>('Snack');
  const [quantity, setQuantity] = useState('1');
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = 'unset';
    };
  }, []);

  const parsedQuantity = Number(quantity.replace(',', '.'));
  const effectiveQuantity = Number.isFinite(parsedQuantity) && parsedQuantity > 0 ? parsedQuantity : 1;

  const dismiss = (fn: () => void) => {
    (document.activeElement as HTMLElement | null)?.blur();
    fn();
  };

  const handleConfirm = async () => {
    setIsSaving(true);
    setError(null);
    try {
      await logPersonalFoodItem(item.id, { mealType, consumedAtIso: null, quantity: effectiveQuantity });
      dismiss(onLogged);
    } catch {
      setError('No se pudo registrar este alimento. Intenta de nuevo.');
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4" onClick={() => dismiss(onClose)}>
      <div
        className="max-h-[90vh] w-full max-w-md overflow-y-auto rounded-2xl bg-[var(--card-bg)] p-4 pb-[max(1rem,env(safe-area-inset-bottom))] shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-3 flex items-start justify-between">
          <div>
            <h3 className="text-sm font-semibold text-[var(--text-primary)]">{item.name}</h3>
            {item.description && <p className="text-xs text-[var(--text-muted)]">{item.description}</p>}
          </div>
          <button type="button" onClick={() => dismiss(onClose)} aria-label="Cerrar" className="rounded-full p-1.5 text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]">
            <X className="h-4 w-4" />
          </button>
        </div>

        {item.servingSize && <p className="mb-3 text-xs text-[var(--text-muted)]">Porción guardada: {item.servingSize}</p>}

        <label className="mb-1 block text-xs font-medium text-[var(--text-secondary)]">¿Cuántas porciones estás consumiendo?</label>
        <input
          type="number"
          inputMode="decimal"
          min="0.1"
          step="0.5"
          value={quantity}
          onChange={(e) => setQuantity(e.target.value)}
          className="mb-4 w-full rounded-lg border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2 text-sm text-[var(--text-primary)] outline-none"
        />

        <label className="mb-1 block text-xs font-medium text-[var(--text-secondary)]">Tipo de comida</label>
        <div className="mb-4 grid grid-cols-4 gap-1.5">
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

        {error && <p className="mb-3 text-xs text-red-600">{error}</p>}

        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => dismiss(onClose)}
            className="flex-1 rounded-lg border border-[var(--card-border)] py-2 text-sm font-medium text-[var(--text-secondary)]"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => void handleConfirm()}
            disabled={isSaving}
            className="flex flex-1 items-center justify-center gap-1.5 rounded-lg bg-[var(--accent)] py-2 text-sm font-semibold text-white disabled:opacity-60"
          >
            {isSaving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
            Registrar
          </button>
        </div>
      </div>
    </div>
  );
}

function PersonalFoodRow({ item, onView, onAdd }: { item: PersonalFoodItem; onView: (p: PersonalFoodItem) => void; onAdd: (p: PersonalFoodItem) => void }) {
  return (
    <div className="flex w-full items-center justify-between gap-2 sm:gap-3 rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] p-2 sm:p-3">
      <div className="flex min-w-0 items-center gap-2 sm:gap-3">
        <span className="flex h-8 sm:h-9 w-8 sm:w-9 shrink-0 items-center justify-center rounded-lg bg-[var(--accent-soft)] text-[var(--accent-text)]">
          <BookMarked className="h-3.5 sm:h-4 w-3.5 sm:w-4" />
        </span>
        <div className="min-w-0">
          <p className="truncate text-xs sm:text-sm font-medium text-[var(--text-primary)]">{item.name}</p>
          <p className="truncate text-[10px] sm:text-xs text-[var(--text-muted)]">
            {item.calories != null ? `${Math.round(item.calories)} kcal` : 'Sin calorías'}
            {item.servingSize ? ` · ${item.servingSize}` : ''}
            {item.timesLogged > 0 ? ` · ${item.timesLogged}x` : ''}
          </p>
        </div>
      </div>
      <div className="flex shrink-0 items-center gap-1 sm:gap-1.5">
        <button
          type="button"
          onClick={() => onView(item)}
          aria-label="Ver detalle"
          className="flex items-center gap-0.5 sm:gap-1 rounded-lg border border-[var(--card-border)] px-2 sm:px-2.5 py-1.5 text-[11px] sm:text-xs font-medium text-[var(--text-secondary)] hover:bg-[var(--hover-bg)] whitespace-nowrap"
        >
          <Eye className="h-3.5 w-3.5 sm:h-3.5 sm:w-3.5" />
          <span className="hidden sm:inline">Ver</span>
        </button>
        <button
          type="button"
          onClick={() => onAdd(item)}
          aria-label="Adicionar a nutrición"
          className="flex items-center gap-0.5 sm:gap-1 rounded-lg bg-[var(--accent)] px-2 sm:px-2.5 py-1.5 text-[11px] sm:text-xs font-semibold text-white whitespace-nowrap"
        >
          <Plus className="h-3.5 w-3.5 sm:h-3.5 sm:w-3.5" />
          <span className="hidden sm:inline">Adicionar</span>
        </button>
      </div>
    </div>
  );
}

function ProductRow({
  product,
  onView,
  onAdd,
}: {
  product: FoodItem;
  onView: (p: FoodItem) => void;
  onAdd: (p: FoodItem) => void;
}) {
  return (
    <div className="flex w-full items-center justify-between gap-2 sm:gap-3 rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] p-2 sm:p-3">
      <div className="flex min-w-0 items-center gap-2 sm:gap-3">
        <span className="flex h-8 sm:h-9 w-8 sm:w-9 shrink-0 items-center justify-center rounded-lg bg-[var(--accent-soft)] text-[var(--accent-text)]">
          <Package className="h-3.5 sm:h-4 w-3.5 sm:w-4" />
        </span>
        <div className="min-w-0">
          <p className="truncate text-xs sm:text-sm font-medium text-[var(--text-primary)]">{product.name}</p>
          <p className="truncate text-[10px] sm:text-xs text-[var(--text-muted)]">
            {product.brand ? `${product.brand} · ` : ''}
            {product.calories != null ? `${Math.round(product.calories)} kcal` : 'Sin calorías'}
            {product.servingSize ? ` · ${product.servingSize}` : ''}
            {product.timesLogged > 0 ? ` · ${product.timesLogged}x` : ''}
          </p>
        </div>
      </div>
      <div className="flex shrink-0 items-center gap-1 sm:gap-1.5">
        <button
          type="button"
          onClick={() => onView(product)}
          aria-label="Ver producto"
          className="flex items-center gap-0.5 sm:gap-1 rounded-lg border border-[var(--card-border)] px-2 sm:px-2.5 py-1.5 text-[11px] sm:text-xs font-medium text-[var(--text-secondary)] hover:bg-[var(--hover-bg)] whitespace-nowrap"
        >
          <Eye className="h-3.5 w-3.5 sm:h-3.5 sm:w-3.5" />
          <span className="hidden sm:inline">Ver</span>
        </button>
        <button
          type="button"
          onClick={() => onAdd(product)}
          aria-label="Adicionar a nutrición"
          className="flex items-center gap-0.5 sm:gap-1 rounded-lg bg-[var(--accent)] px-2 sm:px-2.5 py-1.5 text-[11px] sm:text-xs font-semibold text-white whitespace-nowrap"
        >
          <Plus className="h-3.5 w-3.5 sm:h-3.5 sm:w-3.5" />
          <span className="hidden sm:inline">Adicionar</span>
        </button>
      </div>
    </div>
  );
}

/** "Productos" page: browse the global food database built up by every user scanning
 * nutrition labels, view a product's full nutrition info, and log an already-known product
 * as a meal with a single tap - no re-scanning needed. Also has a "Mi catálogo" tab, read-only,
 * for the per-person catalog saved via the chat's "Guardar en mi catálogo" button. */
export function ProductsPage() {
  const [activeTab, setActiveTab] = useState<'global' | 'personal'>('global');
  const [products, setProducts] = useState<FoodItem[]>([]);
  const [search, setSearch] = useState('');
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [viewing, setViewing] = useState<FoodItem | null>(null);
  const [adding, setAdding] = useState<FoodItem | null>(null);
  const [confirmation, setConfirmation] = useState<string | null>(null);

  const [personalItems, setPersonalItems] = useState<PersonalFoodItem[]>([]);
  const [isLoadingPersonal, setIsLoadingPersonal] = useState(true);
  const [personalError, setPersonalError] = useState<string | null>(null);
  const [viewingPersonal, setViewingPersonal] = useState<PersonalFoodItem | null>(null);
  const [addingPersonal, setAddingPersonal] = useState<PersonalFoodItem | null>(null);
  const [personalConfirmation, setPersonalConfirmation] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setIsLoading(true);
    setError(null);

    const handle = setTimeout(() => {
      getFoodItems(search.trim() || undefined)
        .then((data) => {
          if (!cancelled) setProducts(data);
        })
        .catch(() => {
          if (!cancelled) setError('No se pudieron cargar los productos.');
        })
        .finally(() => {
          if (!cancelled) setIsLoading(false);
        });
    }, 300);

    return () => {
      cancelled = true;
      clearTimeout(handle);
    };
  }, [search]);

  useEffect(() => {
    if (activeTab !== 'personal') return;
    let cancelled = false;
    setIsLoadingPersonal(true);
    setPersonalError(null);

    getPersonalFoodItems()
      .then((data) => {
        if (!cancelled) setPersonalItems(data);
      })
      .catch(() => {
        if (!cancelled) setPersonalError('No se pudo cargar tu catálogo personal.');
      })
      .finally(() => {
        if (!cancelled) setIsLoadingPersonal(false);
      });

    return () => {
      cancelled = true;
    };
  }, [activeTab]);

  return (
    <div className="flex h-full flex-col overflow-hidden bg-[var(--app-bg)]">
      <div className="border-b border-[var(--card-border)] bg-[var(--card-bg)] px-3 sm:px-4 py-2 sm:py-3 sticky top-0 z-10">
        <h1 className="text-sm sm:text-base font-semibold text-[var(--text-primary)]">Productos</h1>
        <p className="max-h-10 overflow-hidden text-xs text-[var(--text-muted)] line-clamp-2">
          {activeTab === 'global' ? 'Productos ya creados por usuarios al escanear etiquetas' : 'Comidas que has guardado desde el chat'}
        </p>
        <div className="mt-2 inline-flex w-full gap-0.5 rounded-lg border border-[var(--card-border)] bg-[var(--app-bg)] p-0.5">
          <button
            type="button"
            onClick={() => setActiveTab('global')}
            className={`flex-1 min-w-0 truncate rounded-md px-2 py-1.5 text-xs font-medium transition-colors whitespace-nowrap overflow-hidden text-ellipsis ${
              activeTab === 'global' ? 'bg-[var(--accent)] text-white' : 'text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]'
            }`}
          >
            Productos
          </button>
          <button
            type="button"
            onClick={() => setActiveTab('personal')}
            className={`flex-1 min-w-0 truncate rounded-md px-2 py-1.5 text-xs font-medium transition-colors whitespace-nowrap overflow-hidden text-ellipsis ${
              activeTab === 'personal' ? 'bg-[var(--accent)] text-white' : 'text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]'
            }`}
          >
            Mi catálogo
          </button>
        </div>
        {activeTab === 'global' && (
          <div className="mt-2 flex items-center gap-2 rounded-lg border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2">
            <Search className="h-4 w-4 shrink-0 text-[var(--text-muted)]" />
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Buscar…"
              className="w-full bg-transparent text-xs sm:text-sm text-[var(--text-primary)] outline-none"
            />
          </div>
        )}
      </div>

      {activeTab === 'personal' ? (
        <div className="flex-1 overflow-y-auto px-3 sm:px-4 py-4">
          {personalError && <div className="mb-4 rounded-lg bg-red-50 px-4 py-2 text-sm text-red-600">{personalError}</div>}
          {personalConfirmation && (
            <div className="mb-4 rounded-lg bg-emerald-50 px-4 py-2 text-sm text-emerald-700">{personalConfirmation}</div>
          )}

          {isLoadingPersonal ? (
            <div className="text-sm text-[var(--text-muted)]">Cargando…</div>
          ) : personalItems.length === 0 ? (
            <div className="rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 text-sm text-[var(--text-muted)]">
              Aún no has guardado nada en tu catálogo. Desde el chat, usa "Guardar en mi catálogo" después de que el agente calcule una comida.
            </div>
          ) : (
            <div className="space-y-2">
              {personalItems.map((item) => (
                <PersonalFoodRow key={item.id} item={item} onView={setViewingPersonal} onAdd={setAddingPersonal} />
              ))}
            </div>
          )}
        </div>
      ) : (
      <div className="flex-1 overflow-y-auto px-3 sm:px-4 py-4">
        {error && <div className="mb-4 rounded-lg bg-red-50 px-4 py-2 text-sm text-red-600">{error}</div>}
        {confirmation && (
          <div className="mb-4 rounded-lg bg-emerald-50 px-4 py-2 text-sm text-emerald-700">{confirmation}</div>
        )}

        {isLoading ? (
          <div className="text-sm text-[var(--text-muted)]">Cargando…</div>
        ) : products.length === 0 ? (
          <div className="rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 text-sm text-[var(--text-muted)]">
            {search.trim()
              ? 'No se encontraron productos con ese nombre.'
              : 'Aún no hay productos guardados. Escanea la etiqueta de un alimento desde Nutrición o Agente para empezar.'}
          </div>
        ) : (
          <div className="space-y-2">
            {products.map((product) => (
              <ProductRow key={product.id} product={product} onView={setViewing} onAdd={setAdding} />
            ))}
          </div>
        )}
      </div>
      )}

      {viewingPersonal && (
        <PersonalFoodDetailSheet
          item={viewingPersonal}
          onClose={() => setViewingPersonal(null)}
          onRequestAdd={() => {
            setAddingPersonal(viewingPersonal);
            setViewingPersonal(null);
          }}
        />
      )}

      {addingPersonal && (
        <LogPersonalFoodSheet
          item={addingPersonal}
          onClose={() => setAddingPersonal(null)}
          onLogged={() => {
            setAddingPersonal(null);
            setPersonalConfirmation(`"${addingPersonal.name}" agregado a tu consumo de hoy.`);
            setTimeout(() => setPersonalConfirmation(null), 4000);
          }}
        />
      )}

      {viewing && (
        <ProductDetailSheet
          product={viewing}
          onClose={() => setViewing(null)}
          onRequestAdd={() => {
            setAdding(viewing);
            setViewing(null);
          }}
        />
      )}

      {adding && (
        <LogProductSheet
          product={adding}
          onClose={() => setAdding(null)}
          onLogged={() => {
            setAdding(null);
            setConfirmation(`"${adding.name}" agregado a tu consumo de hoy.`);
            setTimeout(() => setConfirmation(null), 4000);
          }}
        />
      )}
    </div>
  );
}
