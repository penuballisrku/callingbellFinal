import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSnackbar } from 'notistack';
import {
  Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, MenuItem, Skeleton, Switch, TextField, Tooltip,
} from '@mui/material';
import AddRounded from '@mui/icons-material/AddRounded';
import EditOutlined from '@mui/icons-material/EditOutlined';
import DeleteOutlineRounded from '@mui/icons-material/DeleteOutlineRounded';
import GroupsOutlined from '@mui/icons-material/GroupsOutlined';
import CallOutlined from '@mui/icons-material/CallOutlined';
import MailOutlineRounded from '@mui/icons-material/MailOutlineRounded';
import { api, ApiError, errorMessage } from '@/lib/api';
import { number } from '@/lib/format';
import { useDocumentTitle } from '@/lib/hooks';
import type { OwnerService, OwnerStaff, StaffHour } from '@/lib/types';
import { ConfirmDialog, EmptyState, ErrorState, PageHeader } from '@/components/ui';
import { useBusiness } from './OwnerPortal';

const DAYS = [1, 2, 3, 4, 5, 6, 0];
const DAY = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
type DayMode = 'business' | 'custom' | 'off';
interface DayForm { mode: DayMode; open: string; close: string }
interface StaffForm {
  fullName: string; title: string; phone: string; email: string; bio: string; yearsExperience: string; languages: string;
  acceptsBookings: boolean; isActive: boolean; serviceIds: string[]; days: Record<number, DayForm>;
}

const initials = (n: string) => n.split(' ').filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('');

function toForm(s: OwnerStaff | null, allServiceIds: string[]): StaffForm {
  const days: Record<number, DayForm> = {};
  for (const d of DAYS) {
    const h = s?.hours.find((x) => x.dayOfWeek === d);
    days[d] = !h ? { mode: 'business', open: '09:00', close: '18:00' }
      : h.isClosed ? { mode: 'off', open: '09:00', close: '18:00' } : { mode: 'custom', open: h.open ?? '09:00', close: h.close ?? '18:00' };
  }
  return {
    fullName: s?.fullName ?? '', title: s?.title ?? '', phone: s?.phone ?? '', email: s?.email ?? '', bio: s?.bio ?? '',
    yearsExperience: s?.yearsExperience != null ? String(s.yearsExperience) : '', languages: s?.languages ?? '',
    acceptsBookings: s?.acceptsBookings ?? true, isActive: s?.isActive ?? true, serviceIds: s?.serviceIds ?? allServiceIds, days,
  };
}

