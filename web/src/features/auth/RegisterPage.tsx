import { useState } from 'react';
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Alert, Button, MenuItem, TextField, ToggleButton, ToggleButtonGroup } from '@mui/material';
import { ApiError, api, errorMessage } from '@/lib/api';
import { useCities, useDocumentTitle } from '@/lib/hooks';
import { homeFor, isOwner, useAuth } from '@/stores/auth';
import { EmptyState } from '@/components/ui';
import BusinessWizard from '@/features/onboarding/BusinessWizard';
import type { AuthResult } from '@/lib/types';
import { AuthShell } from './LoginPage';
import { GoogleSignInButton } from './GoogleSignInButton';

const schema = z.object({
  accountType: z.enum(['Customer', 'BusinessOwner']),
  displayName: z.string().trim().min(2, 'Enter your full name').max(120),
  email: z.string().trim().email('Enter a valid email address'),
  phoneNumber: z.string().trim().regex(/^(\+91[\s-]?)?[6-9]\d{4}[\s-]?\d{5}$/, 'Enter a valid 10-digit mobile number'),
  citySlug: z.string().optional(),
  password: z.string().min(8, 'At least 8 characters').regex(/[A-Z]/, 'Include an uppercase letter').regex(/[a-z]/, 'Include a lowercase letter').regex(/\d/, 'Include a number'),
});
type Form = z.infer<typeof schema>;

export default function RegisterPage() {
  const [params] = useSearchParams();
  // Decide once, on arrival: the wizard signs the new owner in part-way through submitting (before payment and uploads),
  // and must not be swapped out for a redirect when that happens.
  const [user] = useState(() => useAuth.getState().user);
  if (params.get('type') !== 'business') return <CustomerRegister />;
  // Business sign-up: a multi-step wizard that creates the account and the business profile together.
  if (!user) return <BusinessWizard mode="register" />;
  if (isOwner(user)) return <Navigate to="/business/setup" replace />;
  return (
    <div className="container-page py-16">
      <EmptyState title="You're signed in with a customer account" message="Sign out and create a business account to list your business on Calling Bell."
        action={<Button component={Link} to="/" variant="outlined">Back to home</Button>} />
    </div>
  );
}

function CustomerRegister() {
  useDocumentTitle('Create account');
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const setSession = useAuth((s) => s.setSession);
  const { data: cities } = useCities();
  const [error, setError] = useState<string | null>(null);
  const { register, control, handleSubmit, setError: setFieldError, watch, formState: { errors, isSubmitting } } = useForm<Form>({
    resolver: zodResolver(schema),
    defaultValues: { accountType: params.get('type') === 'business' ? 'BusinessOwner' : 'Customer', citySlug: '' },
  });
  const type = watch('accountType');

  const onSubmit = async (values: Form) => {
    setError(null);
    try {
      const { data } = await api.post<AuthResult>('/api/auth/register', { ...values, citySlug: values.citySlug || null });
      setSession(data);
      navigate(homeFor(data.user), { replace: true });
    } catch (e) {
      if (e instanceof ApiError && e.errors) {
        Object.entries(e.errors).forEach(([field, msgs]) => setFieldError(field as keyof Form, { message: msgs[0] }));
      }
      setError(errorMessage(e));
    }
  };

  return (
    <AuthShell title="Create your account" subtitle={type === 'BusinessOwner' ? 'List your business and start receiving leads today.' : 'Save favourites, book services and track requests.'}>
      <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
        {error && <Alert severity="error">{error}</Alert>}
        <Controller control={control} name="accountType" render={({ field }) => (
          <ToggleButtonGroup exclusive fullWidth size="small" value={field.value} aria-label="Account type"
            onChange={(_, v) => { if (v === 'BusinessOwner') navigate('/register?type=business'); else if (v) field.onChange(v); }}>
            <ToggleButton value="Customer">I'm looking for services</ToggleButton>
            <ToggleButton value="BusinessOwner">I own a business</ToggleButton>
          </ToggleButtonGroup>
        )} />
        <TextField label="Full name" autoComplete="name" {...register('displayName')} error={!!errors.displayName} helperText={errors.displayName?.message} />
        <TextField label="Email" type="email" autoComplete="email" {...register('email')} error={!!errors.email} helperText={errors.email?.message} />
        <TextField label="Mobile number" autoComplete="tel" placeholder="98765 43210" {...register('phoneNumber')} error={!!errors.phoneNumber} helperText={errors.phoneNumber?.message} />
        <Controller control={control} name="citySlug" render={({ field }) => (
          <TextField select label="City" {...field}>
            <MenuItem value="">Select later</MenuItem>
            {cities?.map((c) => <MenuItem key={c.slug} value={c.slug}>{c.name}</MenuItem>)}
          </TextField>
        )} />
        <TextField label="Password" type="password" autoComplete="new-password" {...register('password')} error={!!errors.password}
          helperText={errors.password?.message ?? 'At least 8 characters with upper, lower case and a number'} />
        <Button type="submit" variant="contained" size="large" fullWidth disabled={isSubmitting}>{isSubmitting ? 'Creating account…' : 'Create account'}</Button>
      </form>
      <div className="mt-4">
        <GoogleSignInButton label="signup_with" accountType={type} onSignedIn={(r) => navigate(homeFor(r.user), { replace: true })} />
      </div>
      <p className="mt-6 text-center text-sm text-muted">Already have an account? <Link to="/login" className="font-semibold text-ink hover:underline">Sign in</Link></p>
    </AuthShell>
  );
}
