import { Camera, Image, X } from 'lucide-react';

/** Small action sheet letting the user choose between taking a new photo with the
 * camera or picking an existing one from their gallery/files, before scanning a
 * nutrition label. Native file-input choosers vary a lot on mobile, so we surface
 * both intents explicitly with two clearly-labeled hidden inputs. */
export function PhotoSourceSheet({
  onTakePhoto,
  onChooseGallery,
  onClose,
}: {
  onTakePhoto: () => void;
  onChooseGallery: () => void;
  onClose: () => void;
}) {
  return (
    <div className="fixed inset-0 z-50 flex items-end bg-black/40" onClick={onClose}>
      <div
        className="w-full rounded-t-2xl bg-[var(--card-bg)] p-4 pb-[max(1rem,env(safe-area-inset-bottom))] shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-3 flex items-center justify-between">
          <h3 className="text-sm font-semibold text-[var(--text-primary)]">Foto de etiqueta</h3>
          <button
            type="button"
            onClick={onClose}
            aria-label="Cerrar"
            className="rounded-full p-1.5 text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="flex flex-col gap-2">
          <button
            type="button"
            onClick={onTakePhoto}
            className="flex items-center gap-3 rounded-lg border border-[var(--card-border)] px-4 py-3 text-sm font-medium text-[var(--text-primary)] hover:bg-[var(--hover-bg)]"
          >
            <Camera className="h-5 w-5 text-[var(--accent)]" />
            Tomar foto
          </button>
          <button
            type="button"
            onClick={onChooseGallery}
            className="flex items-center gap-3 rounded-lg border border-[var(--card-border)] px-4 py-3 text-sm font-medium text-[var(--text-primary)] hover:bg-[var(--hover-bg)]"
          >
            <Image className="h-5 w-5 text-[var(--accent)]" />
            Elegir de galería
          </button>
        </div>
      </div>
    </div>
  );
}
