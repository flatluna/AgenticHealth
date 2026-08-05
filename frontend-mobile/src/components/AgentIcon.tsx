import { Sparkles } from 'lucide-react';

/**
 * Animated gradient "AI" badge used to represent the Agente de Salud - a soft pulsing
 * glow behind a Sparkles glyph.
 */
export function AgentIcon({ className = 'h-5 w-5' }: { className?: string }) {
  return (
    <span className={`relative inline-flex shrink-0 items-center justify-center ${className}`}>
      <span className="absolute inset-0 animate-ping rounded-full bg-fuchsia-400 opacity-40" />
      <span className="absolute inset-0 rounded-full bg-gradient-to-br from-fuchsia-400 via-purple-500 to-indigo-500 shadow-sm shadow-purple-400/50" />
      <Sparkles className="relative h-[60%] w-[60%] text-white" strokeWidth={2.5} />
    </span>
  );
}
