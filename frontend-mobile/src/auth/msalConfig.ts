import { PublicClientApplication } from '@azure/msal-browser';

// Same Entra External ID (CIAM) app registration used by the main "suite" frontend -
// this mobile app is a separate deployment/domain but reuses the SAME client id, just
// registered with an additional SPA redirect URI for this domain.
const clientId = import.meta.env.VITE_AZURE_CLIENT_ID?.trim() ?? '25966c97-2755-49bf-abbd-b81d2206a42f';
const tenantId = import.meta.env.VITE_AZURE_TENANT_ID?.trim() ?? '0e9c8663-a4ff-440e-af94-be25e63a1a6a';
const configuredAuthority = import.meta.env.VITE_AZURE_AUTHORITY?.trim() ?? `https://twinetwork.ciamlogin.com/${tenantId}/v2.0/`;
const authority = configuredAuthority;

const redirectUri = import.meta.env.VITE_AZURE_REDIRECT_URI?.trim() ?? 'http://localhost:5176/suite/login';

const isAuthorityValid = authority.length > 0 && authority.includes('.ciamlogin.com/') && authority.includes('/v2.0/');

export const msalConfig = {
  auth: {
    clientId,
    authority,
    redirectUri,
    postLogoutRedirectUri: redirectUri,
    knownAuthorities: authority ? [new URL(authority).host] : [],
    navigateToLoginRequestUrl: false,
  },
  cache: {
    cacheLocation: 'localStorage' as const,
    storeAuthStateInCookie: false,
  },
};

export const loginRequest = {
  scopes: ['openid', 'profile', 'User.Read'],
};

export const isMsalConfigured = Boolean(clientId && isAuthorityValid);

export const msalInstance = new PublicClientApplication(msalConfig);
