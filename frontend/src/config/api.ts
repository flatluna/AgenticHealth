const defaultApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim() || '/api';

export const apiBaseUrl = defaultApiBaseUrl.replace(/\/$/, '');
