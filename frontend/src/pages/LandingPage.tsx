import { useState } from 'react';
import { useAuth } from '../contexts/AuthContext';

const features = [
  {
    title: 'AgenticHealth Assistant',
    description: 'Habla con un asistente de salud que te acompaña en nutrición, ejercicio y objetivos.',
  },
  {
    title: 'Plan de metas',
    description: 'Crea planes accionables y revisa tu progreso con check-ins simples.',
  },
  {
    title: 'Perfil personalizado',
    description: 'Guarda tu idioma, foco de bienestar y tus preferencias para una experiencia más útil.',
  },
];

export function LandingPage() {
  const { login, subscribe, isAuthenticated, user } = useAuth();
  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
  const [language, setLanguage] = useState<'es' | 'en'>('es');
  const [message, setMessage] = useState<string | null>(null);

  const handleLogin = async () => {
    try {
      await login();
    } catch (error) {
      const detail = error instanceof Error ? error.message : 'No se pudo iniciar sesión.';
      setMessage(
        `${language === 'es' ? 'No se pudo iniciar sesión con Microsoft.' : 'Microsoft sign-in could not be completed.'} ${detail}`,
      );
    }
  };

  const handleSubscribe = async () => {
    try {
      await subscribe({
        azureObjectId: `local-${email}`,
        email,
        displayName: name || email,
        preferredLanguage: language,
        timezone: 'UTC',
      });
      setMessage('Suscripción creada. Ahora puedes continuar al panel.');
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'No se pudo completar la suscripción.');
    }
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-purple-950 via-purple-900 to-slate-900 text-white">
      <div className="mx-auto flex max-w-7xl flex-col gap-10 px-6 py-16 lg:px-10">
        <header className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <p className="text-sm uppercase tracking-[0.3em] text-purple-200">AgenticHealth</p>
            <h1 className="text-4xl font-semibold sm:text-5xl">Tu bienestar, guiado por agentes inteligentes</h1>
          </div>
          <div className="flex gap-3">
            <button className="rounded-full border border-white/20 px-4 py-2 text-sm" onClick={() => setLanguage('es')}>ES</button>
            <button className="rounded-full border border-white/20 px-4 py-2 text-sm" onClick={() => setLanguage('en')}>EN</button>
          </div>
        </header>

        <section className="grid gap-8 lg:grid-cols-[1.2fr_0.8fr]">
          <div className="space-y-6 rounded-3xl border border-white/10 bg-white/10 p-8 backdrop-blur">
            <p className="text-lg text-purple-100">
              {language === 'es'
                ? 'Descubre una experiencia de salud impulsada por IA para nutrirte mejor, moverte con intención y alcanzar metas reales.'
                : 'Discover an AI-powered health experience to nourish better, move with intention, and achieve real goals.'}
            </p>
            <div className="grid gap-4 md:grid-cols-3">
              {features.map((feature) => (
                <div key={feature.title} className="rounded-2xl bg-slate-950/30 p-4">
                  <h3 className="font-semibold">{feature.title}</h3>
                  <p className="mt-2 text-sm text-slate-300">{feature.description}</p>
                </div>
              ))}
            </div>
          </div>

          <div className="rounded-3xl border border-white/10 bg-slate-950/40 p-8 shadow-2xl">
            <h2 className="text-2xl font-semibold">{language === 'es' ? 'Inicia sesión o suscríbete' : 'Log in or subscribe'}</h2>
            <p className="mt-2 text-sm text-slate-300">
              {language === 'es'
                ? 'Usa una cuenta Microsoft del tenant correcto o una cuenta invitada al directorio. Las cuentas externas como Outlook.com no funcionarán si el tenant no las admite para esta app.'
                : 'Use a Microsoft account from the correct tenant or an invited directory account. External accounts such as Outlook.com will not work if the tenant does not allow them for this app.'}
            </p>
            <div className="mt-6 space-y-3">
              <input className="w-full rounded-xl border border-white/10 bg-white/10 px-4 py-3 text-sm outline-none" placeholder={language === 'es' ? 'Tu correo' : 'Your email'} value={email} onChange={(e) => setEmail(e.target.value)} />
              <input className="w-full rounded-xl border border-white/10 bg-white/10 px-4 py-3 text-sm outline-none" placeholder={language === 'es' ? 'Tu nombre' : 'Your name'} value={name} onChange={(e) => setName(e.target.value)} />
              <button className="w-full rounded-xl bg-purple-500 px-4 py-3 font-semibold text-white" onClick={handleSubscribe}>
                {language === 'es' ? 'Suscribirme' : 'Subscribe'}
              </button>
              <button className="w-full rounded-xl border border-purple-400 px-4 py-3 font-semibold text-purple-200" onClick={handleLogin}>
                {language === 'es' ? 'Entrar con Microsoft' : 'Sign in with Microsoft'}
              </button>
            </div>
            {message && <p className="mt-4 text-sm text-purple-200">{message}</p>}
            {isAuthenticated && user && <p className="mt-4 text-sm text-emerald-300">{language === 'es' ? 'Sesión activa' : 'Session active'}: {user.displayName}</p>}
          </div>
        </section>
      </div>
    </div>
  );
}
