import { VoiceChatAgent } from './VoiceChatAgent';
import { useChatWidget } from '../contexts/ChatWidgetContext';

/** Small persistent voice-call modal, bottom-left so it never overlaps the text chat
 * panel/FAB (bottom-right). Auto-connects the moment it appears - a single click on
 * ChatPanel's mic button is enough to start talking, no second click needed. */
export function VoiceModal() {
  const { isVoiceActive, addMessage, setVoiceActive } = useChatWidget();

  if (!isVoiceActive) {
    return null;
  }

  return (
    <div className="fixed bottom-5 left-5 z-50 w-56 rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] shadow-2xl">
      <VoiceChatAgent
        autoConnect
        compact
        onUserTranscript={(text) => addMessage('user', text)}
        onAssistantTranscript={(text) => addMessage('assistant', text)}
        onClose={() => setVoiceActive(false)}
      />
    </div>
  );
}
