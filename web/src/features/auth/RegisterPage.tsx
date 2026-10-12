import { useState } from 'react';
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router';
import { Alert, ToggleButton, ToggleButtonGroup } from '@mui/material';
import { useDocumentTitle } from '@/lib/hooks';
import { homeFor, isOwner, useAuth } from '@/stores/auth';
import type { AuthResult } from '@/lib/types';
import { AuthShell } from './LoginPage';
import { GoogleSignIn } from './GoogleSignIn';

type AccountType = 'Customer' | 'BusinessOwner';

/** "Join Calling Bell"'s place and category hint, kept for the owner's business setup. */
function setupPath(params: URLSearchParams) {
  const keep = new URLSearchParams([...params].filter(([k]) => k === 'source' || k === 'hint'));
  return `/owner/setup${keep.size ? `?${keep}` : ''}`;
}

/**
 * Create an account with Google. Customers are signed straight in; business owners get an owner account and continue to the business
 * setup wizard (`/owner/setup`).
 */
export default function RegisterPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const setSession = useAuth((s) => s.setSession);
  // Decide once, on arrival, so signing in here doesn't swap the page for a redirect before our own navigation.
  const [user] = useState(() => useAuth.getState().user);
  const type: AccountType = params.get('type') === 'business' ? 'BusinessOwner' : 'Customer';
  useDocumentTitle(type === 'BusinessOwner' ? 'List your business' : 'Create account');

  if (type === 'BusinessOwner' && isOwner(user)) return <Navigate to={setupPath(params)} replace />;

  const signedIn = (result: AuthResult) => {
    setSession(result);
    navigate(type === 'BusinessOwner' && isOwner(result.user) ? setupPath(params) : homeFor(result.user), { replace: true });
  };

  const choose = (next: AccountType) => {
    const p = new URLSearchParams(params);
    if (next === 'BusinessOwner') p.set('type', 'business'); else p.delete('type');
    navigate(`/register${p.size ? `?${p}` : ''}`, { replace: true });
  };

  return (
    <AuthShell title={type === 'BusinessOwner' ? 'List your business' : 'Create your account'}
      subtitle={type === 'BusinessOwner' ? 'Create your business account with Google, then set up your business profile.' : 'Save favourites, book services and track requests.'}>
      <ToggleButtonGroup exclusive fullWidth size="small" value={type} aria-label="Account type" sx={{ mb: 3 }}
        onChange={(_, v: AccountType | null) => { if (v) choose(v); }}>
        <ToggleButton value="Customer">I'm looking for services</ToggleButton>
        <ToggleButton value="BusinessOwner">I own a business</ToggleButton>
      </ToggleButtonGroup>
      {type === 'BusinessOwner' && user && !isOwner(user) && (
        <Alert severity="info" sx={{ mb: 3 }}>You're signed in as {user.displayName}. Continue with Google to add a business account.</Alert>
      )}
      {/* key: Google's button is set up once per account type. */}
      <GoogleSignIn key={type} context="signup" accountType={type} divider={false} onSignedIn={signedIn}
        unavailable={<Alert severity="warning">Sign-up is unavailable right now. Please try again later.</Alert>} />
      <p className="mt-4 text-center text-xs text-muted">We use your Google name, email and photo to set up your account. No password needed.</p>
      <p className="mt-6 text-center text-sm text-muted">Already have an account? <Link to="/login" className="font-semibold text-ink hover:underline">Sign in</Link></p>
    </AuthShell>
  );
}