/** The team: who works here, which services each person does, when they work, and how busy they are. */
export function OwnerTeam() {
  useDocumentTitle('Team');
  const business = useBusiness();
  const { enqueueSnackbar } = useSnackbar();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<OwnerStaff | 'new' | null>(null);
  const [removing, setRemoving] = useState<OwnerStaff | null>(null);
  const staff = useQuery({ queryKey: ['owner', 'staff', business.id], queryFn: () => api.get<OwnerStaff[]>(`/api/owner/businesses/${business.id}/staff`) });
  const services = useQuery({ queryKey: ['owner', 'services', business.id], queryFn: () => api.get<OwnerService[]>(`/api/owner/businesses/${business.id}/services`) });
  const remove = useMutation({
    mutationFn: (s: OwnerStaff) => api.del<number>(`/api/owner/businesses/${business.id}/staff/${s.id}`),
    onSuccess: (res, s) => {
      enqueueSnackbar(res.data ? `${s.fullName} removed. ${res.data} upcoming booking${res.data === 1 ? '' : 's'} need a new professional.` : `${s.fullName} removed`,
        { variant: res.data ? 'warning' : 'success' });
      setRemoving(null);
      void queryClient.invalidateQueries({ queryKey: ['owner', 'staff'] });
      void queryClient.invalidateQueries({ queryKey: ['owner', 'bookings'] });
    },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });
  const serviceName = (id: string) => services.data?.find((s) => s.id === id)?.name;

  return (
    <>
      <PageHeader title="Team" subtitle="The people customers can book. Each person's services and hours decide which times are free."
        actions={<Button variant="contained" startIcon={<AddRounded />} onClick={() => setEditing('new')} disabled={!services.data}>Add team member</Button>} />
      {staff.isError ? <ErrorState onRetry={() => void staff.refetch()} /> : staff.isLoading ? (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">{[0, 1, 2].map((i) => <Skeleton key={i} variant="rounded" height={190} />)}</div>
      ) : !staff.data?.length ? (
        <div className="card"><EmptyState icon={<GroupsOutlined />} title="No team members yet"
          message="Add the people who do your services. Customers can choose who they book, and bookings are shared out by who is free."
          action={<Button variant="contained" startIcon={<AddRounded />} onClick={() => setEditing('new')}>Add team member</Button>} /></div>
      ) : (
        <ul className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {staff.data.map((s) => (
            <li key={s.id} className={`card flex flex-col p-4 ${s.isActive ? '' : 'opacity-70'}`}>
              <div className="flex items-start gap-3">
                <span aria-hidden className="grid h-12 w-12 shrink-0 place-items-center rounded-full bg-accent-soft font-bold text-accent-ink">{initials(s.fullName)}</span>
                <div className="min-w-0 flex-1">
                  <p className="truncate font-semibold">{s.fullName}</p>
                  <p className="truncate text-sm text-muted">{[s.title, s.yearsExperience != null ? `${s.yearsExperience} yrs` : null].filter(Boolean).join(' · ')}</p>
                </div>
                <Tooltip title="Edit"><IconButton size="small" onClick={() => setEditing(s)} aria-label={`Edit ${s.fullName}`}><EditOutlined fontSize="small" /></IconButton></Tooltip>
                <Tooltip title="Remove"><IconButton size="small" onClick={() => setRemoving(s)} aria-label={`Remove ${s.fullName}`}><DeleteOutlineRounded fontSize="small" /></IconButton></Tooltip>
              </div>
              <div className="mt-3 flex flex-wrap gap-1.5">
                {!s.isActive && <Chip size="small" label="Inactive" />}
                {s.isActive && !s.acceptsBookings && <Chip size="small" label="Not bookable" />}
                {s.isActive && s.acceptsBookings && <Chip size="small" color="success" variant="outlined" label="Taking bookings" />}
              </div>
              <p className="mt-3 line-clamp-2 text-sm text-ink-2">
                {s.serviceIds.length ? s.serviceIds.map(serviceName).filter(Boolean).join(', ') : <span className="text-muted">No services yet: can't be booked</span>}
              </p>
              <div className="mt-auto grid grid-cols-2 gap-2 pt-4 text-center">
                <div className="rounded-lg bg-subtle py-2"><div className="text-lg font-bold tabular">{number(s.upcomingBookings)}</div><div className="text-[11px] text-muted">Upcoming</div></div>
                <div className="rounded-lg bg-subtle py-2"><div className="text-lg font-bold tabular">{number(s.completedThisMonth)}</div><div className="text-[11px] text-muted">Done this month</div></div>
              </div>
              <div className="mt-3 flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted">
                {s.phone && <a href={`tel:${s.phone.replace(/\s/g, '')}`} className="inline-flex items-center gap-1 hover:text-ink"><CallOutlined sx={{ fontSize: 14 }} />{s.phone}</a>}
                {s.email && <a href={`mailto:${s.email}`} className="inline-flex min-w-0 items-center gap-1 hover:text-ink"><MailOutlineRounded sx={{ fontSize: 14 }} /><span className="truncate">{s.email}</span></a>}
              </div>
            </li>
          ))}
        </ul>
      )}

      {editing && services.data && (
        <StaffDialog businessId={business.id} staff={editing === 'new' ? null : editing} services={services.data} onClose={() => setEditing(null)} />
      )}
      <ConfirmDialog open={!!removing} title={`Remove ${removing?.fullName}?`} destructive confirmLabel="Remove" loading={remove.isPending}
        message={removing?.upcomingBookings ? `${removing.fullName} has ${removing.upcomingBookings} upcoming booking${removing.upcomingBookings === 1 ? '' : 's'}. They will need someone else.` : 'They will no longer appear on your profile or take bookings.'}
        onConfirm={() => removing && remove.mutate(removing)} onClose={() => setRemoving(null)} />
    </>
  );
}

function StaffDialog({ businessId, staff, services, onClose }: { businessId: string; staff: OwnerStaff | null; services: OwnerService[]; onClose: () => void }) {
  const { enqueueSnackbar } = useSnackbar();
  const queryClient = useQueryClient();
  const [form, setForm] = useState<StaffForm>(() => toForm(staff, services.filter((s) => s.isActive).map((s) => s.id)));
  const [errors, setErrors] = useState<Record<string, string>>({});
  useEffect(() => setErrors({}), [staff]);
  const set = <K extends keyof StaffForm>(k: K, v: StaffForm[K]) => { setForm((f) => ({ ...f, [k]: v })); setErrors((e) => ({ ...e, [k]: '' })); };
  const setDay = (d: number, patch: Partial<DayForm>) => setForm((f) => ({ ...f, days: { ...f.days, [d]: { ...f.days[d]!, ...patch } } }));

  const save = useMutation({
    mutationFn: () => {
      const hours: StaffHour[] = DAYS.filter((d) => form.days[d]!.mode !== 'business')
        .map((d) => ({ dayOfWeek: d, isClosed: form.days[d]!.mode === 'off', open: form.days[d]!.open, close: form.days[d]!.close }));
      const body = {
        fullName: form.fullName, title: form.title || null, phone: form.phone || null, email: form.email || null, bio: form.bio || null,
        yearsExperience: form.yearsExperience ? Number(form.yearsExperience) : null, languages: form.languages || null,
        acceptsBookings: form.acceptsBookings, isActive: form.isActive, serviceIds: form.serviceIds, hours,
      };
      return staff ? api.put(`/api/owner/businesses/${businessId}/staff/${staff.id}`, body) : api.post(`/api/owner/businesses/${businessId}/staff`, body);
    },
    onSuccess: () => {
      enqueueSnackbar(staff ? 'Team member updated' : 'Team member added', { variant: 'success' });
      void queryClient.invalidateQueries({ queryKey: ['owner', 'staff'] });
      void queryClient.invalidateQueries({ queryKey: ['team', businessId] });
      onClose();
    },
    onError: (e) => {
      if (e instanceof ApiError && e.errors) setErrors(Object.fromEntries(Object.entries(e.errors).map(([k, v]) => [k.replace(/^hours.*/, 'hours'), v[0] ?? ''])));
      else enqueueSnackbar(errorMessage(e), { variant: 'error' });
    },
  });
  const submit = () => {
    const local: Record<string, string> = {};
    if (form.fullName.trim().length < 2) local.fullName = 'Enter the name';
    const bad = DAYS.find((d) => form.days[d]!.mode === 'custom' && !(form.days[d]!.open < form.days[d]!.close));
    if (bad !== undefined) local.hours = `${DAY[bad]}: the closing time must be after the opening time`;
    setErrors(local);
    if (!Object.keys(local).length) save.mutate();
  };

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md" aria-labelledby="staff-title">
      <DialogTitle id="staff-title" fontWeight={700}>{staff ? `Edit ${staff.fullName}` : 'Add team member'}</DialogTitle>
      <DialogContent>
        <div className="grid gap-4 pt-1 sm:grid-cols-2">
          <TextField label="Full name" required value={form.fullName} onChange={(e) => set('fullName', e.target.value)} error={!!errors.fullName} helperText={errors.fullName} />
          <TextField label="Role / title" placeholder="e.g. Senior Technician" value={form.title} onChange={(e) => set('title', e.target.value)} />
          <TextField label="Mobile (private)" type="tel" value={form.phone} onChange={(e) => set('phone', e.target.value)} error={!!errors.phone} helperText={errors.phone ?? 'Only you can see this'} />
          <TextField label="Email (private)" type="email" value={form.email} onChange={(e) => set('email', e.target.value)} error={!!errors.email} helperText={errors.email} />
          <TextField label="Years of experience" inputMode="numeric" value={form.yearsExperience} onChange={(e) => set('yearsExperience', e.target.value.replace(/\D/g, '').slice(0, 2))} />
          <TextField label="Languages" placeholder="Telugu, Hindi, English" value={form.languages} onChange={(e) => set('languages', e.target.value)} />
          <TextField label="Short bio" multiline minRows={2} value={form.bio} onChange={(e) => set('bio', e.target.value)} className="sm:col-span-2"
            slotProps={{ htmlInput: { maxLength: 500 } }} helperText="Shown on your profile under “Our team”" />
        </div>

        <h3 className="mb-2 mt-6 text-sm font-semibold">Services they do</h3>
        <div className="grid gap-x-4 sm:grid-cols-2">
          {services.map((s) => (
            <FormControlLabel key={s.id} label={<span className={s.isActive ? '' : 'text-muted'}>{s.name}{s.isActive ? '' : ' (inactive)'}</span>}
              control={<Checkbox size="small" checked={form.serviceIds.includes(s.id)}
                onChange={(e) => set('serviceIds', e.target.checked ? [...form.serviceIds, s.id] : form.serviceIds.filter((x) => x !== s.id))} />} />
          ))}
        </div>

        <h3 className="mb-1 mt-6 text-sm font-semibold">Working hours</h3>
        <p className="mb-2 text-xs text-muted">Days on “Business hours” follow your opening hours.</p>
        {errors.hours && <p className="mb-2 text-sm text-danger">{errors.hours}</p>}
        <div className="divide-y divide-line rounded-lg border border-line">
          {DAYS.map((d) => {
            const day = form.days[d]!;
            return (
              <div key={d} className="grid grid-cols-[6.5rem_1fr] items-center gap-2 px-3 py-2 sm:grid-cols-[8rem_10rem_1fr]">
                <span className="text-sm font-medium">{DAY[d]}</span>
                <TextField select size="small" value={day.mode} onChange={(e) => setDay(d, { mode: e.target.value as DayMode })} slotProps={{ htmlInput: { 'aria-label': `${DAY[d]} hours` } }}>
                  <MenuItem value="business">Business hours</MenuItem>
                  <MenuItem value="custom">Custom</MenuItem>
                  <MenuItem value="off">Day off</MenuItem>
                </TextField>
                {day.mode === 'custom' && (
                  <div className="col-span-2 grid grid-cols-2 gap-2 sm:col-span-1">
                    <TextField type="time" size="small" label="From" value={day.open} onChange={(e) => setDay(d, { open: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} />
                    <TextField type="time" size="small" label="To" value={day.close} onChange={(e) => setDay(d, { close: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} />
                  </div>
                )}
              </div>
            );
          })}
        </div>

        <div className="mt-5 flex flex-col gap-1 sm:flex-row sm:gap-6">
          <FormControlLabel control={<Switch checked={form.acceptsBookings} onChange={(e) => set('acceptsBookings', e.target.checked)} />} label="Customers can book them" />
          <FormControlLabel control={<Switch checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} />} label="Active (shown on your profile)" />
        </div>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2.5 }}>
        <Button onClick={onClose} color="inherit">Cancel</Button>
        <Button variant="contained" onClick={submit} disabled={save.isPending}>{save.isPending ? 'Saving…' : staff ? 'Save changes' : 'Add to team'}</Button>
      </DialogActions>
    </Dialog>
  );
}
