import { createBrowserRouter, Navigate } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { AppLayout } from './components/AppLayout'
import { HomePage } from './pages/HomePage'
import { LandingPage } from './pages/LandingPage'
import { ProfilePage } from './pages/ProfilePage'
import { FoodPage } from './pages/FoodPage'
import { ExercisesPage } from './pages/ExercisesPage'
import { WeightPage } from './pages/WeightPage'
import { GoalsPage } from './pages/GoalsPage'
import { useAuth } from './contexts/AuthContext'

function LoginRedirectPage() {
  const { loading, isAuthenticated } = useAuth();

  if (loading) {
    return <div className="flex min-h-screen items-center justify-center bg-slate-950 text-white">Cargando…</div>;
  }

  return <Navigate to={isAuthenticated ? '/app' : '/'} replace />;
}

function LandingOrApp() {
  const { loading, isAuthenticated } = useAuth();

  if (loading) {
    return <div className="flex min-h-screen items-center justify-center bg-slate-950 text-white">Cargando…</div>;
  }

  // Authenticated users should stay in the app; only unauthenticated visitors see the landing page.
  return isAuthenticated ? <Navigate to="/app" replace /> : <LandingPage />;
}

// Guards every /app/* route - without this, logging out (or navigating directly to an
// /app/... URL) left protected pages fully visible with no session.
function RequireAuth({ children }: { children: React.ReactNode }) {
  const { loading, isAuthenticated } = useAuth();

  if (loading) {
    return <div className="flex min-h-screen items-center justify-center bg-slate-950 text-white">Cargando…</div>;
  }

  if (!isAuthenticated) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}

const router = createBrowserRouter([
  {
    path: '/',
    element: <LandingOrApp />,
  },
  {
    // Must match the SPA redirect URI registered in the Entra app registration.
    path: '/suite/login',
    element: <LoginRedirectPage />,
  },
  {
    path: '/app',
    element: <Navigate to="/app/" replace />,
  },
  {
    path: '/app',
    element: (
      <RequireAuth>
        <AppLayout />
      </RequireAuth>
    ),
    children: [
      { index: true, element: <HomePage /> },
      { path: 'nutricion', element: <FoodPage /> },
      { path: 'ejercicios', element: <ExercisesPage /> },
      { path: 'peso', element: <WeightPage /> },
      { path: 'objetivos', element: <GoalsPage /> },
      { path: 'perfil', element: <ProfilePage /> },
    ],
  },
])

function App() {
  const { loading } = useAuth();

  if (loading) {
    return <div className="flex min-h-screen items-center justify-center bg-slate-950 text-white">Cargando…</div>;
  }

  return <RouterProvider router={router} />;
}

export default App
