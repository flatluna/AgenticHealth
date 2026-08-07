import { useState } from 'react';
import { LogOut, UserCircle, Palette, Check } from 'lucide-react';
import { NavLink } from 'react-router';
import { useAuth } from '../contexts/AuthContext';
import { useTheme, type Theme } from '../contexts/ThemeContext';
import { VersionInfo } from './VersionInfo';

const THEME_OPTIONS: { value: Theme; label: string; swatch: string }[] = [
  { value: 'light', label: 'Claro', swatch: 'bg-gradient-to-br from-slate-100 to-white' },
  { value: 'dark', label: 'Oscuro', swatch: 'bg-gradient-to-br from-slate-800 to-slate-950' },
  { value: 'blue', label: 'Azul elegante', swatch: 'bg-gradient-to-br from-sky-500 to-blue-900' },
];

/**
 * Persistent top bar: company brand on the left, theme switcher + signed-in user +
 * profile/logout on the right.
 */
export function AppHeader() {
  const { user, logout } = useAuth();
  const { theme, setTheme } = useTheme();
  const [themeMenuOpen, setThemeMenuOpen] = useState(false);

  return (
    <header className="flex items-center justify-between gap-1 sm:gap-3 border-b border-[var(--card-border)] bg-[var(--card-bg)] px-2 sm:px-4 py-2">
      <span className="text-xs sm:text-sm font-bold tracking-tight text-[var(--accent-text)] shrink-0">Engrams</span>
      <div className="flex-1" />
      <div className="hidden sm:block text-right text-[10px] text-[var(--text-muted)]">
        <VersionInfo />
      </div>
      <div className="flex items-center gap-1 sm:gap-2 justify-end">
        <div className="relative hidden sm:block">
          <button
            type="button"
            onClick={() => setThemeMenuOpen((prev) => !prev)}
            aria-label="Cambiar tema"
            className="rounded-full border border-[var(--card-border)] p-1.5 text-[var(--text-secondary)] transition-colors hover:bg-[var(--hover-bg)]"
          >
            <Palette className="h-4 w-4" />
          </button>
          {themeMenuOpen && (
            <>
              <div className="fixed inset-0 z-40" onClick={() => setThemeMenuOpen(false)} aria-hidden="true" />
              <div className="absolute right-0 z-50 mt-2 w-44 overflow-hidden rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] shadow-lg">
                {THEME_OPTIONS.map((option) => (
                  <button
                    key={option.value}
                    type="button"
                    onClick={() => {
                      setTheme(option.value);
                      setThemeMenuOpen(false);
                    }}
                    className="flex w-full items-center gap-2.5 px-3 py-2.5 text-sm text-[var(--text-primary)] hover:bg-[var(--hover-bg)]"
                  >
                    <span className={`h-4 w-4 shrink-0 rounded-full border border-black/10 ${option.swatch}`} />
                    <span className="flex-1 text-left">{option.label}</span>
                    {theme === option.value && <Check className="h-4 w-4 shrink-0" />}
                  </button>
                ))}
              </div>
            </>
          )}
        </div>
        <div className="text-right leading-tight hidden sm:block">
          <p className="max-w-[140px] truncate text-xs font-semibold text-[var(--text-primary)]">{user?.displayName || '—'}</p>
          <p className="max-w-[140px] truncate text-[10px] text-[var(--text-muted)]">{user?.email || '—'}</p>
        </div>
        <NavLink
          to="/perfil"
          aria-label="Perfil"
          className={({ isActive }) =>
            `rounded-full border p-1.5 transition-colors ${
              isActive
                ? 'border-[var(--accent-text)] text-[var(--accent-text)]'
                : 'border-[var(--card-border)] text-[var(--text-secondary)] hover:bg-[var(--hover-bg)]'
            }`
          }
        >
          <UserCircle className="h-4 w-4" />
        </NavLink>
        <button
          type="button"
          onClick={() => void logout()}
          aria-label="Cerrar sesión"
          className="rounded-full border border-[var(--card-border)] p-1.5 text-[var(--text-secondary)] transition-colors hover:bg-[var(--hover-bg)]"
        >
          <LogOut className="h-4 w-4" />
        </button>
      </div>
    </header>
  );
}
