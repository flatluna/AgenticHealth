import { NavLink } from 'react-router';
import { Dumbbell, Scale, Target, X, Apple, UserCircle } from 'lucide-react';
import { AgentIcon } from './AgentIcon';
import { useChatWidget } from '../contexts/ChatWidgetContext';

interface SidebarProps {
  open: boolean;
  onClose: () => void;
  /** Fired when a nav link is clicked - lets the parent auto-collapse the sidebar on mobile only. */
  onNavigate: () => void;
}

const navItems = [
  { to: '/app/nutricion', label: 'Nutrición', icon: Apple, end: false, chip: 'bg-emerald-500/15 text-emerald-500' },
  { to: '/app/ejercicios', label: 'Ejercicios', icon: Dumbbell, end: false, chip: 'bg-orange-500/15 text-orange-500' },
  { to: '/app/peso', label: 'Peso', icon: Scale, end: false, chip: 'bg-sky-500/15 text-sky-500' },
  { to: '/app/objetivos', label: 'Objetivos', icon: Target, end: false, chip: 'bg-rose-500/15 text-rose-500' },
  { to: '/app/perfil', label: 'Perfil', icon: UserCircle, end: false, chip: 'bg-indigo-500/15 text-indigo-500' },
];

export function Sidebar({ open, onClose, onNavigate }: SidebarProps) {
  const { toggle: toggleChat } = useChatWidget();

  return (
    <>
      {open && (
        <div
          className="fixed inset-0 z-30 bg-black/30 lg:hidden"
          onClick={onClose}
          aria-hidden="true"
        />
      )}

      {/* Mobile: off-canvas drawer, fully hidden when closed. Desktop: never hidden -
          collapses to an icon-only rail instead, so the icons stay visible either way. */}
      <aside
        className={`fixed inset-y-0 left-0 z-40 flex flex-col border-r transition-all duration-200 ease-in-out lg:static lg:translate-x-0 ${
          open ? 'w-64 translate-x-0' : 'w-64 -translate-x-full lg:w-20'
        }`}
        style={{ backgroundColor: 'var(--sidebar-bg)', borderColor: 'var(--sidebar-border)' }}
      >
        <div
          className="flex items-center justify-between border-b px-4 py-4"
          style={{ borderColor: 'var(--sidebar-border)' }}
        >
          <span
            className={`bg-clip-text text-lg font-bold text-transparent ${open ? '' : 'lg:hidden'}`}
            style={{ backgroundImage: 'linear-gradient(to right, var(--brand-from), var(--brand-to))' }}
          >
            AgenticHealth
          </span>
          <button
            type="button"
            onClick={onClose}
            className="rounded p-1 hover:opacity-70 lg:hidden"
            style={{ color: 'var(--sidebar-text)' }}
            aria-label="Cerrar menú"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <nav className="flex flex-col gap-1 p-3">
          <button
            type="button"
            onClick={() => {
              toggleChat();
              onNavigate();
            }}
            title="Agente de Salud"
            className={`flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-all hover:bg-[var(--sidebar-hover-bg)] ${
              open ? '' : 'lg:justify-center lg:px-0'
            }`}
            style={{ color: 'var(--sidebar-text)' }}
          >
            <AgentIcon className="h-8 w-8" />
            <span className={open ? '' : 'lg:hidden'}>Agente de Salud</span>
          </button>
          {navItems.map(({ to, label, icon: Icon, end, chip }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              onClick={onNavigate}
              title={label}
              className={({ isActive }) =>
                `flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-all hover:bg-[var(--sidebar-hover-bg)] ${
                  open ? '' : 'lg:justify-center lg:px-0'
                } ${isActive ? 'shadow-sm' : ''}`
              }
              style={({ isActive }) => ({
                backgroundColor: isActive ? 'var(--sidebar-active-bg)' : undefined,
                color: isActive ? 'var(--sidebar-text-active)' : 'var(--sidebar-text)',
              })}
            >
              {Icon ? (
                <span className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-lg ${chip}`}>
                  <Icon className="h-4 w-4" />
                </span>
              ) : (
                <AgentIcon className="h-8 w-8" />
              )}
              <span className={open ? '' : 'lg:hidden'}>{label}</span>
            </NavLink>
          ))}
        </nav>
      </aside>
    </>
  );

}

