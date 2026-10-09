import { useState } from 'react';
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Alert, Button, InputAdornment, TextField, ToggleButton, ToggleButtonGroup } from '@mui/material';
import { ApiError, api, errorMessage } from '@/lib/api';
import { useDocumentTitle } from '@/lib/hooks';
import { CityAutocomplete } from '@/components/CityAutocomplete';
import { homeFor, isOwner, useAuth } from '@/stores/auth';
import { EmptyState } from '@/components/ui';
import BusinessWizard from '@/features/onboarding/BusinessWizard';
import type { AuthResult } from '@/lib/types';
import { AuthShell } from './LoginPage';
import { MOBILE_PATTERN, PhoneVerification, phoneDigits } from './otp';

const schema = z.object({
  accountType: z.enum(['Customer', 'BusinessOwner']),
  displayName: z.string().trim().min(2, 'Enter your full name').max(120),
  email: z.string().trim().email('Enter a valid email address'),
  phoneNumber: z.string().trim().regex(MOBILE_PATTERN, 'Enter a valid 10-digit mobile number'),
  citySlug: z.string().optional(),
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
  if (isOwner(user)) {
    // Keep "Join Calling Bell"'s place and category hint for the owner's business setup.
    const keep = new URLSearchParams([...params].filter(([k]) => k === 'source' || k === 'hint'));
    return <Navigate to={`/owner/setup${keep.size ? `?${keep}` : ''}`} replace />;
  }
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
  const [error, setError] = useState<string | null>(null);
  const { register, control, handleSubmit, setError: setFieldError, clearErrors, trigger, watch, formState: { errors, isSubmitting } } = useForm<Form>({
    resolver: zodResolver(schema),
    defaultValues: { accountType: params.get('type') === 'business' ? 'BusinessOwner' : 'Customer', citySlug: '' },
  });
  const type = watch('accountType');
  const phoneNumber = watch('phoneNumber') ?? '';
  // The OTP verification token is only valid for the number it was issued to.
  const [verification, setVerification] = useState<{ token: string; digits: string } | null>(null);
  const verified = !!verification && verification.digits === phoneDigits(phoneNumber);

  const onSubmit = async (values: Form) => {
    setError(null);
    if (!verified) { setFieldError('phoneNumber', { message: 'Verify your mobile number to continue' }, { shouldFocus: true }); return; }
    try {
      const { data } = await api.post<AuthResult>('/api/auth/register', {
        ...values, citySlug: values.citySlug || null, phoneVerificationToken: verification.token,
      });
      setSession(data);
      navigate(homeFor(data.user), { replace: true });
    } catch (e) {
      if (e instanceof ApiError && e.errors) {
        Object.entries(e.errors).forEach(([field, msgs]) => setFieldError(field as keyof Form, { message: msgs[0] }));
        if (e.errors.phoneNumber) setVerification(null); // expired or already used: verify again
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
        <TextField label="Mobile number" type="tel" autoComplete="tel" placeholder="98765 43210" {...register('phoneNumber')} error={!!errors.phoneNumber}
          helperText={errors.phoneNumber?.message ?? 'You will sign in with a one-time code sent to this number'}
          slotProps={{ input: { startAdornment: <InputAdornment position="start">+91</InputAdornment> } }} />
        <PhoneVerification phoneNumber={phoneNumber} verified={verified} validatePhone={() => trigger('phoneNumber')}
          onVerified={(token, phone) => { setVerification({ token, digits: phoneDigits(phone) }); clearErrors('phoneNumber'); }}
          onPhoneError={(message) => setFieldError('phoneNumber', { message }, { shouldFocus: true })} />
        <Controller control={control} name="citySlug" render={({ field }) => (
          <CityAutocomplete value={field.value || null} onChange={(slug) => field.onChange(slug ?? '')} label="City" placeholder="Search your city"
            size="medium" helperText="Optional. You can choose it later." />
        )} />
        <Button type="submit" variant="contained" size="large" fullWidth disabled={isSubmitting}>{isSubmitting ? 'Creating account…' : 'Create account'}</Button>
      </form>
      <p className="mt-6 text-center text-sm text-muted">Already have an account? <Link to="/login" className="font-semibold text-ink hover:underline">Sign in</Link></p>
    </AuthShell>
  );
}
