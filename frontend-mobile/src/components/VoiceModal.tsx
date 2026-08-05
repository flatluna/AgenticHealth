import { VoiceChatAgent } from './VoiceChatAgent';
import { useChatWidget } from '../contexts/ChatWidgetContext';

/** Small persistent voice-call modal, bottom-left so it never overlaps the text chat
 * panel's mic button. Auto-connects the moment it appears - a single tap on ChatPanel's
 * mic button is enough to start talking, no second tap needed. */
export function VoiceModal() {
  const { isVoiceActive, addMessage, setVoiceActive } = useChatWidget();

  if (!isVoiceActive) {
    return null;
  }

  return (
    <div className="fixed bottom-24 left-1/2 z-50 w-56 -translate-x-1/2 rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] shadow-2xl sm:bottom-5 sm:left-5 sm:translate-x-0">
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
