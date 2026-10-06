import { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Button, InputAdornment, Skeleton, TextField } from '@mui/material';
import StorefrontOutlined from '@mui/icons-material/StorefrontOutlined';
import PermMediaOutlined from '@mui/icons-material/PermMediaOutlined';
import WorkspacePremiumOutlined from '@mui/icons-material/WorkspacePremiumOutlined';
import AddBusinessOutlined from '@mui/icons-material/AddBusinessOutlined';
import ChevronRightRounded from '@mui/icons-material/ChevronRightRounded';
import { useSnackbar } from 'notistack';
import { ApiError, api, errorMessage } from '@/lib/api';
import { date, dateTime } from '@/lib/format';
import { useDocumentTitle } from '@/lib/hooks';
import type { AccountSettings, CurrentUser } from '@/lib/types';
import { useAuth } from '@/stores/auth';
import { ConfirmDialog, ErrorState, PageHeader, Panel } from '@/components/ui';

const accountSchema = z.object({
  displayName: z.string().trim().min(2, 'Enter your full name').max(120),
  phoneNumber: z.string().trim().regex(/^(\+91[\s-]?)?[6-9]\d{4}[\s-]?\d{5}$/, 'Enter a valid 10-digit mobile number'),
});
const passwordSchema = z.object({
  currentPassword: z.string(),
  newPassword: z.string().min(8, 'At least 8 characters').regex(/[A-Z]/, 'Include an uppercase letter').regex(/[a-z]/, 'Include a lowercase letter').regex(/\d/, 'Include a number'),
  confirmPassword: z.string(),
}).refine((v) => v.newPassword === v.confirmPassword, { path: ['confirmPassword'], message: 'Passwords do not match' });

export default function OwnerSettings() {
  useDocumentTitle('Settings');
  const { data, isLoading, isError, refetch } = useQuery({ queryKey: ['me', 'account'], queryFn: () => api.get<AccountSettings>('/api/me/account') });
  if (isError) return <ErrorState onRetry={() => refetch()} />;
  return (
    <>
      <PageHeader title="Settings" subtitle="Manage your account, security and business preferences." crumbs={[{ label: 'Dashboard', to: '/business' }, { label: 'Settings' }]} />
      {isLoading || !data ? <Skeleton variant="rounded" height={520} /> : (
        <div className="grid grid-cols-1 gap-6 xl:grid-cols-[1fr_360px]">
          <div className="min-w-0 space-y-6">
            <AccountDetails data={data} />
            <PasswordPanel hasPassword={data.hasPassword} />
            <SessionsPanel data={data} />
          </div>
          <aside className="min-w-0 space-y-6">
            <Panel title="Business settings" noPad>
              <ul className="divide-y divide-line">
                {[
                  ['/business/profile', 'Business profile', 'Description, contact, hours and social links', <StorefrontOutlined key="p" />],
                  ['/business/media', 'Photos & videos', 'Logo, cover image, gallery and videos', <PermMediaOutlined key="m" />],
                  ['/business/plan', 'Plan & billing', 'Your plan, invoices and upgrades', <WorkspacePremiumOutlined key="b" />],
                  ['/business/setup', 'Add another business', 'List a second location or brand', <AddBusinessOutlined key="a" />],
                ].map(([to, label, hint, icon]) => (
                  <li key={to as string}>
                    <Link to={to as string} className="flex items-center gap-3 px-5 py-3.5 transition-colors hover:bg-subtle">
                      <span className="text-muted">{icon}</span>
                      <span className="min-w-0 flex-1"><span className="block text-sm font-semibold">{label}</span><span className="block truncate text-xs text-muted">{hint}</span></span>
                      <ChevronRightRounded className="text-faint" />
                    </Link>
                  </li>
                ))}
              </ul>
            </Panel>
            <Panel title="Account">
              <dl className="space-y-2 text-sm">
                <div className="flex justify-between gap-3"><dt className="text-muted">Member since</dt><dd>{date(data.createdOn)}</dd></div>
                <div className="flex justify-between gap-3"><dt className="text-muted">Last sign-in</dt><dd>{data.lastLoginOn ? dateTime(data.lastLoginOn) : '-'}</dd></div>
                <div className="flex justify-between gap-3"><dt className="text-muted">Sign-in methods</dt><dd>{[data.phoneNumber && 'Mobile OTP', data.hasPassword && 'Password'].filter(Boolean).join(' & ') || '-'}</dd></div>
              </dl>
            </Panel>
          </aside>
        </div>
      )}
    </>
  );
}

function AccountDetails({ data }: { data: AccountSettings }) {
  const { enqueueSnackbar } = useSnackbar();
  const queryClient = useQueryClient();
  const { register, handleSubmit, reset, formState: { errors, isSubmitting, isDirty } } = useForm<z.infer<typeof accountSchema>>({ resolver: zodResolver(accountSchema) });
  useEffect(() => { reset({ displayName: data.displayName, phoneNumber: (data.phoneNumber ?? '').replace(/^\+91\s?/, '') }); }, [data, reset]);
  const save = async (v: z.infer<typeof accountSchema>) => {
    try {
      const res = await api.put<CurrentUser>('/api/me/account', v);
      useAuth.setState({ user: res.data });
      enqueueSnackbar('Account details saved', { variant: 'success' });
      void queryClient.invalidateQueries({ queryKey: ['me', 'account'] });
    } catch (e) { enqueueSnackbar(errorMessage(e), { variant: 'error' }); }
  };
  return (
    <Panel title="Account details" subtitle="The person who manages this business on Calling Bell">
      <form onSubmit={handleSubmit(save)} noValidate className="grid gap-4 sm:grid-cols-2">
        <TextField label="Full name" autoComplete="name" {...register('displayName')} error={!!errors.displayName} helperText={errors.displayName?.message} />
        <TextField label="Mobile number" autoComplete="tel" {...register('phoneNumber')} error={!!errors.phoneNumber} helperText={errors.phoneNumber?.message}
          slotProps={{ input: { startAdornment: <InputAdornment position="start">+91</InputAdornment> } }} />
        <div className="sm:col-span-2"><TextField label="Email" value={data.email} disabled helperText="Your sign-in email. Contact support to change it." /></div>
        <div className="sm:col-span-2 flex justify-end"><Button type="submit" variant="contained" disabled={!isDirty || isSubmitting}>{isSubmitting ? 'Saving…' : 'Save changes'}</Button></div>
      </form>
    </Panel>
  );
}

