import { createBrowserRouter, Navigate, Outlet } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { AgentIcon } from './components/AgentIcon'
import { ChatPanel } from './components/ChatPanel'
import { VoiceModal } from './components/VoiceModal'
import { BottomNav } from './components/BottomNav'
import { SideNav } from './components/SideNav'
import { AppHeader } from './components/AppHeader'
import { VersionInfo } from './components/VersionInfo'
import { NutritionPage } from './pages/NutritionPage'
import { ProductsPage } from './pages/ProductsPage'
import { ExercisePage } from './pages/ExercisePage'
import { WeightPage } from './pages/WeightPage'
import { GoalsPage } from './pages/GoalsPage'
import { ProfilePage } from './pages/ProfilePage'
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
  const now = new Date();
  const dateTime = now.toLocaleDateString('es-MX', { 
    year: 'numeric', 
    month: '2-digit', 
    day: '2-digit' 
  }) + ' ' + now.toLocaleTimeString('es-MX', { 
    hour: '2-digit', 
    minute: '2-digit',
    second: '2-digit'
  });

  return (
    <div className="flex h-dvh flex-col items-center justify-center gap-6 bg-gradient-to-br from-purple-600 via-fuchsia-600 to-indigo-700 px-6 text-center text-white">
      <div className="absolute top-4 left-4 text-white/70 text-sm">
        {dateTime}
      </div>
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
      <div className="absolute bottom-4 right-4 text-white/60">
        <VersionInfo />
      </div>
    </div>
  );
}

function ChatScreen() {
  return (
    <div className="relative h-full w-full">
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

function AppShell() {
  const { loading, isAuthenticated } = useAuth();

  if (loading) {
    return <LoadingScreen />;
  }

  if (!isAuthenticated) {
    return <LoginScreen />;
  }

  return (
    <div className="flex h-dvh w-dvw overflow-hidden bg-[var(--app-bg)]">
      <SideNav />
      <div className="flex min-h-0 flex-1 flex-col w-full">
        <AppHeader />
        <div className="min-h-0 flex-1 w-full">
          <Outlet />
        </div>
        <BottomNav />
      </div>
    </div>
  );
}

const router = createBrowserRouter([
  {
    path: '/',
    element: <AppShell />,
    children: [
      { index: true, element: <ChatScreen /> },
      { path: 'nutricion', element: <NutritionPage /> },
      { path: 'productos', element: <ProductsPage /> },
      { path: 'ejercicio', element: <ExercisePage /> },
      { path: 'peso', element: <WeightPage /> },
      { path: 'metas', element: <GoalsPage /> },
      { path: 'perfil', element: <ProfilePage /> },
    ],
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
