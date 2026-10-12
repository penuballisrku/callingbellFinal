import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Alert } from '@mui/material';
import { api, errorMessage } from '@/lib/api';
import type { AuthResult } from '@/lib/types';
import { useCity } from '@/stores/city';

/** Google Identity Services, the script behind the official "Sign in with Google" button. */
interface GoogleAccountsId {
  initialize(options: { client_id: string; callback: (response: { credential: string }) => void; ux_mode?: 'popup'; context?: string; itp_support?: boolean; auto_select?: boolean }): void;
  renderButton(parent: HTMLElement, options: { type?: string; theme?: string; size?: string; text?: string; shape?: string; logo_alignment?: string; width?: number }): void;
}
declare global { interface Window { google?: { accounts: { id: GoogleAccountsId } } } }

const SCRIPT = 'https://accounts.google.com/gsi/client';
let loading: Promise<void> | null = null;
function loadGoogle(): Promise<void> {
  if (window.google?.accounts?.id) return Promise.resolve();
  loading ??= new Promise<void>((resolve, reject) => {
    const s = document.createElement('script');
    s.src = SCRIPT;
    s.async = true;
    s.onload = () => resolve();
    s.onerror = () => { loading = null; reject(new Error('Google sign-in could not be loaded.')); };
    document.head.appendChild(s);
  });
  return loading;
}

/**
 * Google keeps one configuration per page and warns when initialize() runs again, so it is set up once per client id; its callback
 * goes to the button currently on screen (`onCredential`). The sign-in/sign-up wording comes from renderButton's `text`.
 */
let initializedFor: string | null = null;
let onCredential: ((credential: string) => void) | null = null;
function initializeGoogle(clientId: string) {
  if (initializedFor === clientId || !window.google) return;
  initializedFor = clientId;
  window.google.accounts.id.initialize({
    client_id: clientId, ux_mode: 'popup', itp_support: true, auto_select: false,
    callback: ({ credential }) => onCredential?.(credential),
  });
}

/** Google never accepts a bare IP address as a sign-in origin; on the local network the same PC is reachable by name through nip.io. */
const IPV4 = /^\d{1,3}(\.\d{1,3}){3}$/;
function nameForIpHost(): string | null {
  const { hostname, protocol, port, pathname, search } = window.location;
  if (!IPV4.test(hostname)) return null;
  return `${protocol}//${hostname.replaceAll('.', '-')}.nip.io${port ? `:${port}` : ''}${pathname}${search}`;
}

/**
 * The official "Continue with Google" button (Google's own, as its branding rules require), optionally with an "or" divider below. Google
 * checks the account and returns an ID token; the API verifies it and signs the person in, creating an account of `accountType` the first
 * time. When Google sign-in isn't configured on the server it shows `unavailable` (nothing by default).
 */
export function GoogleSignIn({ context, onSignedIn, accountType = 'Customer', divider = true, unavailable = null }: {
  context: 'signin' | 'signup'; onSignedIn: (result: AuthResult) => void; accountType?: 'Customer' | 'BusinessOwner';
  divider?: boolean; unavailable?: React.ReactNode;
}) {
  const { data, isPending } = useQuery({ queryKey: ['auth', 'providers'], queryFn: () => api.get<{ googleClientId: string | null }>('/api/auth/providers'), staleTime: 10 * 60_000 });
  const clientId = data?.googleClientId;
  const citySlug = useCity((s) => s.citySlug);
  const box = useRef<HTMLDivElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  // The latest callback for Google's (one-time) initialisation.
  const signedIn = useRef(onSignedIn);
  signedIn.current = onSignedIn;
  const city = useRef(citySlug);
  city.current = citySlug;
  const type = useRef(accountType);
  type.current = accountType;

  const [byName] = useState(nameForIpHost);

  useEffect(() => {
    if (!clientId || byName) return;
    let cancelled = false;
    const handle = async (credential: string) => {
      setError(null);
      setBusy(true);
      try {
        const { data: result } = await api.post<AuthResult>('/api/auth/google', { credential, citySlug: city.current, accountType: type.current });
        signedIn.current(result);
      } catch (e) {
        setError(errorMessage(e));
      } finally {
        setBusy(false);
      }
    };
    loadGoogle().then(() => {
      if (cancelled || !box.current || !window.google) return;
      initializeGoogle(clientId);
      onCredential = handle;
      const dark = document.documentElement.dataset.theme === 'dark';
      window.google.accounts.id.renderButton(box.current, {
        type: 'standard', theme: dark ? 'filled_black' : 'outline', size: 'large', shape: 'pill', logo_alignment: 'center',
        text: context === 'signup' ? 'signup_with' : 'continue_with', width: Math.min(400, Math.round(box.current.getBoundingClientRect().width)),
      });
    }).catch((e: Error) => !cancelled && setError(e.message));
    return () => {
      cancelled = true;
      if (onCredential === handle) onCredential = null;
    };
  }, [clientId, context, byName]);

  if (!isPending && !clientId) return <>{unavailable}</>;
  if (clientId && byName) {
    return (
      <div className={divider ? 'mb-5' : undefined}>
        <Alert severity="info">Google sign-in doesn't work on an IP address. <a href={byName} className="font-semibold underline">Open this page by name</a> to continue with Google.</Alert>
      </div>
    );
  }
  return (
    <div className={divider ? 'mb-5' : undefined}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {/* Height reserved for Google's button (44px), also while the settings load, so nothing moves when it appears. color-scheme
          light matches Google's iframe: in the dark theme a mismatch makes the browser paint a white box behind the button. */}
      <div ref={box} style={{ colorScheme: 'light' }} className={`flex h-11 w-full justify-center ${busy ? 'pointer-events-none opacity-60' : ''}`} aria-busy={busy || isPending} />
      {divider && (
        <div className="mt-5 flex items-center gap-3 text-xs font-medium uppercase tracking-wide text-faint" role="separator">
          <span className="h-px flex-1 bg-line" />or<span className="h-px flex-1 bg-line" />
        </div>
      )}
    </div>
  );
}
