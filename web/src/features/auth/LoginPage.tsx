import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Alert, Button, IconButton, InputAdornment, TextField, ToggleButton, ToggleButtonGroup } from '@mui/material';
import VisibilityOutlined from '@mui/icons-material/VisibilityOutlined';
import VisibilityOffOutlined from '@mui/icons-material/VisibilityOffOutlined';
import { api, errorMessage } from '@/lib/api';
import { useDocumentTitle } from '@/lib/hooks';
import { useNetworkTagline } from '@/components/VisitorCountry';
import { homeFor, useAuth } from '@/stores/auth';
import type { AuthResult } from '@/lib/types';
import { MOBILE_PATTERN, OTP_LENGTH, OtpCodeField, OtpSentNote, fieldError, useOtpChallenge } from './otp';

const schema = z.object({
  email: z.string().trim().email('Enter a valid email address'),
  password: z.string().min(1, 'Enter your password'),
});
type Form = z.infer<typeof schema>;
type Method = 'otp' | 'password';

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
  const [method, setMethod] = useState<Method>('otp');
  const [error, setError] = useState<string | null>(null);
  const [show, setShow] = useState(false);
  const { register, handleSubmit, setValue, formState: { errors, isSubmitting } } = useForm<Form>({ resolver: zodResolver(schema) });

  const signedIn = (result: AuthResult) => {
    setSession(result);
    navigate(params.get('returnUrl') ?? homeFor(result.user), { replace: true });
  };

  const onSubmit = async (values: Form) => {
    setError(null);
    try {
      const { data } = await api.post<AuthResult>('/api/auth/login', values);
      signedIn(data);
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <AuthShell title="Welcome back" subtitle="Sign in to manage bookings, leads and your business.">
      <ToggleButtonGroup exclusive fullWidth size="small" value={method} aria-label="Sign-in method" sx={{ mb: 3 }}
        onChange={(_, v: Method | null) => { if (v) { setMethod(v); setError(null); } }}>
        <ToggleButton value="otp">Mobile OTP</ToggleButton>
        <ToggleButton value="password">Email &amp; password</ToggleButton>
      </ToggleButtonGroup>

      {method === 'otp' ? <OtpSignIn onSignedIn={signedIn} /> : (
        <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
          {error && <Alert severity="error">{error}</Alert>}
          <TextField label="Email" type="email" autoComplete="email" autoFocus {...register('email')} error={!!errors.email} helperText={errors.email?.message} />
          <TextField label="Password" type={show ? 'text' : 'password'} autoComplete="current-password" {...register('password')}
            error={!!errors.password} helperText={errors.password?.message}
            slotProps={{ input: { endAdornment: <InputAdornment position="end"><IconButton onClick={() => setShow((s) => !s)} edge="end" aria-label={show ? 'Hide password' : 'Show password'}>{show ? <VisibilityOffOutlined /> : <VisibilityOutlined />}</IconButton></InputAdornment> } }} />
          <Button type="submit" variant="contained" size="large" fullWidth disabled={isSubmitting}>{isSubmitting ? 'Signing in…' : 'Sign in'}</Button>
        </form>
      )}
      <p className="mt-6 text-center text-sm text-muted">New to Calling Bell? <Link to="/register" className="font-semibold text-ink underline-offset-2 hover:underline">Create an account</Link></p>

      <div className="mt-8 rounded-xl border border-dashed border-line bg-canvas p-4">
        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Demo accounts · password CallingBell@2026</p>
        <div className="mt-2 flex flex-wrap gap-2">
          {demoAccounts.map((a) => (
            <Button key={a.email} size="small" variant="outlined" onClick={() => { setMethod('password'); setValue('email', a.email); setValue('password', 'CallingBell@2026'); }}>{a.label}</Button>
          ))}
        </div>
      </div>
    </AuthShell>
  );
}

/** Step 1: mobile number, send a code. Step 2: enter the code to sign in. */
function OtpSignIn({ onSignedIn }: { onSignedIn: (result: AuthResult) => void }) {
  const { challenge, sending, cooldown, send, reset } = useOtpChallenge('SignIn');
  const [phone, setPhone] = useState('');
  const [phoneError, setPhoneError] = useState<string>();
  const [code, setCode] = useState('');
  const [codeError, setCodeError] = useState<string>();
  const [error, setError] = useState<string | null>(null);
  const [verifying, setVerifying] = useState(false);

  const sendCode = async () => {
    setError(null);
    if (!MOBILE_PATTERN.test(phone.trim())) { setPhoneError('Enter a valid 10-digit mobile number'); return; }
    setPhoneError(undefined);
    try {
      await send(phone.trim());
      setCode('');
      setCodeError(undefined);
    } catch (e) {
      const msg = fieldError(e, 'phoneNumber');
      if (msg) setPhoneError(msg); else setError(errorMessage(e));
    }
  };

  const verify = async () => {
    setError(null);
    if (code.length !== OTP_LENGTH) { setCodeError(`Enter the ${OTP_LENGTH}-digit code`); return; }
    setVerifying(true);
    try {
      const { data } = await api.post<AuthResult>('/api/auth/otp/sign-in', { phoneNumber: phone.trim(), code });
      onSignedIn(data);
    } catch (e) {
      const msg = fieldError(e, 'code');
      if (msg) setCodeError(msg); else setError(errorMessage(e));
      setVerifying(false);
    }
  };

  return (
    <form noValidate className="space-y-4" onSubmit={(e) => { e.preventDefault(); void (challenge ? verify() : sendCode()); }}>
      {error && <Alert severity="error">{error}</Alert>}
      <TextField label="Mobile number" type="tel" autoComplete="tel" autoFocus={!challenge} placeholder="98765 43210" value={phone}
        disabled={!!challenge} onChange={(e) => { setPhone(e.target.value); setPhoneError(undefined); }}
        error={!!phoneError} helperText={phoneError ?? (challenge ? undefined : 'We will text you a one-time code')}
        slotProps={{ input: { startAdornment: <InputAdornment position="start">+91</InputAdornment> } }} />
      {challenge && (
        <>
          <OtpSentNote challenge={challenge} cooldown={cooldown} sending={sending} onResend={() => void sendCode()}
            onChangeNumber={() => { reset(); setCode(''); setCodeError(undefined); }} />
          <OtpCodeField value={code} onChange={(v) => { setCode(v); setCodeError(undefined); }} error={codeError} disabled={verifying} />
        </>
      )}
      <Button type="submit" variant="contained" size="large" fullWidth disabled={sending || verifying}>
        {challenge ? (verifying ? 'Signing in…' : 'Verify & sign in') : (sending ? 'Sending code…' : 'Send OTP')}
      </Button>
    </form>
  );
}

export function AuthShell({ title, subtitle, children }: { title: string; subtitle: string; children: React.ReactNode }) {
  const tagline = useNetworkTagline();
  return (
    <div className="container-page grid gap-10 py-10 md:py-16 lg:grid-cols-2 lg:items-center">
      <div className="hidden lg:block">
        <div className="rounded-2xl bg-navy p-10 text-white">
          <p className="text-sm font-semibold uppercase tracking-[0.12em] text-accent">Calling Bell</p>
          <h2 className="mt-3 text-3xl font-bold leading-tight">{tagline}</h2>
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
