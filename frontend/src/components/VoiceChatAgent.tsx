import { useCallback, useEffect, useRef, useState } from 'react';
import { Mic, PhoneOff, Sparkles, AlertCircle } from 'lucide-react';
import { requestVoiceChatSession, executeLogMealTool, executeSearchFoodTool, executeAskAdvisorTool, executeGetRecentMealsTool } from '../api/voiceApi';

type VoiceState = 'idle' | 'connecting' | 'connected' | 'listening' | 'speaking' | 'error';

const STATE_LABEL: Record<VoiceState, string> = {
  idle: 'Toca para hablar',
  connecting: 'Conectando…',
  connected: 'Conectado',
  listening: 'Escuchando…',
  speaking: 'Hablando…',
  error: 'Error de conexión',
};

/**
 * Real-time WebRTC voice mode for the chat page (Azure OpenAI GPT Realtime, see
 * VoiceChatSessionFunction.cs). The browser negotiates the WebRTC session DIRECTLY with
 * Azure (never proxied through our backend) - this component owns the whole lifecycle:
 * mints the ephemeral token, does SDP offer/answer negotiation, plays the remote audio,
 * and derives "listening"/"speaking" purely from the remote audio stream's volume.
 *
 * The session is given real function-calling tools ("log_meal", "search_food_nutrition") -
 * Azure can't execute them itself, so when the model calls one, THIS component receives
 * the call over the data channel, runs it via voiceApi (which hits the same backend the
 * text chat uses), and reports the JSON result back so the model can react/confirm out
 * loud. onMealLogged lets the parent chat page also show a text confirmation (the
 * "escribe" requirement) alongside the spoken transcripts.
 */
