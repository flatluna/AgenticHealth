import { NavLink, useLocation, useNavigate } from 'react-router';
import { DOMAINS } from '../config/domains';

/** Fixed-position bottom nav for the mobile app shell: a domain switcher row (Salud,
 * Finanzas, Educación, ...) on top of the active domain's feature tabs, respecting the
 * safe-area inset so it doesn't collide with the home indicator on iOS. */
export function BottomNav() {
  const location = useLocation();
  const navigate = useNavigate();

  const activeDomain =
    DOMAINS.find((domain) =>
      domain.tabs.some((tab) => (tab.end ? location.pathname === tab.path : location.pathname.startsWith(tab.path))),
    ) ?? DOMAINS[0];

  return (
    <div className="shrink-0 border-t border-[var(--card-border)] bg-[var(--card-bg)] pb-[env(safe-area-inset-bottom)] lg:hidden">
      <div className="flex gap-2 overflow-x-auto px-3 pt-2 pb-1.5">
        {DOMAINS.map((domain) => (
          <button
            key={domain.id}
            type="button"
            disabled={!domain.enabled}
            onClick={() => navigate(domain.tabs[0].path)}
            className={`flex shrink-0 items-center gap-1 rounded-full px-3 py-1 text-[11px] font-medium transition-colors disabled:opacity-40 ${
              domain.id === activeDomain.id
                ? `${domain.accentBg} ${domain.accentText}`
                : 'bg-[var(--app-bg)] text-[var(--text-muted)]'
            }`}
          >
            <domain.icon className="h-3.5 w-3.5" />
            {domain.label}
            {!domain.enabled && ' · Próximamente'}
          </button>
        ))}
      </div>
      <nav className="flex">
        {activeDomain.tabs.map(({ path, label, icon: Icon, end }) => (
          <NavLink
            key={path}
            to={path}
            end={end}
            className={({ isActive }) =>
              `flex flex-1 flex-col items-center gap-0.5 py-2 text-[10px] font-medium transition-colors ${
                isActive ? 'text-[var(--accent-text)]' : 'text-[var(--text-muted)]'
              }`
            }
          >
            <Icon className="h-5 w-5" />
            {label}
          </NavLink>
        ))}
      </nav>
    </div>
  );
}
