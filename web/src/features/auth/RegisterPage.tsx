import { useState } from 'react';
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Alert, Button, IconButton, InputAdornment, TextField, ToggleButton, ToggleButtonGroup } from '@mui/material';
import VisibilityOutlined from '@mui/icons-material/VisibilityOutlined';
import VisibilityOffOutlined from '@mui/icons-material/VisibilityOffOutlined';
import { ApiError, api, errorMessage } from '@/lib/api';
import { useDocumentTitle } from '@/lib/hooks';
import { homeFor, isOwner, useAuth } from '@/stores/auth';
import { useCity } from '@/stores/city';
import type { AuthResult } from '@/lib/types';
import { AuthShell } from './LoginPage';
import { GoogleSignIn } from './GoogleSignIn';

type AccountType = 'Customer' | 'BusinessOwner';

/** The same rules the API applies (RegisterWithPasswordValidator), so most mistakes show before submitting. */
const passwordSchema = z.object({
  displayName: z.string().trim().min(2, 'Enter your full name').max(120),
  email: z.string().trim().email('Enter a valid email address').max(256),
  password: z.string().min(8, 'Use at least 8 characters').max(128)
    .regex(/[A-Z]/, 'Password must contain an uppercase letter')
    .regex(/[a-z]/, 'Password must contain a lowercase letter')
    .regex(/[0-9]/, 'Password must contain a number'),
  confirmPassword: z.string().min(1, 'Confirm your password'),
}).refine((v) => v.password === v.confirmPassword, { path: ['confirmPassword'], message: "Passwords don't match" });
type PasswordForm = z.infer<typeof passwordSchema>;

/** "Join CallInBell"'s place and category hint, kept for the owner's business setup. */
function setupPath(params: URLSearchParams) {
  const keep = new URLSearchParams([...params].filter(([k]) => k === 'source' || k === 'hint'));
  return `/owner/setup${keep.size ? `?${keep}` : ''}`;
}

/**
 * Create an account with Google or with email and password. Customers are signed straight in; business owners get an owner account and
 * continue to the business setup wizard (`/owner/setup`).
 */
export default function RegisterPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const setSession = useAuth((s) => s.setSession);
  // Decide once, on arrival, so signing in here doesn't swap the page for a redirect before our own navigation.
  const [user] = useState(() => useAuth.getState().user);
  // A business sign-up that came back without the owner role (e.g. an API older than this page): said, never a silent trip home.
  const [noOwnerAccess, setNoOwnerAccess] = useState<string | null>(null);
  const type: AccountType = params.get('type') === 'business' ? 'BusinessOwner' : 'Customer';
  useDocumentTitle(type === 'BusinessOwner' ? 'List your business' : 'Create account');

  if (type === 'BusinessOwner' && isOwner(user)) return <Navigate to={setupPath(params)} replace />;

  const signedIn = (result: AuthResult) => {
    setSession(result);
    if (type === 'BusinessOwner' && !isOwner(result.user)) { setNoOwnerAccess(result.user.email); return; }
    navigate(type === 'BusinessOwner' ? setupPath(params) : homeFor(result.user), { replace: true });
  };

  const choose = (next: AccountType) => {
    const p = new URLSearchParams(params);
    if (next === 'BusinessOwner') p.set('type', 'business'); else p.delete('type');
    navigate(`/register${p.size ? `?${p}` : ''}`, { replace: true });
  };

  return (
    <AuthShell title={type === 'BusinessOwner' ? 'List your business' : 'Create your account'}
      subtitle={type === 'BusinessOwner' ? 'Create your business account, then set up your business profile.' : 'Save favourites, book services and track requests.'}>
      <ToggleButtonGroup exclusive fullWidth size="small" value={type} aria-label="Account type" sx={{ mb: 3 }}
        onChange={(_, v: AccountType | null) => { if (v) choose(v); }}>
        <ToggleButton value="Customer">I'm looking for services</ToggleButton>
        <ToggleButton value="BusinessOwner">I own a business</ToggleButton>
      </ToggleButtonGroup>
      {noOwnerAccess && type === 'BusinessOwner' && (
        <Alert severity="error" sx={{ mb: 3 }}>
          You're signed in as {noOwnerAccess}, but the account was created without business access, so the business setup can't open.
          Please contact support to switch it to a business account.
        </Alert>
      )}
      {type === 'BusinessOwner' && user && !isOwner(user) && !noOwnerAccess && (
        <Alert severity="info" sx={{ mb: 3 }}>You're signed in as {user.displayName}. Continue with Google to add a business account to it, or create a separate business account below.</Alert>
      )}
      {/* key: Google's button is set up once per account type. */}
      <GoogleSignIn key={type} context="signup" accountType={type} onSignedIn={signedIn} />
      <PasswordSignUp accountType={type} onSignedIn={signedIn} />
      <p className="mt-6 text-center text-sm text-muted">Already have an account? <Link to="/login" className="font-semibold text-ink hover:underline">Sign in</Link></p>
    </AuthShell>
  );
}

