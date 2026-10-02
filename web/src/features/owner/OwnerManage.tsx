import { useEffect, useState } from 'react';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Controller, useFieldArray, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import {
  Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Pagination, Skeleton, Switch, Tab, Table, TableBody,
  TableCell, TableContainer, TableHead, TableRow, Tabs, TextField, ToggleButton, ToggleButtonGroup,
} from '@mui/material';
import AddRounded from '@mui/icons-material/AddRounded';
import dayjs from 'dayjs';
import { useSnackbar } from 'notistack';
import { ApiError, api, errorMessage } from '@/lib/api';
import { ago, date, money, moneyExact, moneyPrecise, number } from '@/lib/format';
import { useDocumentTitle, useLookup } from '@/lib/hooks';
import type { OwnerAd, OwnerProfile as Profile, OwnerReview, OwnerService, OwnerSubscription as Subscription } from '@/lib/types';
import { EmptyState, ErrorState, PageHeader, Panel, StatusBadge, Stars } from '@/components/ui';
import { PlanGrid } from '@/features/pricing/PricingPage';
import { useBusiness } from './OwnerPortal';
import { SocialLinksPanel } from './SocialLinksPanel';
import { PlanCheckoutDialog } from './PlanCheckoutDialog';
import { usePaymentConfig } from '@/lib/payments';
import type { Plan } from '@/lib/types';

/* ===================== Reviews ===================== */
export function OwnerReviews() {
  useDocumentTitle('Reviews');
  const business = useBusiness();
  const [filter, setFilter] = useState('all');
  const [page, setPage] = useState(1);
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['owner', 'reviews', business.id, filter, page],
    queryFn: () => api.paged<OwnerReview>(`/api/owner/businesses/${business.id}/reviews`, { filter, page, pageSize: 10 }),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader title="Reviews" subtitle={`${business.averageRating.toFixed(2)}★ average from ${number(business.reviewCount)} published reviews. Replying publicly builds trust with new customers.`} />
      <Tabs value={filter} onChange={(_, v) => { setFilter(v); setPage(1); }} sx={{ borderBottom: '1px solid var(--cb-line)', mb: 3 }}>
        <Tab value="all" label="All" /><Tab value="unreplied" label="Needs reply" /><Tab value="replied" label="Replied" />
      </Tabs>
      {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <Skeleton variant="rounded" height={400} /> : !data!.items.length ? (
        <div className="card"><EmptyState title={filter === 'unreplied' ? 'All reviews have a reply' : 'No reviews yet'} /></div>
      ) : (
        <div className="space-y-3">{data!.items.map((r) => <ReviewItem key={r.id} review={r} />)}</div>
      )}
      {data && data.pagination.totalPages > 1 && <div className="mt-6 flex justify-center"><Pagination count={data.pagination.totalPages} page={page} onChange={(_, p) => setPage(p)} shape="rounded" /></div>}
    </>
  );
}

function ReviewItem({ review }: { review: OwnerReview }) {
  const business = useBusiness();
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const [open, setOpen] = useState(false);
  const [text, setText] = useState(review.ownerReply ?? '');
  const reply = useMutation({
    mutationFn: () => api.post(`/api/owner/businesses/${business.id}/reviews/${review.id}/reply`, { reply: text }),
    onSuccess: () => { enqueueSnackbar('Reply posted', { variant: 'success' }); setOpen(false); void queryClient.invalidateQueries({ queryKey: ['owner', 'reviews'] }); },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });

  return (
    <article className="card p-5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2"><Stars value={review.rating} /><span className="font-semibold">{review.title}</span></div>
        <div className="flex items-center gap-2 text-xs text-muted">{review.status !== 'Published' && <StatusBadge type="ReviewStatus" code={review.status} />}{ago(review.createdOn)}</div>
      </div>
      <p className="mt-2 text-sm leading-6 text-ink-2">{review.comment}</p>
      <p className="mt-1 text-xs text-muted">{review.customerName}{review.isVerifiedVisit && ' · Verified booking'}</p>
      {review.ownerReply && !open && (
        <div className="mt-3 rounded-lg border-l-2 border-accent bg-canvas px-4 py-3 text-sm">
          <span className="font-semibold">Your reply</span> <span className="text-xs text-muted">· {ago(review.repliedOn)}</span>
          <p className="mt-0.5 text-ink-2">{review.ownerReply}</p>
        </div>
      )}
      {open ? (
        <div className="mt-3 space-y-2">
          <TextField multiline minRows={3} value={text} onChange={(e) => setText(e.target.value)} placeholder="Thank the customer or address their concern…" />
          <div className="flex justify-end gap-2">
            <Button onClick={() => setOpen(false)} color="inherit">Cancel</Button>
            <Button variant="contained" disabled={text.trim().length < 5 || reply.isPending} onClick={() => reply.mutate()}>Post reply</Button>
          </div>
        </div>
      ) : <Button size="small" sx={{ mt: 1.5 }} onClick={() => setOpen(true)}>{review.ownerReply ? 'Edit reply' : 'Reply'}</Button>}
    </article>
  );
}

