import { useState, useRef, useEffect, type FormEvent } from 'react';
import { Mic, LogOut, Camera, Loader2, Check, Plus, BookmarkPlus } from 'lucide-react';
import { askAgent, getAgentProgress, logPendingMealToday, savePendingMealToCatalog, type PendingMeal } from '../api/agentApi';
import { extractFoodLabel, type FoodLabelExtractionResult } from '../api/foodLabelApi';
import { FoodLabelReviewSheet } from './FoodLabelReviewSheet';
import { PhotoSourceSheet } from './PhotoSourceSheet';
import { CameraCaptureModal } from './CameraCaptureModal';
import { AgentIcon } from './AgentIcon';
import { useChatWidget } from '../contexts/ChatWidgetContext';
import { useAuth } from '../contexts/AuthContext';

const SESSION_STORAGE_KEY = 'personal-agent-mobile-session-id';

type PendingMealAction = 'log' | 'save';

export function ChatPanel() {
  const { messages, addMessage, isVoiceActive, setVoiceActive } = useChatWidget();
  const { logout, user } = useAuth();
  const [input, setInput] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [progressLines, setProgressLines] = useState<string[]>([]);
  // Tracks in-flight/completed state per message id for EACH button independently, so
  // "Agregar a comida de hoy" and "Guardar en mi catálogo" can both be clicked (they're not
  // mutually exclusive - a user may want the meal logged today AND saved to their catalog).
  const [pendingMealActions, setPendingMealActions] = useState<
    Record<number, Partial<Record<PendingMealAction, 'loading' | 'done'>>>
  >({});
  // Persisted across page reloads (sessionStorage) so the agent keeps remembering the
  // conversation within the same browser tab session.
  const [sessionId, setSessionId] = useState<string | null>(() =>
    sessionStorage.getItem(SESSION_STORAGE_KEY),
  );
  const scrollRef = useRef<HTMLDivElement>(null);
  const cameraInputRef = useRef<HTMLInputElement | null>(null);
  const galleryInputRef = useRef<HTMLInputElement | null>(null);
  const [isScanning, setIsScanning] = useState(false);
  const [pendingExtraction, setPendingExtraction] = useState<FoodLabelExtractionResult | null>(null);
  const [showPhotoSource, setShowPhotoSource] = useState(false);
  const [showCamera, setShowCamera] = useState(false);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' });
  }, [messages]);

  const handleFileSelected = async (file: File | undefined) => {
    if (!file) return;
    addMessage('user', '📷 Foto de etiqueta de alimento');
    setIsScanning(true);
    try {
      const result = await extractFoodLabel(file);
      if (!result.isValidLabel) {
        addMessage(
          'assistant',
          result.reason || 'La imagen no muestra una etiqueta de información nutricional legible. Intenta con una foto más clara.',
        );
        return;
      }
      addMessage(
        'assistant',
        `Encontré la información nutricional de "${result.name}"${result.brand ? ` (${result.brand})` : ''}. Revisa los datos y confírmame si quieres que lo agregue a tu consumo.`,
      );
      setPendingExtraction(result);
    } catch {
      addMessage('assistant', 'No pude analizar esa imagen. Intenta de nuevo con una foto más clara de la etiqueta.');
    } finally {
      setIsScanning(false);
    }
  };

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    const trimmed = input.trim();
    if (!trimmed || isLoading) {
      return;
    }

    addMessage('user', trimmed);
    setInput('');
    setIsLoading(true);
    setError(null);
    setProgressLines([]);

    // Generate the sessionId up front (instead of waiting for askAgent's response) so we
    // can poll GET /agent/progress for this same session while the request is in flight.
    const activeSessionId = sessionId ?? crypto.randomUUID().replace(/-/g, '');
    const progressInterval = window.setInterval(() => {
      void getAgentProgress(activeSessionId)
        .then((lines) => {
          if (lines.length > 0) {
            setProgressLines((prev) => [...prev, ...lines]);
          }
        })
        .catch(() => {
          // Best-effort UX nicety - ignore polling failures, the main askAgent call will
          // still surface any real error.
        });
    }, 1200);

    try {
      const { reply, sessionId: newSessionId, pendingMeal } = await askAgent(trimmed, activeSessionId, user?.displayName);
      setSessionId(newSessionId);
      sessionStorage.setItem(SESSION_STORAGE_KEY, newSessionId);
      addMessage('assistant', reply, pendingMeal);
    } catch (err) {
      const message =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'No se pudo contactar al agente. Verifica tu conexión.';
      setError(message);
    } finally {
      window.clearInterval(progressInterval);
      setIsLoading(false);
      setProgressLines([]);
    }
  };

  const setMealActionState = (messageId: number, action: PendingMealAction, state: 'loading' | 'done' | undefined) => {
    setPendingMealActions((prev) => ({ ...prev, [messageId]: { ...prev[messageId], [action]: state } }));
  };

  const handleLogPendingMealToday = async (messageId: number, meal: PendingMeal) => {
    setMealActionState(messageId, 'log', 'loading');
    try {
      const { confirmation } = await logPendingMealToday(meal);
      addMessage('assistant', confirmation);
      setMealActionState(messageId, 'log', 'done');
    } catch {
      addMessage('assistant', 'No pude agregarlo a tu consumo de hoy. Intenta de nuevo.');
      setMealActionState(messageId, 'log', undefined);
    }
  };

  const handleSavePendingMealToCatalog = async (messageId: number, meal: PendingMeal) => {
    setMealActionState(messageId, 'save', 'loading');
    try {
      await savePendingMealToCatalog(meal);
      addMessage('assistant', `Guardado en tu catálogo personal: "${meal.description}".`);
      setMealActionState(messageId, 'save', 'done');
    } catch {
      addMessage('assistant', 'No pude guardarlo en tu catálogo. Intenta de nuevo.');
      setMealActionState(messageId, 'save', undefined);
    }
  };

  return (
    <div className="flex h-full w-full flex-col bg-[var(--app-bg)]">
      <div className="flex items-center justify-between border-b border-[var(--card-border)] bg-[var(--card-bg)] px-4 py-3">
        <div className="flex items-center gap-2">
          <AgentIcon className="h-7 w-7" />
          <span className="text-base font-semibold text-[var(--text-primary)]">Mi Agente de Salud</span>
        </div>
        <div className="flex items-center gap-2">
          {!isVoiceActive && (
            <button
              type="button"
              onClick={() => setVoiceActive(true)}
              className="flex items-center gap-1.5 rounded-full bg-[var(--accent-soft)] px-3 py-1.5 text-sm font-medium text-[var(--accent-text)] transition-opacity hover:opacity-75"
            >
              <Mic className="h-4 w-4" />
              <span className="hidden sm:inline">Toca para hablar</span>
            </button>
          )}
          <button
            type="button"
            onClick={() => void logout()}
            aria-label="Cerrar sesión"
            className="rounded-full border border-[var(--card-border)] p-1.5 text-[var(--text-secondary)] transition-colors hover:bg-[var(--hover-bg)]"
          >
            <LogOut className="h-4 w-4" />
          </button>
        </div>
      </div>

      <div ref={scrollRef} className="flex-1 space-y-3 overflow-y-auto px-4 py-4">
        {messages.map((message) => (
          <div
            key={message.id}
            className={`flex ${message.role === 'user' ? 'justify-end' : 'justify-start'}`}
          >
            <div
              className={`max-w-[80%] whitespace-pre-wrap rounded-2xl px-4 py-2 text-sm ${
                message.role === 'user'
                  ? 'rounded-tr-sm bg-[var(--accent)] text-white'
                  : 'rounded-tl-sm bg-[var(--card-bg)] text-[var(--text-primary)] shadow-sm'
              }`}
            >
              {message.text}
              {message.pendingMeal && (
                <div className="mt-2 flex flex-wrap gap-2">
                  <button
                    type="button"
                    disabled={pendingMealActions[message.id]?.log !== undefined}
                    onClick={() => void handleLogPendingMealToday(message.id, message.pendingMeal!)}
                    className="flex items-center gap-1.5 rounded-full bg-[var(--accent)] px-3 py-1.5 text-xs font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50"
                  >
                    {pendingMealActions[message.id]?.log === 'loading' ? (
                      <Loader2 className="h-3.5 w-3.5 animate-spin" />
                    ) : pendingMealActions[message.id]?.log === 'done' ? (
                      <Check className="h-3.5 w-3.5" />
                    ) : (
                      <Plus className="h-3.5 w-3.5" />
                    )}
                    Presiona para sumarlo a tu consumo de hoy
                  </button>
                  <button
                    type="button"
                    disabled={pendingMealActions[message.id]?.save !== undefined}
                    onClick={() => void handleSavePendingMealToCatalog(message.id, message.pendingMeal!)}
                    className="flex items-center gap-1.5 rounded-full border border-[var(--card-border)] px-3 py-1.5 text-xs font-medium text-[var(--text-primary)] transition-colors hover:bg-[var(--hover-bg)] disabled:opacity-50"
                  >
                    {pendingMealActions[message.id]?.save === 'loading' ? (
                      <Loader2 className="h-3.5 w-3.5 animate-spin" />
                    ) : pendingMealActions[message.id]?.save === 'done' ? (
                      <Check className="h-3.5 w-3.5" />
                    ) : (
                      <BookmarkPlus className="h-3.5 w-3.5" />
                    )}
                    Presiona para guardarlo en tu catálogo
                  </button>
                </div>
              )}
            </div>
          </div>
        ))}
        {isLoading && (
          <div className="flex justify-start">
            <div className="max-w-[80%] rounded-2xl rounded-tl-sm bg-[var(--card-bg)] px-4 py-3 shadow-sm">
              {progressLines.length > 0 && (
                <div className="mb-2 space-y-1 text-sm text-[var(--text-secondary)]">
                  {progressLines.map((line, index) => (
                    <div key={index}>{line}</div>
                  ))}
                </div>
              )}
              <div className="flex items-center gap-1">
                <span className="h-2 w-2 animate-bounce rounded-full bg-[var(--text-muted)] [animation-delay:-0.3s]" />
                <span className="h-2 w-2 animate-bounce rounded-full bg-[var(--text-muted)] [animation-delay:-0.15s]" />
                <span className="h-2 w-2 animate-bounce rounded-full bg-[var(--text-muted)]" />
              </div>
            </div>
          </div>
        )}
      </div>

      {error && <div className="px-4 pb-2 text-sm text-red-600">{error}</div>}

      <form onSubmit={handleSubmit} className="border-t border-[var(--card-border)] bg-[var(--card-bg)] p-3 pb-[max(0.75rem,env(safe-area-inset-bottom))]">
        <div className="flex items-center gap-2 rounded-full border border-[var(--input-border)] px-4 py-2 focus-within:border-[var(--accent)]">
          <input
            ref={cameraInputRef}
            type="file"
            accept="image/*"
            capture="environment"
            className="hidden"
            onChange={(event) => {
              void handleFileSelected(event.target.files?.[0]);
              event.target.value = '';
            }}
          />
          <input
            ref={galleryInputRef}
            type="file"
            accept="image/*"
            className="hidden"
            onChange={(event) => {
              void handleFileSelected(event.target.files?.[0]);
              event.target.value = '';
            }}
          />
          <button
            type="button"
            onClick={() => setShowPhotoSource(true)}
            disabled={isScanning}
            aria-label="Escanear etiqueta de alimento"
            className="shrink-0 text-[var(--text-secondary)] disabled:opacity-40"
          >
            {isScanning ? <Loader2 className="h-5 w-5 animate-spin" /> : <Camera className="h-5 w-5" />}
          </button>
          <input
            type="text"
            value={input}
            onChange={(event) => setInput(event.target.value)}
            placeholder="Escribe tu pregunta…"
            className="flex-1 bg-transparent text-sm text-[var(--text-primary)] outline-none"
            disabled={isLoading}
          />
          <button
            type="submit"
            disabled={isLoading || !input.trim()}
            className="rounded-full bg-[var(--accent)] px-4 py-1.5 text-sm font-medium text-white disabled:opacity-40"
          >
            Enviar
          </button>
        </div>
      </form>

      {showPhotoSource && (
        <PhotoSourceSheet
          onClose={() => setShowPhotoSource(false)}
          onTakePhoto={() => {
            setShowPhotoSource(false);
            setShowCamera(true);
          }}
          onChooseGallery={() => {
            setShowPhotoSource(false);
            galleryInputRef.current?.click();
          }}
        />
      )}

      {showCamera && (
        <CameraCaptureModal
          onClose={() => setShowCamera(false)}
          onCapture={(file) => {
            setShowCamera(false);
            void handleFileSelected(file);
          }}
          onUnavailable={() => {
            setShowCamera(false);
            cameraInputRef.current?.click();
          }}
        />
      )}

      {pendingExtraction && (
        <FoodLabelReviewSheet
          extraction={pendingExtraction}
          onClose={() => setPendingExtraction(null)}
          onSaved={() => {
            setPendingExtraction(null);
            addMessage('assistant', 'Listo, lo agregué a tu consumo de hoy. ¿Algo más?');
          }}
        />
      )}
    </div>
  );
}
