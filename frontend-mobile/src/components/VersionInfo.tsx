/**
 * Displays the build date/version so users can verify they have the latest version.
 * Build date is injected at compile time by vite.config.ts
 */
export function VersionInfo() {
  const buildDate = typeof __BUILD_DATE__ !== 'undefined' ? __BUILD_DATE__ : new Date().toISOString();
  const version = '1.10';

  const formatDate = (isoString: string) => {
    try {
      return new Date(isoString).toLocaleString('es-ES', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
      });
    } catch {
      return isoString;
    }
  };

  return (
    <div className="flex items-center gap-1.5 whitespace-nowrap">
      <span className="text-[var(--text-muted)]">v{version}</span>
      <span className="text-[var(--text-muted)]">•</span>
      <span className="text-[var(--text-muted)]">{formatDate(buildDate)}</span>
    </div>
  );
}
