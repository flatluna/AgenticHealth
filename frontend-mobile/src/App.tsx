import { createBrowserRouter, Navigate } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { AgentIcon } from './components/AgentIcon'
import { ChatPanel } from './components/ChatPanel'
import { VoiceModal } from './components/VoiceModal'
import { useAuth } from './contexts/AuthContext'

function LoadingScreen() {
  return (
    <div className="flex h-dvh items-center justify-center bg-[var(--app-bg)] text-[var(--text-primary)]">
      Cargando…
    </div>
  );
}

function LoginScreen() {
  const { login } = useAuth();

  return (
    <div className="flex h-dvh flex-col items-center justify-center gap-6 bg-gradient-to-br from-purple-600 via-fuchsia-600 to-indigo-700 px-6 text-center text-white">
      <AgentIcon className="h-16 w-16" />
      <div>
        <h1 className="text-2xl font-bold">Mi Agente de Salud</h1>
        <p className="mt-2 text-sm text-white/80">Consulta, habla y registra tus comidas y ejercicios donde sea.</p>
      </div>
      <button
        type="button"
        onClick={() => void login()}
        className="rounded-full bg-white px-6 py-3 text-sm font-semibold text-purple-700 shadow-lg transition-transform hover:scale-105"
      >
        Iniciar sesión
      </button>
    </div>
  );
}

function ChatScreen() {
  return (
    <div className="relative h-dvh w-full">
      <ChatPanel />
      <VoiceModal />
    </div>
  );
}

function LoginRedirectPage() {
  const { loading, isAuthenticated } = useAuth();

  if (loading) {
    return <LoadingScreen />;
  }

  return <Navigate to={isAuthenticated ? '/' : '/'} replace />;
}

function Root() {
  const { loading, isAuthenticated } = useAuth();

  if (loading) {
    return <LoadingScreen />;
  }

  return isAuthenticated ? <ChatScreen /> : <LoginScreen />;
}

const router = createBrowserRouter([
  {
    path: '/',
    element: <Root />,
  },
  {
    // Must match the SPA redirect URI registered in the Entra app registration.
    path: '/suite/login',
    element: <LoginRedirectPage />,
  },
])

function App() {
  const { loading } = useAuth();

  if (loading) {
    return <LoadingScreen />;
  }

  return <RouterProvider router={router} />;
}

export default App
