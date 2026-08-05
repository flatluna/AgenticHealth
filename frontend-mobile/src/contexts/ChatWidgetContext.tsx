import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';

export interface ChatMessage {
  id: number;
  role: 'user' | 'assistant';
  text: string;
}

let nextMessageId = 1;

interface ChatWidgetContextValue {
  /** True while a voice call is live - shown in its own small modal (see VoiceModal), separate from the text panel. */
  isVoiceActive: boolean;
  setVoiceActive: (active: boolean) => void;
  /** Shared across the text panel and the voice modal, so voice transcripts show up in the same history. */
  messages: ChatMessage[];
  addMessage: (role: ChatMessage['role'], text: string) => void;
}

const ChatWidgetContext = createContext<ChatWidgetContextValue | undefined>(undefined);

/** Same shared-state pattern as the main suite frontend, trimmed down (no open/close -
 * this mobile app only ever shows the chat, full screen, no floating bubble). */
export function ChatWidgetProvider({ children }: { children: ReactNode }) {
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
      isVoiceActive,
      setVoiceActive,
      messages,
      addMessage: (role: ChatMessage['role'], text: string) =>
        setMessages((prev) => [...prev, { id: nextMessageId++, role, text }]),
    }),
    [isVoiceActive, messages],
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