/* ===================== Services ===================== */
const serviceSchema = z.object({
  name: z.string().trim().min(2, 'Enter a service name').max(150),
  description: z.string().trim().min(5, 'Add a short description').max(1000),
  price: z.coerce.number().min(0, 'Price cannot be negative'),
  priceUnit: z.string().trim().max(40).optional(),
  durationMinutes: z.coerce.number().int().min(10, 'At least 10 minutes').max(600),
  type: z.string().min(1),
  isPopular: z.boolean(),
  isActive: z.boolean(),
});
type ServiceForm = z.infer<typeof serviceSchema>;

export function OwnerServices() {
  useDocumentTitle('Services');
  const business = useBusiness();
  const types = useLookup('ServiceType');
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const [editing, setEditing] = useState<OwnerService | 'new' | null>(null);
  const { data, isLoading, isError, refetch } = useQuery({ queryKey: ['owner', 'services', business.id], queryFn: () => api.get<OwnerService[]>(`/api/owner/businesses/${business.id}/services`) });

  const { register, control, handleSubmit, reset, setError, formState: { errors, isSubmitting } } = useForm<z.input<typeof serviceSchema>, unknown, ServiceForm>({ resolver: zodResolver(serviceSchema) });
  useEffect(() => {
    if (editing === 'new') reset({ name: '', description: '', price: 0, priceUnit: 'per visit', durationMinutes: 60, type: 'AtHome', isPopular: false, isActive: true });
    else if (editing) reset({ ...editing, priceUnit: editing.priceUnit ?? '' });
  }, [editing, reset]);

  const save = async (v: ServiceForm) => {
    try {
      if (editing === 'new') await api.post(`/api/owner/businesses/${business.id}/services`, v);
      else if (editing) await api.put(`/api/owner/businesses/${business.id}/services/${editing.id}`, v);
      enqueueSnackbar(editing === 'new' ? 'Service added' : 'Service updated', { variant: 'success' });
      setEditing(null);
      void queryClient.invalidateQueries({ queryKey: ['owner', 'services'] });
    } catch (e) {
      if (e instanceof ApiError && e.errors) Object.entries(e.errors).forEach(([k, m]) => setError(k as keyof ServiceForm, { message: m[0] }));
      else enqueueSnackbar(errorMessage(e), { variant: 'error' });
    }
  };

  const toggle = useMutation({
    mutationFn: (s: OwnerService) => api.put(`/api/owner/businesses/${business.id}/services/${s.id}`, { ...s, isActive: !s.isActive }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['owner', 'services'] }),
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });

  return (
    <>
      <PageHeader title="Services & pricing" subtitle="Clear prices help customers choose you. Inactive services are hidden from your profile."
        actions={<Button variant="contained" startIcon={<AddRounded />} onClick={() => setEditing('new')}>Add service</Button>} />
      <div className="card overflow-hidden">
        {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <div className="p-5"><Skeleton height={300} /></div> : !data!.length ? (
          <EmptyState title="No services yet" message="Add your first service with a price so customers can book or request a quote." />
        ) : (
          <TableContainer>
            <Table size="small">
              <TableHead><TableRow><TableCell>Service</TableCell><TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}>Type</TableCell><TableCell align="right">Price</TableCell><TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>Bookings</TableCell><TableCell>Visible</TableCell><TableCell /></TableRow></TableHead>
              <TableBody>
                {data!.map((s) => (
                  <TableRow key={s.id} hover sx={{ opacity: s.isActive ? 1 : 0.6 }}>
                    <TableCell sx={{ maxWidth: 380 }}>
                      <div className="font-semibold">{s.name} {s.isPopular && <span className="ml-1 rounded bg-accent-soft px-1.5 py-0.5 text-[11px] font-semibold text-accent-ink">Popular</span>}</div>
                      <div className="truncate text-xs text-muted">{s.description}</div>
                    </TableCell>
                    <TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}>{types.find((t) => t.code === s.type)?.name ?? s.type}<div className="text-xs text-muted">{s.durationMinutes} min</div></TableCell>
                    <TableCell align="right"><div className="font-semibold">{money(s.price)}</div><div className="text-xs text-muted">{s.price > 0 ? s.priceUnit : ''}</div></TableCell>
                    <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>{number(s.bookingCount)}</TableCell>
                    <TableCell><Switch size="small" checked={s.isActive} onChange={() => toggle.mutate(s)} slotProps={{ input: { 'aria-label': `Show ${s.name}` } }} /></TableCell>
                    <TableCell align="right"><Button size="small" onClick={() => setEditing(s)}>Edit</Button></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </div>

      <Dialog open={!!editing} onClose={() => setEditing(null)} fullWidth maxWidth="sm">
        <DialogTitle fontWeight={700}>{editing === 'new' ? 'Add service' : 'Edit service'}</DialogTitle>
        <form onSubmit={handleSubmit(save)} noValidate>
          <DialogContent className="space-y-4" sx={{ pt: '8px !important' }}>
            <TextField label="Service name" {...register('name')} error={!!errors.name} helperText={errors.name?.message} />
            <TextField label="Description" multiline minRows={2} {...register('description')} error={!!errors.description} helperText={errors.description?.message} />
            <div className="grid gap-4 sm:grid-cols-3">
              <TextField label="Price (₹)" type="number" {...register('price')} error={!!errors.price} helperText={errors.price?.message ?? '0 = free'} />
              <TextField label="Price unit" placeholder="per visit" {...register('priceUnit')} />
              <TextField label="Duration (min)" type="number" {...register('durationMinutes')} error={!!errors.durationMinutes} helperText={errors.durationMinutes?.message} />
            </div>
            <Controller control={control} name="type" render={({ field }) => (
              <TextField select label="Where is it delivered?" {...field}>{types.map((t) => <MenuItem key={t.code} value={t.code}>{t.name}</MenuItem>)}</TextField>
            )} />
            <div className="flex gap-6">
              <Controller control={control} name="isPopular" render={({ field }) => <FormControlLabel control={<Checkbox checked={!!field.value} onChange={(e) => field.onChange(e.target.checked)} />} label="Mark as popular" />} />
              <Controller control={control} name="isActive" render={({ field }) => <FormControlLabel control={<Checkbox checked={!!field.value} onChange={(e) => field.onChange(e.target.checked)} />} label="Visible on profile" />} />
            </div>
          </DialogContent>
          <DialogActions sx={{ px: 3, pb: 2.5 }}>
            <Button onClick={() => setEditing(null)} color="inherit">Cancel</Button>
            <Button type="submit" variant="contained" disabled={isSubmitting}>{isSubmitting ? 'Saving…' : 'Save service'}</Button>
          </DialogActions>
        </form>
      </Dialog>
    </>
  );
}

/* ===================== Profile & hours ===================== */
const profileSchema = z.object({
  tagline: z.string().trim().max(200).optional(),
  description: z.string().trim().min(30, 'Describe your business in at least 30 characters').max(2000),
  phoneNumber: z.string().trim().optional(),
  whatsAppNumber: z.string().trim().optional(),
  email: z.union([z.literal(''), z.string().trim().email('Enter a valid email')]).optional(),
  website: z.union([z.literal(''), z.string().trim().url('Enter a full URL, e.g. https://example.in')]).optional(),
  addressLine: z.string().trim().optional(),
  landmark: z.string().trim().optional(),
  acceptsOnlineBooking: z.boolean(),
  offersVideoConsultation: z.boolean(),
  offersHomeService: z.boolean(),
  hours: z.array(z.object({ dayOfWeek: z.number(), day: z.string(), open: z.string().nullable().optional(), close: z.string().nullable().optional(), isClosed: z.boolean() })),
});
type ProfileForm = z.infer<typeof profileSchema>;

export function OwnerProfile() {
  useDocumentTitle('Business profile');
  const business = useBusiness();
  const { enqueueSnackbar } = useSnackbar();
  const queryClient = useQueryClient();
  const { data, isLoading, isError, refetch } = useQuery({ queryKey: ['owner', 'profile', business.id], queryFn: () => api.get<Profile>(`/api/owner/businesses/${business.id}/profile`) });
  const { register, control, handleSubmit, reset, watch, formState: { errors, isSubmitting, isDirty } } = useForm<ProfileForm>({ resolver: zodResolver(profileSchema) });
  const { fields } = useFieldArray({ control, name: 'hours' });
  useEffect(() => {
    if (data) reset({ ...data, tagline: data.tagline ?? '', phoneNumber: data.phoneNumber ?? '', whatsAppNumber: data.whatsAppNumber ?? '', email: data.email ?? '', website: data.website ?? '', addressLine: data.addressLine ?? '', landmark: data.landmark ?? '' });
  }, [data, reset]);

  const save = async (v: ProfileForm) => {
    try {
      await api.put(`/api/owner/businesses/${business.id}/profile`, {
        ...v, hours: v.hours.map((h) => ({ dayOfWeek: h.dayOfWeek, open: h.isClosed ? null : h.open, close: h.isClosed ? null : h.close, isClosed: h.isClosed })),
      });
      enqueueSnackbar('Profile saved', { variant: 'success' });
      void queryClient.invalidateQueries({ queryKey: ['owner', 'profile'] });
      void queryClient.invalidateQueries({ queryKey: ['business', business.slug] });
    } catch (e) {
      enqueueSnackbar(errorMessage(e), { variant: 'error' });
    }
  };

  if (isError) return <ErrorState onRetry={() => refetch()} />;
  if (isLoading) return <Skeleton variant="rounded" height={600} />;

  return (
    <>
    <form onSubmit={handleSubmit(save)} noValidate>
      <PageHeader title="Business profile" subtitle="This information appears on your public listing."
        actions={<Button type="submit" variant="contained" disabled={isSubmitting || !isDirty}>{isSubmitting ? 'Saving…' : 'Save changes'}</Button>} />
      <div className="grid gap-6 xl:grid-cols-2">
        <Panel title="About your business">
          <div className="space-y-4">
            <TextField label="Business name" value={data!.name} disabled helperText="Contact support to change your registered name" />
            <TextField label="Tagline" {...register('tagline')} />
            <TextField label="Description" multiline minRows={5} {...register('description')} error={!!errors.description} helperText={errors.description?.message} />
            <div className="flex flex-col gap-1">
              {(['acceptsOnlineBooking', 'offersVideoConsultation', 'offersHomeService'] as const).map((k) => (
                <Controller key={k} control={control} name={k} render={({ field }) => (
                  <FormControlLabel control={<Switch checked={!!field.value} onChange={(e) => field.onChange(e.target.checked)} />}
                    label={{ acceptsOnlineBooking: 'Accept online bookings', offersVideoConsultation: 'Offer video consultations', offersHomeService: 'Visit customers at home' }[k]} />
                )} />
              ))}
            </div>
          </div>
        </Panel>
        <Panel title="Contact & address">
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label="Phone" {...register('phoneNumber')} />
            <TextField label="WhatsApp" {...register('whatsAppNumber')} />
            <TextField label="Email" {...register('email')} error={!!errors.email} helperText={errors.email?.message} />
            <TextField label="Website" {...register('website')} error={!!errors.website} helperText={errors.website?.message} />
            <div className="sm:col-span-2"><TextField label="Address" {...register('addressLine')} /></div>
            <div className="sm:col-span-2"><TextField label="Landmark" {...register('landmark')} /></div>
          </div>
        </Panel>
        <Panel title="Working hours" subtitle="Used for “open now” search and booking slots" className="xl:col-span-2">
          <div className="divide-y divide-line">
            {fields.map((f, i) => {
              const closed = watch(`hours.${i}.isClosed`);
              return (
                <div key={f.id} className="flex flex-wrap items-center gap-3 py-2.5">
                  <span className="w-28 font-medium">{f.day}</span>
                  <Controller control={control} name={`hours.${i}.isClosed`} render={({ field }) => (
                    <FormControlLabel control={<Switch checked={!field.value} onChange={(e) => field.onChange(!e.target.checked)} />} label={field.value ? 'Closed' : 'Open'} sx={{ width: 110 }} />
                  )} />
                  {!closed && (
                    <>
                      <TextField type="time" {...register(`hours.${i}.open`)} sx={{ width: 140 }} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': `${f.day} opening time` } }} />
                      <span className="text-muted">to</span>
                      <TextField type="time" {...register(`hours.${i}.close`)} sx={{ width: 140 }} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': `${f.day} closing time` } }} />
                    </>
                  )}
                </div>
              );
            })}
          </div>
        </Panel>
      </div>
    </form>
    <SocialLinksPanel businessId={business.id} slug={business.slug} />
    </>
  );
}

/* ===================== Plan & billing ===================== */
export function OwnerSubscription() {
  useDocumentTitle('Plan & billing');
  const business = useBusiness();
  const { enqueueSnackbar } = useSnackbar();
  const [cycle, setCycle] = useState<'monthly' | 'annual'>('monthly');
  const [checkout, setCheckout] = useState<Plan | null>(null);
  const payments = usePaymentConfig();
  const { data, isLoading, isError, refetch } = useQuery({ queryKey: ['owner', 'subscription', business.id], queryFn: () => api.get<Subscription>(`/api/owner/businesses/${business.id}/subscription`) });
  const choose = (p: Plan) => {
    if (p.monthlyPrice === 0) enqueueSnackbar('To move back to the Free plan, contact support and we will switch you at the end of your current period.', { variant: 'info' });
    else if (!payments.data?.enabled) enqueueSnackbar(`Online payment is unavailable right now. Our team will call you to activate ${p.name}.`, { variant: 'info' });
    else setCheckout(p);
  };
  if (isError) return <ErrorState onRetry={() => refetch()} />;

  return (
    <>
      <PageHeader title="Plan & billing" subtitle="Upgrade for more lead credits, priority placement and advanced analytics. Pay securely online by UPI, card or net banking." />
      <PlanCheckoutDialog businessId={business.id} plan={checkout} cycle={cycle === 'annual' ? 'Annual' : 'Monthly'} open={!!checkout}
        onClose={() => { setCheckout(null); void refetch(); }} />
      {isLoading ? <Skeleton variant="rounded" height={500} /> : (
        <div className="space-y-6">
          {data!.current && (
            <Panel>
              <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                <div>
                  <div className="text-sm text-muted">Current plan</div>
                  <div className="text-2xl font-bold">{data!.current.planName} <span className="text-base font-normal text-muted">· {data!.current.billingCycle}</span></div>
                  <div className="text-sm text-muted">{data!.currentPlanCode === 'FREE' ? 'Free forever' : `${data!.current.status === 'Trial' ? 'Trial ends' : 'Renews'} on ${date(data!.current.endDate)} · ${moneyExact(data!.current.amount)} + GST`}</div>
                </div>
                <StatusBadge type="SubscriptionStatus" code={data!.current.status} size="md" />
              </div>
            </Panel>
          )}
          <div className="flex items-center justify-between">
            <h2 className="text-lg font-bold">Compare plans</h2>
            <ToggleButtonGroup exclusive size="small" value={cycle} onChange={(_, v) => v && setCycle(v)}>
              <ToggleButton value="monthly">Monthly</ToggleButton><ToggleButton value="annual">Annual</ToggleButton>
            </ToggleButtonGroup>
          </div>
          <PlanGrid plans={data!.plans} cycle={cycle} currentCode={data!.currentPlanCode} action={(p) => (
            <Button fullWidth variant={p.code === data!.currentPlanCode ? 'outlined' : 'contained'} disabled={p.code === data!.currentPlanCode}
              onClick={() => choose(p)}>
              {p.code === data!.currentPlanCode ? 'Current plan' : p.monthlyPrice === 0 ? 'Switch to Free' : `Upgrade to ${p.name}`}
            </Button>
          )} />
          <Panel title="Invoices" subtitle="Subscriptions, advertising, booking commission and lead credits" noPad>
            {!data!.invoices.length ? <EmptyState title="No invoices yet" /> : (
              <TableContainer>
                <Table size="small">
                  <TableHead><TableRow><TableCell>Invoice</TableCell><TableCell>Type</TableCell><TableCell sx={{ display: { xs: 'none', sm: 'table-cell' } }}>Date</TableCell><TableCell align="right">Amount</TableCell><TableCell align="right">Total (incl. GST)</TableCell></TableRow></TableHead>
                  <TableBody>
                    {data!.invoices.map((i) => (
                      <TableRow key={i.invoiceNumber} hover>
                        <TableCell sx={{ fontFamily: 'ui-monospace, monospace', fontSize: 12 }}>{i.invoiceNumber}</TableCell>
                        <TableCell><StatusBadge type="PaymentType" code={i.paymentType} /></TableCell>
                        <TableCell sx={{ display: { xs: 'none', sm: 'table-cell' } }}>{date(i.paidOn)}</TableCell>
                        <TableCell align="right">{moneyPrecise(i.amount)}</TableCell>
                        <TableCell align="right" sx={{ fontWeight: 600 }}>{moneyPrecise(i.totalAmount)}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            )}
          </Panel>
        </div>
      )}
    </>
  );
}

/* ===================== Advertising ===================== */
const adSchema = z.object({
  adType: z.string().min(1),
  title: z.string().trim().min(5, 'Write a short headline').max(120),
  description: z.string().trim().max(300).optional(),
  startDate: z.string().min(1),
  endDate: z.string().min(1),
  budget: z.coerce.number().min(2000, 'Minimum budget is ₹2,000'),
}).refine((v) => v.endDate > v.startDate, { path: ['endDate'], message: 'End date must be after the start date' });

export function OwnerAds() {
  useDocumentTitle('Advertising');
  const business = useBusiness();
  const adTypes = useLookup('AdType');
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const [open, setOpen] = useState(false);
  const { data, isLoading, isError, refetch } = useQuery({ queryKey: ['owner', 'ads', business.id], queryFn: () => api.get<OwnerAd[]>(`/api/owner/businesses/${business.id}/advertisements`) });
  const { register, control, handleSubmit, reset, formState: { errors, isSubmitting } } = useForm<z.input<typeof adSchema>, unknown, z.output<typeof adSchema>>({ resolver: zodResolver(adSchema) });

  const create = async (v: z.output<typeof adSchema>) => {
    try {
      const res = await api.post<{ reference: string }>(`/api/owner/businesses/${business.id}/advertisements`, v);
      enqueueSnackbar(`Campaign ${res.data.reference} submitted for approval`, { variant: 'success' });
      setOpen(false);
      void queryClient.invalidateQueries({ queryKey: ['owner', 'ads'] });
    } catch (e) {
      enqueueSnackbar(errorMessage(e), { variant: 'error' });
    }
  };

  const live = data?.filter((a) => a.status === 'Active') ?? [];
  const totals = live.reduce((t, a) => ({ imp: t.imp + a.impressions, clk: t.clk + a.clicks, spent: t.spent + a.amountSpent }), { imp: 0, clk: 0, spent: 0 });

  return (
    <>
      <PageHeader title="Advertising" subtitle="Promote your business on the homepage, in search results and as a featured listing."
        actions={<Button variant="contained" startIcon={<AddRounded />} onClick={() => {
          reset({ adType: 'SponsoredListing', title: '', description: '', startDate: dayjs().add(1, 'day').format('YYYY-MM-DD'), endDate: dayjs().add(31, 'day').format('YYYY-MM-DD'), budget: 10000 });
          setOpen(true);
        }}>New campaign</Button>} />
      <div className="mb-6 grid grid-cols-2 gap-4 lg:grid-cols-4">
        {[['Live campaigns', number(live.length)], ['Impressions', number(totals.imp)], ['Clicks', number(totals.clk)], ['Spent so far', moneyExact(totals.spent)]].map(([l, v]) => (
          <div key={l} className="card p-4"><div className="text-sm text-muted">{l}</div><div className="mt-1 text-2xl font-bold">{isLoading ? <Skeleton width={60} /> : v}</div></div>
        ))}
      </div>
      <div className="card overflow-hidden">
        {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <div className="p-5"><Skeleton height={260} /></div> : !data!.length ? (
          <EmptyState title="No campaigns yet" message="Start a sponsored listing to appear above competitors in your area." />
        ) : (
          <TableContainer>
            <Table size="small">
              <TableHead><TableRow><TableCell>Campaign</TableCell><TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}>Dates</TableCell><TableCell align="right">Budget</TableCell><TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>Impressions</TableCell><TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>CTR</TableCell><TableCell>Status</TableCell></TableRow></TableHead>
              <TableBody>
                {data!.map((a) => (
                  <TableRow key={a.id} hover>
                    <TableCell sx={{ maxWidth: 320 }}><div className="truncate font-semibold">{a.title}</div><div className="text-xs text-muted">{a.campaignCode} · <StatusBadge type="AdType" code={a.adType} /></div></TableCell>
                    <TableCell sx={{ display: { xs: 'none', md: 'table-cell' }, whiteSpace: 'nowrap' }}>{date(a.startDate)} - {date(a.endDate)}</TableCell>
                    <TableCell align="right"><div className="font-semibold">{moneyExact(a.budget)}</div><div className="text-xs text-muted">{moneyExact(a.amountSpent)} spent</div></TableCell>
                    <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>{number(a.impressions)}<div className="text-xs text-muted">{number(a.clicks)} clicks</div></TableCell>
                    <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>{a.ctr.toFixed(2)}%</TableCell>
                    <TableCell><StatusBadge type="AdStatus" code={a.status} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </div>

      <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle fontWeight={700}>New advertising campaign</DialogTitle>
        <form onSubmit={handleSubmit(create)} noValidate>
          <DialogContent className="space-y-4" sx={{ pt: '8px !important' }}>
            <Controller control={control} name="adType" render={({ field }) => (
              <TextField select label="Placement" {...field} helperText={adTypes.find((t) => t.code === field.value)?.description}>
                {adTypes.map((t) => <MenuItem key={t.code} value={t.code}>{t.name}</MenuItem>)}
              </TextField>
            )} />
            <TextField label="Headline" {...register('title')} error={!!errors.title} helperText={errors.title?.message} />
            <TextField label="Description (optional)" multiline minRows={2} {...register('description')} />
            <div className="grid gap-4 sm:grid-cols-3">
              <TextField label="Start" type="date" slotProps={{ inputLabel: { shrink: true } }} {...register('startDate')} />
              <TextField label="End" type="date" slotProps={{ inputLabel: { shrink: true } }} {...register('endDate')} error={!!errors.endDate} helperText={errors.endDate?.message} />
              <TextField label="Budget (₹)" type="number" {...register('budget')} error={!!errors.budget} helperText={errors.budget?.message} />
            </div>
            <p className="text-xs text-muted">Campaigns are reviewed within one business day. Your card is charged when the campaign is approved (18% GST applies).</p>
          </DialogContent>
          <DialogActions sx={{ px: 3, pb: 2.5 }}>
            <Button onClick={() => setOpen(false)} color="inherit">Cancel</Button>
            <Button type="submit" variant="contained" disabled={isSubmitting}>{isSubmitting ? 'Submitting…' : 'Submit for approval'}</Button>
          </DialogActions>
        </form>
      </Dialog>
    </>
  );
}

