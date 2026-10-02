import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Alert, Divider } from '@mui/material';
import { api, errorMessage } from '@/lib/api';
import type { AuthResult } from '@/lib/types';
import { useAuth } from '@/stores/auth';

interface GoogleCredentialResponse { credential: string }
interface GoogleIdentity {
  accounts: {
    id: {
      initialize: (config: { client_id: string; callback: (r: GoogleCredentialResponse) => void; ux_mode?: 'popup'; cancel_on_tap_outside?: boolean }) => void;
      renderButton: (el: HTMLElement, options: Record<string, unknown>) => void;
    };
  };
}
declare global { interface Window { google?: GoogleIdentity } }

let gisScript: Promise<void> | null = null;
function loadGoogleIdentity(): Promise<void> {
  gisScript ??= new Promise<void>((resolve, reject) => {
    if (window.google?.accounts?.id) { resolve(); return; }
    const script = document.createElement('script');
    script.src = 'https://accounts.google.com/gsi/client';
    script.async = true;
    script.defer = true;
    script.onload = () => resolve();
    script.onerror = () => { gisScript = null; reject(new Error('Could not load Google sign-in. Check your connection.')); };
    document.head.appendChild(script);
  });
  return gisScript;
}

/**
 * "Continue with Google" using Google Identity Services. The ID token is verified by the API,
 * which signs the user in (creating the account on first use) and returns Calling Bell tokens.
 * Renders nothing when Google sign-in is not configured on the server.
 */
export function GoogleSignInButton({ accountType = 'Customer', onSignedIn, label = 'continue_with' }: {
  accountType?: 'Customer' | 'BusinessOwner';
  onSignedIn: (result: AuthResult) => void;
  label?: 'continue_with' | 'signup_with' | 'signin_with';
}) {
  const setSession = useAuth((s) => s.setSession);
  const container = useRef<HTMLDivElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  // Keep the latest props for the GIS callback, which is registered once.
  const latest = useRef({ accountType, onSignedIn });
  latest.current = { accountType, onSignedIn };

  const { data, isLoading } = useQuery({
    queryKey: ['auth', 'providers'],
    queryFn: () => api.get<{ googleClientId?: string | null }>('/api/auth/providers'),
    staleTime: Infinity,
  });
  const clientId = data?.googleClientId;
  const [rendered, setRendered] = useState(false);

  useEffect(() => {
    if (!clientId || !container.current) return;
    let cancelled = false;
    loadGoogleIdentity()
      .then(() => {
        if (cancelled || !container.current || !window.google) return;
        window.google.accounts.id.initialize({
          client_id: clientId,
          ux_mode: 'popup',
          cancel_on_tap_outside: true,
          callback: async ({ credential }) => {
            setError(null);
            setBusy(true);
            try {
              const { data: result } = await api.post<AuthResult>('/api/auth/google', { idToken: credential, accountType: latest.current.accountType });
              setSession(result);
              latest.current.onSignedIn(result);
            } catch (e) {
              setError(errorMessage(e));
            } finally {
              setBusy(false);
            }
          },
        });
        window.google.accounts.id.renderButton(container.current, {
          type: 'standard', theme: document.documentElement.dataset.theme === 'dark' ? 'filled_black' : 'outline', size: 'large', shape: 'rectangular', text: label, logo_alignment: 'center',
          width: Math.min(400, Math.max(200, container.current.offsetWidth)),
        });
        setRendered(true);
      })
      .catch((e: Error) => !cancelled && setError(e.message));
    return () => { cancelled = true; };
  }, [clientId, label, setSession]);

  const text = { continue_with: 'Continue with Google', signup_with: 'Sign up with Google', signin_with: 'Sign in with Google' }[label];

  return (
    <div className="space-y-4">
      <Divider sx={{ fontSize: 12, color: 'text.secondary' }}>or</Divider>
      {error && <Alert severity="error">{error}</Alert>}
      {/* Fixed height avoids layout shift while Google renders its button. */}
      <div className="relative h-11 w-full">
        <div ref={container} className={`flex h-full w-full justify-center ${busy ? 'pointer-events-none opacity-60' : ''}`} aria-busy={busy} />
        {!rendered && (
          // Shown while Google's script loads, and when Google sign-in is not configured on the server.
          <button type="button" disabled={isLoading || !!clientId}
            onClick={() => setError('Google sign-in is not available yet. Please sign in with your email and password.')}
            className="absolute inset-0 flex items-center justify-center gap-3 rounded-sm border border-[#DADCE0] bg-white text-sm font-medium text-[#3C4043] transition-colors hover:bg-[#F8FAFF] dark:border-[#8E918F] dark:bg-[#131314] dark:text-[#E3E3E3] dark:hover:bg-[#1F1F20] disabled:cursor-default">
            <GoogleLogo />
            {text}
          </button>
        )}
      </div>
    </div>
  );
}

const GoogleLogo = () => (
  <svg width="18" height="18" viewBox="0 0 48 48" aria-hidden>
    <path fill="#EA4335" d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z" />
    <path fill="#4285F4" d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z" />
    <path fill="#FBBC05" d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z" />
    <path fill="#34A853" d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z" />
  </svg>
);
