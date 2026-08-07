import { NavLink, useLocation, useNavigate } from 'react-router';
import { DOMAINS } from '../config/domains';

/** Left-hand nav for wide/desktop viewports: same domains/tabs as BottomNav, but stacked
 * vertically since a sidebar has height to spare and no room to fit horizontal chips. */
export function SideNav() {
  const location = useLocation();
  const navigate = useNavigate();

  const activeDomain =
    DOMAINS.find((domain) =>
      domain.tabs.some((tab) => (tab.end ? location.pathname === tab.path : location.pathname.startsWith(tab.path))),
    ) ?? DOMAINS[0];

  return (
    <aside className="hidden w-56 shrink-0 flex-col overflow-y-auto border-r border-[var(--card-border)] bg-[var(--card-bg)] py-4 lg:flex">
      <nav className="flex flex-col gap-1 px-3">
        {DOMAINS.map((domain) => {
          const isActiveDomain = domain.id === activeDomain.id;
          return (
            <div key={domain.id}>
              <button
                type="button"
                disabled={!domain.enabled}
                onClick={() => navigate(domain.tabs[0].path)}
                className={`flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm font-semibold transition-colors disabled:opacity-40 ${
                  isActiveDomain
                    ? `${domain.accentBg} ${domain.accentText}`
                    : 'text-[var(--text-muted)] hover:bg-[var(--app-bg)]'
                }`}
              >
                <span className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-md ${domain.accentBg} ${domain.accentText}`}>
                  <domain.icon className="h-4 w-4" />
                </span>
                <span className="flex-1 text-left">{domain.label}</span>
                {!domain.enabled && <span className="text-[10px] font-normal">Próximamente</span>}
              </button>
              {isActiveDomain && (
                <div className="mt-1 flex flex-col gap-0.5 pl-2">
                  {domain.tabs.map(({ path, label, icon: Icon, end }) => (
                    <NavLink
                      key={path}
                      to={path}
                      end={end}
                      className={({ isActive }) =>
                        `flex items-center gap-2 rounded-lg px-3 py-1.5 text-sm font-medium transition-colors ${
                          isActive ? domain.accentText : 'text-[var(--text-muted)] hover:bg-[var(--app-bg)]'
                        }`
                      }
                    >
                      <Icon className="h-4 w-4" />
                      {label}
                    </NavLink>
                  ))}
                </div>
              )}
            </div>
          );
        })}
      </nav>
    </aside>
  );
}
