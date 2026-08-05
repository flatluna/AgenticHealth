import { useState, useRef, useEffect, type FormEvent } from 'react';
import { Mic } from 'lucide-react';
import { askAgent } from '../api/agentApi';
import { VoiceChatAgent } from './VoiceChatAgent';
import { AgentIcon } from './AgentIcon';

interface ChatMessage {
  id: number;
  role: 'user' | 'assistant';
  text: string;
}

let nextId = 1;

const SESSION_STORAGE_KEY = 'personal-agent-session-id';

export function ChatPanel() {
  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      id: nextId++,
      role: 'assistant',
      text: 'Hola 👋 Soy tu asistente personal. Puedo ayudarte con dietas, conteo de calorías, ejercicio y preguntas generales.',
    },
  ]);
  const [input, setInput] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Persisted across page reloads (sessionStorage) so the agent keeps remembering the
  // conversation (e.g. "una banana tiene 120 kcal") within the same browser tab session.
  const [sessionId, setSessionId] = useState<string | null>(() =>
    sessionStorage.getItem(SESSION_STORAGE_KEY),
  );
  const [isVoiceModeOpen, setIsVoiceModeOpen] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' });
  }, [messages]);

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    const trimmed = input.trim();
    if (!trimmed || isLoading) {
      return;
    }

    setMessages((prev) => [...prev, { id: nextId++, role: 'user', text: trimmed }]);
    setInput('');
    setIsLoading(true);
    setError(null);

    try {
      const { reply, sessionId: newSessionId } = await askAgent(trimmed, sessionId);
      setSessionId(newSessionId);
      sessionStorage.setItem(SESSION_STORAGE_KEY, newSessionId);
      setMessages((prev) => [...prev, { id: nextId++, role: 'assistant', text: reply }]);
    } catch (err) {
      const message =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'No se pudo contactar al agente. Verifica que el backend esté corriendo.';
      setError(message);
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="flex h-full w-full flex-col bg-[var(--app-bg)]">
      <div className="flex items-center justify-between border-b border-[var(--card-border)] bg-[var(--card-bg)] px-4 py-2">
        <div className="flex items-center gap-2">
          <AgentIcon className="h-6 w-6" />
          <span className="text-sm font-medium text-[var(--text-muted)]">Agente de Salud</span>
        </div>
        {/* Only shown when voice mode is closed - once open, VoiceChatAgent's own "Salir del modo voz" is the single way to close it. */}
        {!isVoiceModeOpen && (
          <button
            type="button"
            onClick={() => setIsVoiceModeOpen(true)}
            className="flex items-center gap-1.5 rounded-full bg-[var(--accent-soft)] px-3 py-1.5 text-sm font-medium text-[var(--accent-text)] transition-opacity hover:opacity-75"
          >
            <Mic className="h-4 w-4" />
            Toca para hablar
          </button>
        )}
      </div>

      {isVoiceModeOpen ? (
        <div className="flex flex-1 items-center justify-center bg-[var(--card-bg)] px-4 py-4">
          <VoiceChatAgent
            onUserTranscript={(text) => setMessages((prev) => [...prev, { id: nextId++, role: 'user', text }])}
            onAssistantTranscript={(text) => setMessages((prev) => [...prev, { id: nextId++, role: 'assistant', text }])}
            onClose={() => setIsVoiceModeOpen(false)}
          />
        </div>
      ) : (
      <div ref={scrollRef} className="flex-1 space-y-3 overflow-y-auto px-4 py-4">
        {messages.map((message) => (
          <div
            key={message.id}
            className={`flex ${message.role === 'user' ? 'justify-end' : 'justify-start'}`}
          >
            <div
              className={`max-w-[75%] whitespace-pre-wrap rounded-2xl px-4 py-2 text-sm ${
                message.role === 'user'
                  ? 'rounded-tr-sm bg-[var(--accent)] text-white'
                  : 'rounded-tl-sm bg-[var(--card-bg)] text-[var(--text-primary)] shadow-sm'
              }`}
            >
              {message.text}
            </div>
          </div>
        ))}
        {isLoading && (
          <div className="flex justify-start">
            <div className="rounded-2xl rounded-tl-sm bg-[var(--card-bg)] px-4 py-2 text-sm text-[var(--text-muted)] shadow-sm">
              Pensando…
            </div>
          </div>
        )}
      </div>
      )}

      {!isVoiceModeOpen && error && (
        <div className="px-4 pb-2 text-sm text-red-600">{error}</div>
      )}

      {!isVoiceModeOpen && (
      <form onSubmit={handleSubmit} className="border-t border-[var(--card-border)] bg-[var(--card-bg)] p-3">
        <div className="flex items-center gap-2 rounded-full border border-[var(--input-border)] px-4 py-2 focus-within:border-[var(--accent)]">
          <input
            type="text"
            value={input}
            onChange={(event) => setInput(event.target.value)}
            placeholder="Escribe tu pregunta…"
            className="flex-1 bg-transparent text-sm outline-none"
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
      )}
    </div>
  );
}
