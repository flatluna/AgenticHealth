import type { AxiosInstance } from 'axios';

const defaultApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim() || '/api';

export const apiBaseUrl = defaultApiBaseUrl.replace(/\/$/, '');

const AUTH_STORAGE_KEY = 'agentichealth-auth-user';

/**
 * Attaches the signed-in account's identity (the "x-msal-user" header the backend uses to
 * resolve/create an isolated Person row per account - see AuthContext.tsx/AuthFunctions.cs)
 * to every request made by the given axios client. Without this, the backend has no way to
 * tell which account is calling and falls back to one shared legacy record for everyone -
 * this is what makes every page (Nutrición, Peso, Ejercicio, Objetivos, chat, voz) correctly
 * scoped to the logged-in user instead of leaking data across accounts.
 */
export function attachAuthHeader(client: AxiosInstance): void {
  client.interceptors.request.use((config) => {
    try {
      const raw = localStorage.getItem(AUTH_STORAGE_KEY);
      const azureObjectId = raw ? (JSON.parse(raw) as { azureObjectId?: string }).azureObjectId : null;
      if (azureObjectId) {
        config.headers.set('x-msal-user', azureObjectId);
      }
    } catch {
      // Ignore malformed/absent local storage - request proceeds without the header.
    }
    return config;
  });
}
