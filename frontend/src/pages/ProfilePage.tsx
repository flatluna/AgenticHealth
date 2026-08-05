import { useEffect, useState } from 'react';
import { useAuth } from '../contexts/AuthContext';

export function ProfilePage() {
  const { user } = useAuth();
  const [bio, setBio] = useState('');
  const [city, setCity] = useState('');
  const [country, setCountry] = useState('');
  const [preferredFocus, setPreferredFocus] = useState('');
  const [timezone, setTimezone] = useState('UTC');
  const [wantsWellnessTips, setWantsWellnessTips] = useState(true);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    const loadProfile = async () => {
      try {
        const response = await fetch('/api/auth/profile', {
          headers: { 'x-msal-user': user?.azureObjectId ?? '' },
        });
        if (!response.ok) {
          return;
        }
        const data = await response.json();
        const profile = data.profile;
        setBio(profile?.bio ?? '');
        setCity(profile?.city ?? '');
        setCountry(profile?.country ?? '');
        setPreferredFocus(profile?.preferredFocus ?? '');
        setTimezone(profile?.timezone ?? 'UTC');
        setWantsWellnessTips(profile?.wantsWellnessTips ?? true);
      } catch {
        // ignore
      }
    };

    if (user?.azureObjectId) {
      loadProfile();
    }
  }, [user?.azureObjectId]);

  const handleSave = async () => {
    try {
      const response = await fetch('/api/auth/profile', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'x-msal-user': user?.azureObjectId ?? '' },
        body: JSON.stringify({ bio, city, country, preferredFocus, timezone, wantsWellnessTips }),
      });
      if (!response.ok) {
        throw new Error('No se pudo guardar el perfil');
      }
      setMessage('Perfil guardado correctamente.');
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'No se pudo guardar el perfil.');
    }
  };

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-6 p-6">
      <div className="rounded-3xl border border-[var(--card-border)] bg-[var(--card-bg)] p-6 shadow-sm">
        <h2 className="text-2xl font-semibold text-[var(--text-primary)]">Perfil simple</h2>
        <p className="mt-2 text-sm text-[var(--text-muted)]">Completa tus datos básicos para que AgenticHealth te conozca mejor.</p>
      </div>

      <div className="rounded-3xl border border-[var(--card-border)] bg-[var(--card-bg)] p-6 shadow-sm">
        <h3 className="text-sm font-semibold text-[var(--text-secondary)]">Tu cuenta</h3>
        <p className="mt-1 text-xs text-[var(--text-muted)]">Estos datos vienen de tu cuenta de Microsoft y no se pueden editar aquí.</p>
        <div className="mt-4 grid gap-4 md:grid-cols-2">
          <div className="text-sm text-[var(--text-secondary)]">
            <span className="mb-1 block">Nombre</span>
            <p className="rounded-xl border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2 text-[var(--text-primary)]">{user?.displayName || '—'}</p>
          </div>
          <div className="text-sm text-[var(--text-secondary)]">
            <span className="mb-1 block">Correo</span>
            <p className="rounded-xl border border-[var(--card-border)] bg-[var(--app-bg)] px-3 py-2 text-[var(--text-primary)]">{user?.email || '—'}</p>
          </div>
        </div>
      </div>

      <div className="rounded-3xl border border-[var(--card-border)] bg-[var(--card-bg)] p-6 shadow-sm">
        <div className="grid gap-4 md:grid-cols-2">
          <label className="text-sm text-[var(--text-secondary)]">
            <span className="mb-1 block">Bio</span>
            <textarea className="min-h-24 w-full rounded-xl border border-[var(--card-border)] px-3 py-2" value={bio} onChange={(e) => setBio(e.target.value)} />
          </label>
          <label className="text-sm text-[var(--text-secondary)]">
            <span className="mb-1 block">Ciudad</span>
            <input className="w-full rounded-xl border border-[var(--card-border)] px-3 py-2" value={city} onChange={(e) => setCity(e.target.value)} />
          </label>
          <label className="text-sm text-[var(--text-secondary)]">
            <span className="mb-1 block">País</span>
            <input className="w-full rounded-xl border border-[var(--card-border)] px-3 py-2" value={country} onChange={(e) => setCountry(e.target.value)} />
          </label>
          <label className="text-sm text-[var(--text-secondary)]">
            <span className="mb-1 block">Foco de bienestar</span>
            <input className="w-full rounded-xl border border-[var(--card-border)] px-3 py-2" value={preferredFocus} onChange={(e) => setPreferredFocus(e.target.value)} />
          </label>
          <label className="text-sm text-[var(--text-secondary)]">
            <span className="mb-1 block">Zona horaria</span>
            <input className="w-full rounded-xl border border-[var(--card-border)] px-3 py-2" value={timezone} onChange={(e) => setTimezone(e.target.value)} />
          </label>
        </div>
        <label className="mt-4 flex items-center gap-3 text-sm text-[var(--text-secondary)]">
          <input type="checkbox" checked={wantsWellnessTips} onChange={(e) => setWantsWellnessTips(e.target.checked)} />
          Recibir tips de bienestar
        </label>
        <button className="mt-6 rounded-xl bg-[var(--accent)] px-4 py-2 font-semibold text-white" onClick={handleSave}>Guardar perfil</button>
        {message && <p className="mt-3 text-sm text-[var(--text-muted)]">{message}</p>}
      </div>
    </div>
  );
}
