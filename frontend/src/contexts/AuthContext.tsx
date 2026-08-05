import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import { isMsalConfigured, loginRequest } from '../auth/msalConfig';

interface AuthUser {
  id: number;
  azureObjectId: string;
  email: string;
  displayName: string;
  preferredLanguage: string;
  subscriptionStatus: string;
  profile?: {
    id: number;
    bio?: string | null;
    goal?: string | null;
    city?: string | null;
    country?: string | null;
    preferredFocus?: string | null;
    timezone?: string | null;
    wantsWellnessTips?: boolean | null;
  } | null;
}

interface AuthContextValue {
  user: AuthUser | null;
  isAuthenticated: boolean;
  loading: boolean;
  login: () => Promise<void>;
  logout: () => Promise<void>;
  refreshUser: () => Promise<void>;
  subscribe: (payload: { azureObjectId: string; email: string; displayName: string; preferredLanguage?: string; timezone?: string }) => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

const STORAGE_KEY = 'agentichealth-auth-user';

function toAuthUser(account: { homeAccountId: string; username: string; name?: string | null }): AuthUser {
  return {
    id: 0,
    azureObjectId: account.homeAccountId,
    email: account.username,
    displayName: account.name ?? account.username,
    preferredLanguage: 'en',
    subscriptionStatus: 'active',
  };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const { instance, inProgress } = useMsal();
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);

  const persistUser = (nextUser: AuthUser) => {
    setUser(nextUser);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(nextUser));
  };

  const registerAuthenticatedUser = async (account: { homeAccountId: string; username: string; name?: string | null }) => {
    const baseUser = toAuthUser(account);
    persistUser(baseUser);

    try {
      const response = await fetch('/api/auth/subscribe', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'x-msal-user': baseUser.azureObjectId },
        body: JSON.stringify({
          azureObjectId: baseUser.azureObjectId,
          email: baseUser.email,
          displayName: baseUser.displayName,
          preferredLanguage: baseUser.preferredLanguage,
          timezone: Intl.DateTimeFormat().resolvedOptions().timeZone ?? 'UTC',
        }),
      });

      if (!response.ok) {
        return;
      }

      const data = await response.json();
      persistUser({ ...baseUser, id: data.userId });
    } catch {
      // Keep the locally authenticated user even if the backend is temporarily unavailable.
    }
  };

  const login = async () => {
    if (!isMsalConfigured) {
      throw new Error('Configura VITE_AZURE_AUTHORITY o VITE_AZURE_TENANT_DOMAIN + VITE_AZURE_USER_FLOW con la autoridad de tu user flow de Entra External ID / CIAM.');
    }

    await instance.loginRedirect(loginRequest);
  };

  const logout = async () => {
    await instance.logoutRedirect();
    setUser(null);
    localStorage.removeItem(STORAGE_KEY);
  };

  const refreshUser = async () => {
    try {
      const response = await fetch('/api/auth/me', {
        headers: { 'x-msal-user': user?.azureObjectId ?? '' },
      });
      if (!response.ok) {
        setUser(null);
        localStorage.removeItem(STORAGE_KEY);
        return;
      }
      const data = await response.json();
      const nextUser = data.user as AuthUser;
      setUser(nextUser);
      localStorage.setItem(STORAGE_KEY, JSON.stringify(nextUser));
    } catch {
      setUser(null);
      localStorage.removeItem(STORAGE_KEY);
    }
  };

  const subscribe = async (payload: { azureObjectId: string; email: string; displayName: string; preferredLanguage?: string; timezone?: string }) => {
    const response = await fetch('/api/auth/subscribe', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'x-msal-user': payload.azureObjectId },
      body: JSON.stringify(payload),
    });
    if (!response.ok) {
      throw new Error('No se pudo crear la suscripción');
    }
    const data = await response.json();
    const nextUser: AuthUser = {
      id: data.userId,
      azureObjectId: payload.azureObjectId,
      email: payload.email,
      displayName: payload.displayName,
      preferredLanguage: payload.preferredLanguage ?? 'en',
      subscriptionStatus: 'active',
    };
    setUser(nextUser);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(nextUser));
  };

  useEffect(() => {
    if (inProgress !== InteractionStatus.None) {
      return;
    }

    const activeAccount = instance.getActiveAccount() ?? instance.getAllAccounts()[0] ?? null;
    if (activeAccount) {
      void registerAuthenticatedUser(activeAccount).finally(() => setLoading(false));
      return;
    }

    const cached = localStorage.getItem(STORAGE_KEY);
    if (cached) {
      try {
        setUser(JSON.parse(cached) as AuthUser);
      } catch {
        localStorage.removeItem(STORAGE_KEY);
      }
    }
    setLoading(false);
  }, [instance, inProgress]);

  const value = useMemo(() => ({
    user,
    isAuthenticated: Boolean(user),
    loading,
    login,
    logout,
    refreshUser,
    subscribe,
  }), [loading, user]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within AuthProvider');
  }
  return context;
}
