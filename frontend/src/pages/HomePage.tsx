import { useEffect } from 'react';
import { Link } from 'react-router';
import { Apple, Dumbbell, Scale, Target, UserCircle } from 'lucide-react';
import { useChatWidget } from '../contexts/ChatWidgetContext';
import { useAuth } from '../contexts/AuthContext';

const SHORTCUTS = [
  { to: '/app/nutricion', label: 'Nutrición', icon: Apple, chip: 'bg-emerald-500/15 text-emerald-500' },
  { to: '/app/ejercicios', label: 'Ejercicios', icon: Dumbbell, chip: 'bg-orange-500/15 text-orange-500' },
  { to: '/app/peso', label: 'Peso', icon: Scale, chip: 'bg-sky-500/15 text-sky-500' },
  { to: '/app/objetivos', label: 'Objetivos', icon: Target, chip: 'bg-rose-500/15 text-rose-500' },
  { to: '/app/perfil', label: 'Perfil', icon: UserCircle, chip: 'bg-indigo-500/15 text-indigo-500' },
];

// Landing page after login. The chat now lives in a floating widget (see FloatingChatWidget)
// available from every page, so this auto-opens it once instead of embedding a full chat here.
export function HomePage() {
  const { open } = useChatWidget();
  const { user } = useAuth();

  useEffect(() => {
    open();
  }, [open]);

  return (
    <div className="h-full overflow-y-auto p-6">
      <h2 className="text-xl font-semibold text-[var(--text-primary)]">
        Hola{user?.displayName ? `, ${user.displayName.split(' ')[0]}` : ''} 👋
      </h2>
      <p className="mt-1 text-sm text-[var(--text-muted)]">
        Tu Agente de Salud está abierto - puedes seguir hablando con él mientras navegas cualquier sección.
      </p>

      <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {SHORTCUTS.map(({ to, label, icon: Icon, chip }) => (
          <Link
            key={to}
            to={to}
            className="flex items-center gap-3 rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-4 shadow-sm transition-colors hover:bg-[var(--hover-bg)]"
          >
            <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${chip}`}>
              <Icon className="h-5 w-5" />
            </span>
            <span className="font-medium text-[var(--text-primary)]">{label}</span>
          </Link>
        ))}
      </div>
    </div>
  );
}