function PasswordPanel({ hasPassword }: { hasPassword: boolean }) {
  const { enqueueSnackbar } = useSnackbar();
  const queryClient = useQueryClient();
  const { register, handleSubmit, reset, setError, formState: { errors, isSubmitting } } = useForm<z.infer<typeof passwordSchema>>({
    resolver: zodResolver(passwordSchema), defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
  });
  const save = async (v: z.infer<typeof passwordSchema>) => {
    if (hasPassword && !v.currentPassword) { setError('currentPassword', { message: 'Enter your current password' }); return; }
    try {
      await api.post('/api/me/account/password', { currentPassword: hasPassword ? v.currentPassword : null, newPassword: v.newPassword });
      enqueueSnackbar(hasPassword ? 'Password updated' : 'Password set. You can now sign in with your email.', { variant: 'success' });
      reset();
      void queryClient.invalidateQueries({ queryKey: ['me', 'account'] });
    } catch (e) {
      if (e instanceof ApiError && e.errors) Object.entries(e.errors).forEach(([k, m]) => setError(k as 'currentPassword' | 'newPassword', { message: m[0] }));
      else enqueueSnackbar(errorMessage(e), { variant: 'error' });
    }
  };
  return (
    <Panel title={hasPassword ? 'Change password' : 'Set a password'} subtitle={hasPassword ? 'Use at least 8 characters with upper and lower case letters and a number' : 'You sign in with a one-time code sent to your mobile. Add a password to also sign in with your email.'}>
      <form onSubmit={handleSubmit(save)} noValidate className="grid gap-4 sm:grid-cols-2">
        {hasPassword && <div className="sm:col-span-2 sm:max-w-[calc(50%-8px)]"><TextField label="Current password" type="password" autoComplete="current-password" {...register('currentPassword')} error={!!errors.currentPassword} helperText={errors.currentPassword?.message} /></div>}
        <TextField label="New password" type="password" autoComplete="new-password" {...register('newPassword')} error={!!errors.newPassword} helperText={errors.newPassword?.message} />
        <TextField label="Confirm new password" type="password" autoComplete="new-password" {...register('confirmPassword')} error={!!errors.confirmPassword} helperText={errors.confirmPassword?.message} />
        <div className="sm:col-span-2 flex justify-end"><Button type="submit" variant="contained" disabled={isSubmitting}>{isSubmitting ? 'Updating…' : hasPassword ? 'Update password' : 'Set password'}</Button></div>
      </form>
    </Panel>
  );
}

function SessionsPanel({ data }: { data: AccountSettings }) {
  const { enqueueSnackbar } = useSnackbar();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { refreshToken, clear } = useAuth();
  const [confirm, setConfirm] = useState<'others' | 'all' | null>(null);
  const [busy, setBusy] = useState(false);
  const others = Math.max(0, data.activeSessions - 1);

  const run = async () => {
    setBusy(true);
    try {
      if (confirm === 'others') {
        const res = await api.post<number>('/api/me/account/sessions/revoke', { currentRefreshToken: refreshToken });
        enqueueSnackbar(res.message, { variant: 'success' });
        void queryClient.invalidateQueries({ queryKey: ['me', 'account'] });
      } else {
        if (refreshToken) await api.post('/api/auth/logout', { refreshToken }).catch(() => undefined);
        clear();
        queryClient.clear();
        navigate('/login', { replace: true });
      }
      setConfirm(null);
    } catch (e) { enqueueSnackbar(errorMessage(e), { variant: 'error' }); } finally { setBusy(false); }
  };

  return (
    <Panel title="Sessions & sign-out">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="text-sm">
          <div className="font-semibold">{data.activeSessions} active session{data.activeSessions === 1 ? '' : 's'}</div>
          <div className="text-muted">{others ? `You're signed in on ${others} other device${others === 1 ? '' : 's'} or browser${others === 1 ? '' : 's'}.` : 'Only this device is signed in.'}</div>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="outlined" disabled={!others} onClick={() => setConfirm('others')}>Sign out other devices</Button>
          <Button color="error" onClick={() => setConfirm('all')}>Sign out</Button>
        </div>
      </div>
      <ConfirmDialog open={!!confirm} loading={busy} destructive={confirm === 'all'} confirmLabel={confirm === 'others' ? 'Sign out others' : 'Sign out'}
        title={confirm === 'others' ? 'Sign out of other devices?' : 'Sign out of Calling Bell?'}
        message={confirm === 'others' ? 'Anyone signed in to your account elsewhere will need to sign in again. This device stays signed in.' : 'You will need to sign in again to manage your business.'}
        onConfirm={() => void run()} onClose={() => setConfirm(null)} />
    </Panel>
  );
}
