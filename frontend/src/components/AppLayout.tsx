import { useState } from 'react';
import { Outlet } from 'react-router';
import { Menu, User, Palette, Check } from 'lucide-react';
import { Sidebar } from './Sidebar';
import { FloatingChatWidget } from './FloatingChatWidget';
import { VoiceModal } from './VoiceModal';
import { useAuth } from '../contexts/AuthContext';
import { useTheme, type Theme } from '../contexts/ThemeContext';
import { ChatWidgetProvider } from '../contexts/ChatWidgetContext';

const THEME_OPTIONS: { value: Theme; label: string; swatch: string }[] = [
  { value: 'light', label: 'Claro', swatch: 'bg-gradient-to-br from-slate-100 to-white' },
  { value: 'dark', label: 'Oscuro', swatch: 'bg-gradient-to-br from-slate-800 to-slate-950' },
  { value: 'blue', label: 'Azul elegante', swatch: 'bg-gradient-to-br from-sky-500 to-blue-900' },
];

export function AppLayout() {
  const [sidebarOpen, setSidebarOpen] = useState(() => window.matchMedia('(min-width: 1024px)').matches);
  const [themeMenuOpen, setThemeMenuOpen] = useState(false);
  const { user, logout } = useAuth();
  const { theme, setTheme } = useTheme();

  const handleNavigate = () => {
    if (!window.matchMedia('(min-width: 1024px)').matches) {
      setSidebarOpen(false);
    }
  };

  return (
    <ChatWidgetProvider>
      <div className="flex h-screen w-full" style={{ backgroundColor: 'var(--app-bg)' }}>
        <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} onNavigate={handleNavigate} />

        <div className="flex min-w-0 flex-1 flex-col">
          <header
            className="flex items-center gap-3 border-b px-4 py-3 shadow-sm"
            style={{ backgroundColor: 'var(--header-bg)', borderColor: 'var(--header-border)' }}
          >
            <button
              type="button"
              onClick={() => setSidebarOpen((prev) => !prev)}
              className="rounded p-1.5 hover:opacity-70"
              style={{ color: 'var(--header-text)' }}
              aria-label="Mostrar/ocultar menú"
            >
              <Menu className="h-5 w-5" />
            </button>
            <div>
              <h1 className="text-lg font-semibold" style={{ color: 'var(--header-text)' }}>AgenticHealth</h1>
              <p className="text-xs" style={{ color: 'var(--header-subtext)' }}>Dieta · Ejercicio · Asistente personal</p>
            </div>
            <div className="ml-auto flex items-center gap-2">
              <div className="relative">
                <button
                  type="button"
                  onClick={() => setThemeMenuOpen((prev) => !prev)}
                  className="rounded-full p-2 hover:opacity-70"
                  style={{ color: 'var(--header-text)' }}
                  aria-label="Cambiar tema"
                >
                  <Palette className="h-4 w-4" />
                </button>
                {themeMenuOpen && (
                  <>
                    <div className="fixed inset-0 z-40" onClick={() => setThemeMenuOpen(false)} aria-hidden="true" />
                    <div
                      className="absolute right-0 z-50 mt-2 w-48 overflow-hidden rounded-xl border shadow-lg"
                      style={{ backgroundColor: 'var(--card-bg)', borderColor: 'var(--card-border)' }}
                    >
                      {THEME_OPTIONS.map((option) => (
                        <button
                          key={option.value}
                          type="button"
                          onClick={() => {
                            setTheme(option.value);
                            setThemeMenuOpen(false);
                          }}
                          className="flex w-full items-center gap-2.5 px-3 py-2.5 text-sm hover:opacity-80"
                          style={{ color: 'var(--header-text)' }}
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
              <div className="rounded-full bg-purple-100 p-2 text-purple-700">
                <User className="h-4 w-4" />
              </div>
              <div className="text-sm" style={{ color: 'var(--header-text)' }}>
                <div className="font-medium">{user?.displayName ?? 'Usuario'}</div>
                <div className="text-xs" style={{ color: 'var(--header-subtext)' }}>{user?.email ?? ''}</div>
              </div>
              <button
                className="ml-2 rounded-full border px-3 py-1.5 text-sm hover:opacity-70"
                style={{ borderColor: 'var(--header-border)', color: 'var(--header-text)' }}
                onClick={logout}
              >
                Salir
              </button>
            </div>
          </header>

          <main className="min-h-0 flex-1 overflow-hidden">
            <Outlet />
          </main>
        </div>

        <FloatingChatWidget />
        <VoiceModal />
      </div>
    </ChatWidgetProvider>
  );
}

