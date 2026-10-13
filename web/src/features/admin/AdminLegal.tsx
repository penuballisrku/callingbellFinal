import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSnackbar } from 'notistack';
import { Alert, Button, Skeleton, TextField } from '@mui/material';
import OpenInNewRounded from '@mui/icons-material/OpenInNewRounded';
import { api, ApiError, errorMessage } from '@/lib/api';
import { POLICIES, POLICY_KEYS } from '@/lib/legal';
import { ErrorState, PageHeader, Panel } from '@/components/ui';

interface LegalSetting { key: string; label: string; help?: string | null; value: string }

/**
 * Admin > Legal & policies: the company details filled into the Terms and Conditions, Privacy, Cancellation and Refund pages
 * (legal name, registered address, customer care, Grievance Officer, jurisdiction, effective date). The policy text itself is in the
 * database (database/scripts/36_LegalPolicies.sql); these values are inserted wherever it names them.
 */
export function AdminLegal() {
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const key = ['admin', 'legal-settings'];
  const { data, isLoading, isError, refetch } = useQuery({ queryKey: key, queryFn: () => api.get<LegalSetting[]>('/api/admin/legal-settings') });
  const [values, setValues] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  useEffect(() => { if (data) setValues(Object.fromEntries(data.map((s) => [s.key, s.value]))); }, [data]);

  const save = useMutation({
    mutationFn: () => api.put<LegalSetting[]>('/api/admin/legal-settings', values).then((r) => r.data),
    onSuccess: (saved) => {
      queryClient.setQueryData(key, saved);
      // The policy pages show the new details at once.
      void queryClient.invalidateQueries({ queryKey: ['content'] });
      setErrors({});
      enqueueSnackbar('Legal details saved', { variant: 'success' });
    },
    onError: (e) => {
      if (e instanceof ApiError && e.errors) {
        // Keyed by setting ("supportEmail"); matched without regard to case.
        setErrors(Object.fromEntries(Object.entries(e.errors).map(([k, v]) => [k.split('.').pop()!.toLowerCase(), v[0] ?? 'Invalid'])));
      }
      enqueueSnackbar(errorMessage(e), { variant: 'error' });
    },
  });

  const required = (data ?? []).filter((s) => s.help?.startsWith('Required'));
  const missing = required.filter((s) => !values[s.key]?.trim());
  const dirty = !!data && data.some((s) => (values[s.key] ?? '') !== s.value);

  return (
    <div className="space-y-6">
      <PageHeader title="Legal & policies" subtitle="Company and grievance details shown in the Terms and Conditions, Privacy, Cancellation and Refund policies." />
      {isError ? <ErrorState onRetry={() => refetch()} /> : (
        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_300px] lg:items-start">
          <Panel title="Company details" subtitle="Filled into the policies wherever they name the company, customer care or Grievance Officer.">
            {isLoading ? <div className="space-y-4">{[0, 1, 2, 3, 4].map((i) => <Skeleton key={i} variant="rounded" height={56} />)}</div> : (
              <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); save.mutate(); }}>
                {missing.length > 0 && (
                  <Alert severity="warning">
                    Still needed before you rely on the policies: {missing.map((s) => s.label).join(', ')}. Until then the policies point people to the
                    Contact support page instead.
                  </Alert>
                )}
                {data!.map((s) => (
                  <TextField key={s.key} label={s.label} value={values[s.key] ?? ''} fullWidth
                    multiline={s.key === 'RegisteredAddress'} minRows={s.key === 'RegisteredAddress' ? 2 : undefined}
                    type={s.key.endsWith('Email') ? 'email' : 'text'}
                    onChange={(e) => setValues((v) => ({ ...v, [s.key]: e.target.value }))}
                    error={!!errors[s.key.toLowerCase()]} helperText={errors[s.key.toLowerCase()] ?? s.help ?? undefined}
                    slotProps={{ htmlInput: { maxLength: 300 } }} />
                ))}
                <div className="flex justify-end gap-2">
                  <Button type="button" color="inherit" disabled={!dirty || save.isPending}
                    onClick={() => { setValues(Object.fromEntries(data!.map((s) => [s.key, s.value]))); setErrors({}); }}>Reset</Button>
                  <Button type="submit" variant="contained" disabled={!dirty || save.isPending}>{save.isPending ? 'Saving…' : 'Save details'}</Button>
                </div>
              </form>
            )}
          </Panel>
          <Panel title="Policy pages" subtitle="Public pages, linked from the footer and the checkout.">
            <ul className="space-y-1">
              {POLICY_KEYS.map((k) => (
                <li key={k}>
                  <Link to={POLICIES[k].path} target="_blank" rel="noopener" className="flex items-center justify-between rounded-lg px-2 py-2 text-sm hover:bg-subtle">
                    {POLICIES[k].label}<OpenInNewRounded sx={{ fontSize: 16 }} className="text-muted" />
                  </Link>
                </li>
              ))}
            </ul>
            <p className="mt-3 text-xs leading-5 text-muted">
              The policy wording is kept in the database and updated with database/scripts/36_LegalPolicies.sql. Change the effective date here
              whenever the wording changes: businesses accept the version shown before paying. Have the policies reviewed by a lawyer.
            </p>
          </Panel>
        </div>
      )}
    </div>
  );
}
