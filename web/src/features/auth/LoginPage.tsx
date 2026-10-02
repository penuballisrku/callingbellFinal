import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Alert, Button, IconButton, InputAdornment, TextField } from '@mui/material';
import VisibilityOutlined from '@mui/icons-material/VisibilityOutlined';
import VisibilityOffOutlined from '@mui/icons-material/VisibilityOffOutlined';
import { api, errorMessage } from '@/lib/api';
import { useDocumentTitle } from '@/lib/hooks';
import { homeFor, useAuth } from '@/stores/auth';
import type { AuthResult } from '@/lib/types';
import { GoogleSignInButton } from './GoogleSignInButton';

const schema = z.object({
  email: z.string().trim().email('Enter a valid email address'),
  password: z.string().min(1, 'Enter your password'),
});
type Form = z.infer<typeof schema>;

const demoAccounts = [
  { label: 'Customer', email: 'aarav.sharma@demo.callingbell.in' },
  { label: 'Business owner', email: 'venkatesh.goud@demo.callingbell.in' },
  { label: 'Administrator', email: 'admin@demo.callingbell.in' },
];

export default function LoginPage() {
  useDocumentTitle('Sign in');
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const setSession = useAuth((s) => s.setSession);
  const [error, setError] = useState<string | null>(null);
  const [show, setShow] = useState(false);
  const { register, handleSubmit, setValue, formState: { errors, isSubmitting } } = useForm<Form>({ resolver: zodResolver(schema) });

  const onSubmit = async (values: Form) => {
    setError(null);
    try {
      const { data } = await api.post<AuthResult>('/api/auth/login', values);
      setSession(data);
      navigate(params.get('returnUrl') ?? homeFor(data.user), { replace: true });
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <AuthShell title="Welcome back" subtitle="Sign in to manage bookings, leads and your business.">
      <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
        {error && <Alert severity="error">{error}</Alert>}
        <TextField label="Email" type="email" autoComplete="email" autoFocus {...register('email')} error={!!errors.email} helperText={errors.email?.message} />
        <TextField label="Password" type={show ? 'text' : 'password'} autoComplete="current-password" {...register('password')}
          error={!!errors.password} helperText={errors.password?.message}
          slotProps={{ input: { endAdornment: <InputAdornment position="end"><IconButton onClick={() => setShow((s) => !s)} edge="end" aria-label={show ? 'Hide password' : 'Show password'}>{show ? <VisibilityOffOutlined /> : <VisibilityOutlined />}</IconButton></InputAdornment> } }} />
        <Button type="submit" variant="contained" size="large" fullWidth disabled={isSubmitting}>{isSubmitting ? 'Signing in…' : 'Sign in'}</Button>
      </form>
      <div className="mt-4">
        <GoogleSignInButton label="signin_with" onSignedIn={(r) => navigate(params.get('returnUrl') ?? homeFor(r.user), { replace: true })} />
      </div>
      <p className="mt-6 text-center text-sm text-muted">New to Calling Bell? <Link to="/register" className="font-semibold text-ink underline-offset-2 hover:underline">Create an account</Link></p>

      <div className="mt-8 rounded-xl border border-dashed border-line bg-canvas p-4">
        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Demo accounts · password CallingBell@2026</p>
        <div className="mt-2 flex flex-wrap gap-2">
          {demoAccounts.map((a) => (
            <Button key={a.email} size="small" variant="outlined" onClick={() => { setValue('email', a.email); setValue('password', 'CallingBell@2026'); }}>{a.label}</Button>
          ))}
        </div>
      </div>
    </AuthShell>
  );
}

export function AuthShell({ title, subtitle, children }: { title: string; subtitle: string; children: React.ReactNode }) {
  return (
    <div className="container-page grid gap-10 py-10 md:py-16 lg:grid-cols-2 lg:items-center">
      <div className="hidden lg:block">
        <div className="rounded-2xl bg-navy p-10 text-white">
          <p className="text-sm font-semibold uppercase tracking-[0.12em] text-accent">Calling Bell</p>
          <h2 className="mt-3 text-3xl font-bold leading-tight">India's real-time local business network.</h2>
          <ul className="mt-6 space-y-3 text-[15px] text-on-navy-muted">
            <li>• See who's available right now - call, chat or book instantly</li>
            <li>• Verified professionals with genuine customer reviews</li>
            <li>• Businesses get real-time leads and a booking calendar</li>
          </ul>
        </div>
      </div>
      <div className="mx-auto w-full max-w-md">
        <div className="card p-6 md:p-8">
          <h1 className="text-2xl font-bold tracking-tight">{title}</h1>
          <p className="mb-6 mt-1 text-sm text-muted">{subtitle}</p>
          {children}
        </div>
      </div>
    </div>
  );
}
