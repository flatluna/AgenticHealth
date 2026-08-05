import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';

export interface ChatMessage {
  id: number;
  role: 'user' | 'assistant';
  text: string;
}

let nextMessageId = 1;

interface ChatWidgetContextValue {
  isOpen: boolean;
  open: () => void;
  close: () => void;
  toggle: () => void;
  /** True while a voice call is live - shown in its own small modal (see VoiceModal), separate from the text panel. */
  isVoiceActive: boolean;
  setVoiceActive: (active: boolean) => void;
  /** Shared across the text panel and the voice modal, so voice transcripts show up in the same history. */
  messages: ChatMessage[];
  addMessage: (role: ChatMessage['role'], text: string) => void;
}

const ChatWidgetContext = createContext<ChatWidgetContextValue | undefined>(undefined);

/** Lets any page (via the sidebar link or the floating button) open the persistent chat
 * widget rendered once in AppLayout, so the conversation survives navigating between pages. */
export function ChatWidgetProvider({ children }: { children: ReactNode }) {
  const [isOpen, setIsOpen] = useState(false);
  const [isVoiceActive, setVoiceActive] = useState(false);
  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      id: nextMessageId++,
      role: 'assistant',
      text: 'Hola 👋 Soy tu asistente personal. Puedo ayudarte con dietas, conteo de calorías, ejercicio y preguntas generales.',
    },
  ]);

  const value = useMemo(
    () => ({
      isOpen,
      open: () => setIsOpen(true),
      close: () => setIsOpen(false),
      toggle: () => setIsOpen((prev) => !prev),
      isVoiceActive,
      setVoiceActive,
      messages,
      addMessage: (role: ChatMessage['role'], text: string) =>
        setMessages((prev) => [...prev, { id: nextMessageId++, role, text }]),
    }),
    [isOpen, isVoiceActive, messages],
  );

  return <ChatWidgetContext.Provider value={value}>{children}</ChatWidgetContext.Provider>;
}

export function useChatWidget(): ChatWidgetContextValue {
  const context = useContext(ChatWidgetContext);
  if (!context) {
    throw new Error('useChatWidget must be used within a ChatWidgetProvider');
  }
  return context;
}
