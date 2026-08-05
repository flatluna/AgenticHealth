import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { MsalProvider } from '@azure/msal-react'
import { msalInstance } from './auth/msalConfig'
import { AuthProvider } from './contexts/AuthContext'
import { ThemeProvider } from './contexts/ThemeContext'
import { ChatWidgetProvider } from './contexts/ChatWidgetContext'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <MsalProvider instance={msalInstance}>
      <ThemeProvider>
        <AuthProvider>
          <ChatWidgetProvider>
            <App />
          </ChatWidgetProvider>
        </AuthProvider>
      </ThemeProvider>
    </MsalProvider>
  </StrictMode>,
)
