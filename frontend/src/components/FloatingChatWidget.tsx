import { ChatPanel } from './ChatPanel';
import { AgentIcon } from './AgentIcon';
import { useChatWidget } from '../contexts/ChatWidgetContext';

/** Persistent floating chat bubble + panel, rendered once in AppLayout so it stays mounted
 * (and keeps its conversation) while the user navigates between pages like Peso, Objetivos, etc.
 * Full-screen on mobile, anchored bottom-right card on larger screens. */
export function FloatingChatWidget() {
  const { isOpen, toggle, close } = useChatWidget();

  return (
    <>
      <button
        type="button"
        onClick={toggle}
        aria-label={isOpen ? 'Cerrar agente de salud' : 'Abrir agente de salud'}
        className={`fixed bottom-5 right-5 z-50 flex h-14 w-14 items-center justify-center rounded-full bg-[var(--accent)] text-white shadow-xl transition-all duration-200 hover:scale-105 active:scale-95 ${
          isOpen ? 'pointer-events-none scale-0 opacity-0' : 'scale-100 opacity-100'
        }`}
      >
        <AgentIcon className="h-7 w-7" />
      </button>

      {/* Kept mounted at all times (visibility toggled via CSS only) so the conversation
          survives closing/reopening the widget while browsing other pages. */}
      <div
        role="dialog"
        aria-label="Agente de Salud"
        aria-hidden={!isOpen}
        className={`fixed inset-0 z-50 flex flex-col overflow-hidden border border-[var(--card-border)] shadow-2xl transition-all duration-200 sm:inset-auto sm:bottom-24 sm:right-5 sm:h-[min(640px,calc(100vh-7rem))] sm:w-[380px] sm:rounded-3xl ${
          isOpen ? 'translate-y-0 opacity-100' : 'pointer-events-none translate-y-4 opacity-0'
        }`}
      >
        <ChatPanel onClose={close} />
      </div>
    </>
  );
}