/** Sign-up with full name, email, password and password confirmation, as a customer or a business owner. */
function PasswordSignUp({ accountType, onSignedIn }: { accountType: AccountType; onSignedIn: (result: AuthResult) => void }) {
  const citySlug = useCity((s) => s.citySlug);
  const [error, setError] = useState<string | null>(null);
  const [show, setShow] = useState(false);
  const { register, handleSubmit, setError: setFieldError, formState: { errors, isSubmitting } } = useForm<PasswordForm>({
    resolver: zodResolver(passwordSchema), mode: 'onTouched',
  });

  const onSubmit = async (values: PasswordForm) => {
    setError(null);
    try {
      const { data } = await api.post<AuthResult>('/api/auth/register-with-password', { ...values, citySlug: citySlug || null, accountType });
      onSignedIn(data);
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) { setFieldError('email', { message: e.message }, { shouldFocus: true }); return; }
      if (e instanceof ApiError && e.errors) {
        const fields = Object.keys(passwordSchema.innerType().shape);
        const unmatched = Object.entries(e.errors).filter(([field, msgs]) => {
          if (!fields.includes(field)) return true;
          setFieldError(field as keyof PasswordForm, { message: msgs[0] });
          return false;
        });
        if (unmatched.length) setError(unmatched[0][1][0]);
        return;
      }
      setError(errorMessage(e));
    }
  };

  const visibility = (
    <InputAdornment position="end">
      <IconButton onClick={() => setShow((v) => !v)} edge="end" aria-label={show ? 'Hide passwords' : 'Show passwords'}>
        {show ? <VisibilityOffOutlined /> : <VisibilityOutlined />}
      </IconButton>
    </InputAdornment>
  );

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
      {error && <Alert severity="error">{error}</Alert>}
      <TextField label="Full name" autoComplete="name" {...register('displayName')} error={!!errors.displayName} helperText={errors.displayName?.message} />
      <TextField label="Email" type="email" autoComplete="email" {...register('email')} error={!!errors.email} helperText={errors.email?.message} />
      <TextField label="Password" type={show ? 'text' : 'password'} autoComplete="new-password" {...register('password')}
        error={!!errors.password} helperText={errors.password?.message ?? 'At least 8 characters, with upper and lower case letters and a number'}
        slotProps={{ input: { endAdornment: visibility } }} />
      <TextField label="Confirm password" type={show ? 'text' : 'password'} autoComplete="new-password" {...register('confirmPassword')}
        error={!!errors.confirmPassword} helperText={errors.confirmPassword?.message} />
      <Button type="submit" variant="contained" size="large" fullWidth disabled={isSubmitting}>{isSubmitting ? 'Creating account…' : accountType === 'BusinessOwner' ? 'Create business account' : 'Create account'}</Button>
    </form>
  );
}