export function VoiceChatAgent({
  onUserTranscript,
  onAssistantTranscript,
  onMealLogged,
  onClose,
  autoConnect = false,
  compact = false,
}: {
  onUserTranscript?: (text: string) => void;
  onAssistantTranscript?: (text: string) => void;
  /** Fired right after "log_meal" tool executes successfully, with the confirmation text. */
  onMealLogged?: (confirmation: string) => void;
  onClose?: () => void;
  /** Starts the call immediately on mount instead of waiting for a click on the orb. */
  autoConnect?: boolean;
  /** Smaller orb/spacing, used inside the small left-side voice modal. */
  compact?: boolean;
}) {
  const [state, setState] = useState<VoiceState>('idle');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  // Normalized 0-1 volume of the agent's own voice, sampled every animation frame - drives
  // the orb's live scale/glow so it visibly "breathes" with the actual audio instead of
  // just showing a static icon.
  const [audioLevel, setAudioLevel] = useState(0);

  const peerConnectionRef = useRef<RTCPeerConnection | null>(null);
  const localStreamRef = useRef<MediaStream | null>(null);
  const audioElementRef = useRef<HTMLAudioElement | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const animationFrameRef = useRef<number | null>(null);
  const dataChannelRef = useRef<RTCDataChannel | null>(null);
  // Dedup guard - Azure can report the same completed function call via both
  // response.output_item.done and response.done; only ever execute a given call_id once.
  const handledToolCallIdsRef = useRef<Set<string>>(new Set());
  // Dedup guard - Azure can also report the same assistant transcript via both
  // response.output_audio_transcript.done and response.done; only forward it once.
  const lastAssistantTranscriptRef = useRef<string | null>(null);

  const teardown = useCallback(() => {
    if (animationFrameRef.current !== null) {
      cancelAnimationFrame(animationFrameRef.current);
      animationFrameRef.current = null;
    }
    dataChannelRef.current = null;
    peerConnectionRef.current?.close();
    peerConnectionRef.current = null;
    localStreamRef.current?.getTracks().forEach((track) => track.stop());
    localStreamRef.current = null;
    audioContextRef.current?.close().catch(() => undefined);
    audioContextRef.current = null;
    setAudioLevel(0);
  }, []);

  useEffect(() => teardown, [teardown]);

  const monitorRemoteAudioLevel = useCallback((stream: MediaStream) => {
    const audioContext = new AudioContext();
    audioContextRef.current = audioContext;
    const source = audioContext.createMediaStreamSource(stream);
    const analyser = audioContext.createAnalyser();
    analyser.fftSize = 512;
    source.connect(analyser);

    const buffer = new Uint8Array(analyser.frequencyBinCount);
    const tick = () => {
      analyser.getByteFrequencyData(buffer);
      let sum = 0;
      for (let i = 0; i < buffer.length; i += 1) sum += buffer[i];
      const average = sum / buffer.length;
      const isAgentSpeaking = average > 12;
      setAudioLevel(Math.min(1, average / 70));

      if (isAgentSpeaking && audioElementRef.current?.paused) {
        audioElementRef.current.play().catch(() => undefined);
      }

      setState((current) => {
        if (current === 'idle' || current === 'connecting' || current === 'error') return current;
        return isAgentSpeaking ? 'speaking' : 'listening';
      });
      animationFrameRef.current = requestAnimationFrame(tick);
    };
    tick();
  }, []);

  /** Executes a Realtime function-calling tool and returns its JSON string result. */
  const runTool = useCallback(
    async (toolName: string, args: Record<string, unknown>): Promise<string> => {
      try {
        if (toolName === 'log_meal') {
          const { confirmation } = await executeLogMealTool(args);
          onMealLogged?.(confirmation);
          return JSON.stringify({ confirmation });
        }
        if (toolName === 'search_food_nutrition') {
          const foodDescription = (args.foodDescription as string) ?? '';
          const { result } = await executeSearchFoodTool(foodDescription);
          return JSON.stringify({ result });
        }
        if (toolName === 'ask_health_advisor') {
          const question = (args.question as string) ?? '';
          const { result } = await executeAskAdvisorTool(question);
          return JSON.stringify({ result });
        }
        if (toolName === 'get_recent_meals') {
          const daysBack = typeof args.daysBack === 'number' ? args.daysBack : undefined;
          const { result } = await executeGetRecentMealsTool(daysBack);
          return JSON.stringify({ result });
        }
        return JSON.stringify({ error: `Herramienta desconocida: ${toolName}` });
      } catch (err) {
        console.error(`VoiceChatAgent: tool "${toolName}" failed`, err);
        return JSON.stringify({ error: 'La herramienta falló al ejecutarse.' });
      }
    },
    [onMealLogged],
  );

  const connect = useCallback(async () => {
    setErrorMessage(null);
    setState('connecting');
    handledToolCallIdsRef.current.clear();
    try {
      const session = await requestVoiceChatSession();

      const micStream = await navigator.mediaDevices.getUserMedia({
        audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true },
      });
      localStreamRef.current = micStream;

      const peerConnection = new RTCPeerConnection();
      peerConnectionRef.current = peerConnection;

      micStream.getTracks().forEach((track) => peerConnection.addTrack(track, micStream));

      peerConnection.ontrack = (event) => {
        const [remoteStream] = event.streams;
        if (audioElementRef.current) {
          audioElementRef.current.srcObject = remoteStream;
        }
        monitorRemoteAudioLevel(remoteStream);
      };

      const dataChannel = peerConnection.createDataChannel('oai-events');
      dataChannelRef.current = dataChannel;
      dataChannel.onmessage = (event) => {
        let payload: Record<string, unknown>;
        try {
          payload = JSON.parse(event.data as string);
        } catch {
          return;
        }

        if (payload.type === 'input_audio_buffer.speech_started') {
          audioElementRef.current?.pause();
        }

        if (
          onUserTranscript &&
          payload.type === 'conversation.item.input_audio_transcription.completed' &&
          typeof payload.transcript === 'string' &&
          payload.transcript.trim()
        ) {
          onUserTranscript(payload.transcript.trim());
        }

        // The GA Realtime API's exact event name for the assistant's own spoken-text
        // transcript has varied across API versions, so check every shape defensively
        // rather than relying on just one.
        if (onAssistantTranscript) {
          const forwardTranscript = (transcript: string) => {
            const trimmed = transcript.trim();
            if (!trimmed || trimmed === lastAssistantTranscriptRef.current) {
              return;
            }
            lastAssistantTranscriptRef.current = trimmed;
            onAssistantTranscript(trimmed);
          };

          if (
            (payload.type === 'response.output_audio_transcript.done' || payload.type === 'response.audio_transcript.done') &&
            typeof payload.transcript === 'string' &&
            payload.transcript.trim()
          ) {
            forwardTranscript(payload.transcript);
          } else if (payload.type === 'response.done') {
            const output = ((payload.response as Record<string, unknown> | undefined)?.output as
              | Record<string, unknown>[]
              | undefined) ?? [];
            for (const item of output) {
              const content = (item.content as Record<string, unknown>[] | undefined) ?? [];
              for (const part of content) {
                const transcript = (part.transcript as string | undefined) ?? (part.text as string | undefined);
                if (transcript && transcript.trim()) {
                  forwardTranscript(transcript);
                }
              }
            }
          }
        }

        // Function-calling: Azure can report the same completed call via either
        // response.output_item.done or response.done - collect candidates from both.
        const functionCallItems: Record<string, unknown>[] =
          payload.type === 'response.output_item.done' && payload.item
            ? [payload.item as Record<string, unknown>]
            : payload.type === 'response.done'
              ? (((payload.response as Record<string, unknown> | undefined)?.output as
                  | Record<string, unknown>[]
                  | undefined) ?? [])
              : [];

        for (const item of functionCallItems) {
          if (item.type !== 'function_call') continue;
          const callId = item.call_id as string;
          const toolName = item.name as string;
          if (!callId || handledToolCallIdsRef.current.has(callId)) continue;
          handledToolCallIdsRef.current.add(callId);

          let args: Record<string, unknown> = {};
          try {
            args = JSON.parse((item.arguments as string) ?? '{}');
          } catch {
            args = {};
          }

          void runTool(toolName, args).then((output) => {
            if (dataChannelRef.current?.readyState !== 'open') return;
            dataChannelRef.current.send(
              JSON.stringify({
                type: 'conversation.item.create',
                item: { type: 'function_call_output', call_id: callId, output },
              }),
            );
            dataChannelRef.current.send(JSON.stringify({ type: 'response.create' }));
          });
        }
      };
      dataChannel.onopen = () => {
        dataChannel.send(JSON.stringify({ type: 'response.create' }));
      };

      const offer = await peerConnection.createOffer();
      await peerConnection.setLocalDescription(offer);

      const sdpResponse = await fetch(session.realtimeCallsUrl, {
        method: 'POST',
        body: offer.sdp,
        headers: {
          Authorization: `Bearer ${session.clientSecret}`,
          'Content-Type': 'application/sdp',
        },
      });

      if (!sdpResponse.ok) {
        throw new Error(`Fallo la negociación WebRTC (status ${sdpResponse.status}).`);
      }

      const answerSdp = await sdpResponse.text();
      await peerConnection.setRemoteDescription({ type: 'answer', sdp: answerSdp });

      setState('connected');
    } catch (err) {
      console.error('VoiceChatAgent: connection failed', err);
      setErrorMessage(
        err instanceof Error ? err.message : 'No se pudo iniciar la conversación por voz. Verifica el micrófono.',
      );
      teardown();
      setState('error');
    }
  }, [onUserTranscript, onAssistantTranscript, runTool, monitorRemoteAudioLevel, teardown]);

  const disconnect = useCallback(() => {
    teardown();
    setState('idle');
  }, [teardown]);

  const hasAutoConnectedRef = useRef(false);
  useEffect(() => {
    if (autoConnect && !hasAutoConnectedRef.current) {
      hasAutoConnectedRef.current = true;
      connect();
    }
  }, [autoConnect, connect]);

  const isActive = state === 'connected' || state === 'listening' || state === 'speaking';
  const isBusy = state === 'connecting';

  const stateGradient: Record<VoiceState, string> = {
    idle: 'from-purple-400 via-fuchsia-500 to-indigo-500',
    connecting: 'from-amber-400 via-orange-500 to-amber-500',
    connected: 'from-purple-400 via-fuchsia-500 to-indigo-500',
    listening: 'from-emerald-400 via-teal-500 to-cyan-500',
    speaking: 'from-fuchsia-400 via-purple-500 to-indigo-600',
    error: 'from-red-400 via-rose-500 to-red-600',
  };
  const gradient = stateGradient[state];

  return (
    <div className={`flex flex-col items-center rounded-2xl border border-purple-200 bg-purple-50/60 ${compact ? 'gap-2 p-3' : 'gap-3 p-5'}`}>
      <audio ref={audioElementRef} autoPlay className="hidden" />

      <div className={`relative flex items-center justify-center ${compact ? 'h-16 w-16' : 'h-28 w-28'}`}>
        {/* Slowly rotating gradient halo - always visible, gives the orb a "living" feel. */}
        <div
          className={`absolute inset-0 rounded-full bg-gradient-to-tr ${gradient} opacity-60 blur-md voice-orbit-ring`}
        />
        {/* Expanding ripple rings, only while the call is actually live. */}
        {isActive && (
          <>
            <span className={`absolute inset-0 rounded-full bg-gradient-to-tr ${gradient} opacity-50 voice-pulse-ring`} />
            <span
              className={`absolute inset-0 rounded-full bg-gradient-to-tr ${gradient} opacity-30 voice-pulse-ring`}
              style={{ animationDelay: '1s' }}
            />
          </>
        )}

        <button
          type="button"
          onClick={isActive ? disconnect : connect}
          disabled={isBusy}
          style={{ transform: `scale(${1 + audioLevel * 0.18})` }}
          className={`relative z-10 flex items-center justify-center rounded-full bg-gradient-to-br ${gradient} text-white shadow-lg shadow-purple-500/40 transition-transform duration-100 ${
            compact ? 'h-10 w-10' : 'h-16 w-16'
          } ${isBusy ? 'opacity-70' : 'hover:scale-105'} ${state === 'idle' || state === 'connected' ? 'voice-breathe' : ''}`}
          aria-label={isActive ? 'Colgar' : 'Hablar con el asistente'}
        >
          {isBusy ? (
            <Sparkles className={compact ? 'h-4 w-4 animate-spin' : 'h-7 w-7 animate-spin'} />
          ) : isActive ? (
            <PhoneOff className={compact ? 'h-4 w-4' : 'h-7 w-7'} />
          ) : (
            <Mic className={compact ? 'h-4 w-4' : 'h-7 w-7'} />
          )}
        </button>
      </div>

      <p className={compact ? 'text-xs font-medium text-purple-800' : 'text-sm font-medium text-purple-800'}>{STATE_LABEL[state]}</p>

      {errorMessage && (
        <div className="flex items-center gap-1.5 text-xs text-red-600">
          <AlertCircle className="h-4 w-4 shrink-0" />
          <span>{errorMessage}</span>
        </div>
      )}

      {onClose && (
        <button
          type="button"
          onClick={() => {
            teardown();
            onClose();
          }}
          className={compact ? 'text-[11px] font-medium text-slate-500 hover:text-slate-700' : 'text-xs font-medium text-slate-500 hover:text-slate-700'}
        >
          {compact ? 'Colgar' : 'Salir del modo voz'}
        </button>
      )}
    </div>
  );
}
