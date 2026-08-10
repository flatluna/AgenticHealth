import { useState } from 'react';
import { Check } from 'lucide-react';

export function SaveProductScopeModal({
  productName,
  onSave,
  onCancel,
}: {
  productName: string;
  onSave: (scopes: ('global' | 'local')[]) => void;
  onCancel: () => void;
}) {
  const [selectedScopes, setSelectedScopes] = useState<Set<'global' | 'local'>>(new Set(['global', 'local']));

  const toggleGlobal = () => {
    const newScopes = new Set(selectedScopes);
    if (newScopes.has('global')) {
      newScopes.delete('global');
    } else {
      newScopes.add('global');
    }
    setSelectedScopes(newScopes);
  };

  const toggleLocal = () => {
    const newScopes = new Set(selectedScopes);
    if (newScopes.has('local')) {
      newScopes.delete('local');
    } else {
      newScopes.add('local');
    }
    setSelectedScopes(newScopes);
  };

  const handleSave = () => {
    onSave(Array.from(selectedScopes));
  };

  const isGlobalSelected = selectedScopes.has('global');
  const isLocalSelected = selectedScopes.has('local');
  const canSave = selectedScopes.size > 0;

  return (
    <div className="fixed inset-0 z-50 flex items-end bg-black/50 backdrop-blur-sm">
      <div className="w-full animate-in slide-in-from-bottom rounded-t-3xl bg-[var(--card-bg)] p-6 shadow-xl">
        <div className="mb-6 text-center">
          <h2 className="text-lg font-semibold text-[var(--text-primary)]">
            Guardar "{productName}"
          </h2>
          <p className="mt-1 text-sm text-[var(--text-secondary)]">
            Selecciona dónde guardarlo (puedes elegir ambos)
          </p>
        </div>

        <div className="space-y-3">
          <button
            type="button"
            onClick={toggleGlobal}
            className={`flex w-full items-center gap-4 rounded-xl border transition-all ${
              isGlobalSelected
                ? 'border-blue-500 bg-gradient-to-r from-blue-50/20 to-cyan-50/20'
                : 'border-[var(--card-border)] bg-gradient-to-r from-blue-50/5 to-cyan-50/5 hover:bg-gradient-to-r hover:from-blue-50/15 hover:to-cyan-50/15'
            } p-4 text-left`}
          >
            <div className={`flex h-6 w-6 items-center justify-center rounded-full ${
              isGlobalSelected ? 'bg-blue-500' : 'border-2 border-blue-300 bg-transparent'
            }`}>
              {isGlobalSelected && <Check className="h-4 w-4 text-white" />}
            </div>
            <div className="flex-1">
              <div className="font-medium text-[var(--text-primary)]">Productos Globales</div>
              <div className="text-xs text-[var(--text-secondary)]">
                Disponible para todos en la app
              </div>
            </div>
          </button>

          <button
            type="button"
            onClick={toggleLocal}
            className={`flex w-full items-center gap-4 rounded-xl border transition-all ${
              isLocalSelected
                ? 'border-purple-500 bg-gradient-to-r from-purple-50/20 to-pink-50/20'
                : 'border-[var(--card-border)] bg-gradient-to-r from-purple-50/5 to-pink-50/5 hover:bg-gradient-to-r hover:from-purple-50/15 hover:to-pink-50/15'
            } p-4 text-left`}
          >
            <div className={`flex h-6 w-6 items-center justify-center rounded-full ${
              isLocalSelected ? 'bg-purple-500' : 'border-2 border-purple-300 bg-transparent'
            }`}>
              {isLocalSelected && <Check className="h-4 w-4 text-white" />}
            </div>
            <div className="flex-1">
              <div className="font-medium text-[var(--text-primary)]">Mi Catálogo</div>
              <div className="text-xs text-[var(--text-secondary)]">
                Solo para ti
              </div>
            </div>
          </button>
        </div>

        <div className="mt-6 flex gap-3">
          <button
            type="button"
            onClick={onCancel}
            className="flex-1 rounded-lg border border-[var(--card-border)] px-4 py-2.5 font-medium text-[var(--text-secondary)] transition-colors hover:bg-[var(--hover-bg)]"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={handleSave}
            disabled={!canSave}
            className="flex-1 rounded-lg bg-blue-500 px-4 py-2.5 font-medium text-white transition-all hover:bg-blue-600 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Guardar
          </button>
        </div>
      </div>
    </div>
  );
}
