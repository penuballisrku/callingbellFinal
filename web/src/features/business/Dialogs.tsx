import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useLocation } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import {
  Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Rating as MuiRating, Skeleton, TextField, ToggleButton, ToggleButtonGroup,
} from '@mui/material';
import dayjs from 'dayjs';
import { useSnackbar } from 'notistack';
import { ApiError, api, errorMessage } from '@/lib/api';
import { money } from '@/lib/format';
import { useAuth } from '@/stores/auth';
import type { BusinessDetail, CreatedReference, Review, Slot, TeamMember } from '@/lib/types';

const phone = z.string().trim().regex(/^(\+91[\s-]?)?[6-9]\d{4}[\s-]?\d{5}$/, 'Enter a valid 10-digit mobile number');

/* ---------------- Enquiry / quotation / callback ---------------- */
const enquirySchema = z.object({
  enquiryType: z.enum(['Enquiry', 'Quotation', 'Callback']),
  serviceId: z.string().optional(),
  customerName: z.string().trim().min(2, 'Enter your name'),
  customerPhone: phone,
  customerEmail: z.union([z.literal(''), z.string().trim().email('Enter a valid email')]).optional(),
  message: z.string().trim().min(10, 'Tell the business a little more (10+ characters)').max(1000),
  preferredDate: z.string().optional(),
  budget: z.string().optional(),
});
type EnquiryForm = z.infer<typeof enquirySchema>;

export function EnquiryDialog({ business, open, initialType = 'Enquiry', serviceId, onClose }: {
  business: BusinessDetail; open: boolean; initialType?: EnquiryForm['enquiryType']; serviceId?: string; onClose: () => void;
}) {
  const user = useAuth((s) => s.user);
  const { enqueueSnackbar } = useSnackbar();
  const [done, setDone] = useState<string | null>(null);
  const { register, control, handleSubmit, reset, setError, watch, formState: { errors, isSubmitting } } = useForm<EnquiryForm>({
    resolver: zodResolver(enquirySchema),
  });

  useEffect(() => {
    if (open) {
      setDone(null);
      reset({
        enquiryType: initialType, serviceId: serviceId ?? '', customerName: user?.displayName ?? '', customerPhone: user?.phoneNumber ?? '',
        customerEmail: user?.email ?? '', message: '', preferredDate: '', budget: '',
      });
    }
  }, [open, initialType, serviceId, user, reset]);
  const type = watch('enquiryType');

  const submit = async (v: EnquiryForm) => {
    try {
      const { data } = await api.post<CreatedReference>(`/api/businesses/${business.card.id}/enquiries`, {
        ...v, serviceId: v.serviceId || null, customerEmail: v.customerEmail || null,
        preferredDate: v.preferredDate || null, budget: v.budget ? Number(v.budget) : null,
      });
      setDone(data.reference);
      enqueueSnackbar('Request sent to the business', { variant: 'success' });
    } catch (e) {
      if (e instanceof ApiError && e.errors) Object.entries(e.errors).forEach(([k, m]) => setError(k as keyof EnquiryForm, { message: m[0] }));
      else enqueueSnackbar(errorMessage(e), { variant: 'error' });
    }
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle fontWeight={700}>Contact {business.card.name}</DialogTitle>
      {done ? (
        <>
          <DialogContent>
            <Alert severity="success" sx={{ mb: 2 }}>Your request <strong>{done}</strong> has been sent.</Alert>
            <p className="text-sm text-muted">{business.card.name} usually replies within {business.card.responseTimeMinutes ?? 30} minutes. You'll also find the update under My account.</p>
          </DialogContent>
          <DialogActions sx={{ px: 3, pb: 2.5 }}><Button variant="contained" onClick={onClose}>Done</Button></DialogActions>
        </>
      ) : (
        <form onSubmit={handleSubmit(submit)} noValidate>
          <DialogContent className="space-y-4" sx={{ pt: '8px !important' }}>
            <Controller control={control} name="enquiryType" render={({ field }) => (
              <ToggleButtonGroup exclusive fullWidth size="small" value={field.value} onChange={(_, v) => v && field.onChange(v)}>
                <ToggleButton value="Enquiry">Ask a question</ToggleButton>
                <ToggleButton value="Quotation">Get a quote</ToggleButton>
                <ToggleButton value="Callback">Request callback</ToggleButton>
              </ToggleButtonGroup>
            )} />
            <Controller control={control} name="serviceId" render={({ field }) => (
              <TextField select label="Service (optional)" {...field} value={field.value ?? ''}>
                <MenuItem value="">General enquiry</MenuItem>
                {business.services.map((s) => <MenuItem key={s.id} value={s.id}>{s.name} · {money(s.price)}</MenuItem>)}
              </TextField>
            )} />
            <div className="grid gap-4 sm:grid-cols-2">
              <TextField label="Your name" {...register('customerName')} error={!!errors.customerName} helperText={errors.customerName?.message} />
              <TextField label="Mobile number" {...register('customerPhone')} error={!!errors.customerPhone} helperText={errors.customerPhone?.message} />
            </div>
            <TextField label="Email (optional)" type="email" {...register('customerEmail')} error={!!errors.customerEmail} helperText={errors.customerEmail?.message} />
            <TextField label={type === 'Callback' ? 'What should they call you about?' : 'Describe what you need'} multiline minRows={3}
              {...register('message')} error={!!errors.message} helperText={errors.message?.message} />
            {type !== 'Callback' && (
              <div className="grid gap-4 sm:grid-cols-2">
                <TextField label="Preferred date" type="date" slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: dayjs().format('YYYY-MM-DD') } }} {...register('preferredDate')} />
                {type === 'Quotation' && <TextField label="Budget (₹, optional)" type="number" {...register('budget')} />}
              </div>
            )}
          </DialogContent>
          <DialogActions sx={{ px: 3, pb: 2.5 }}>
            <Button onClick={onClose} color="inherit">Cancel</Button>
            <Button type="submit" variant="contained" disabled={isSubmitting}>{isSubmitting ? 'Sending…' : 'Send request'}</Button>
          </DialogActions>
        </form>
      )}
    </Dialog>
  );
}

/* ---------------- Booking ---------------- */
export function BookingDialog({ business, open, serviceId, onClose }: { business: BusinessDetail; open: boolean; serviceId?: string; onClose: () => void }) {
  const { enqueueSnackbar } = useSnackbar();
  const queryClient = useQueryClient();
  const days = useMemo(() => Array.from({ length: 14 }, (_, i) => dayjs().add(i, 'day')), []);
  const [selectedService, setSelectedService] = useState(serviceId ?? business.services[0]?.id ?? '');
  const [day, setDay] = useState(days[0]!.format('YYYY-MM-DD'));
  const [slot, setSlot] = useState<string | null>(null);
  // "" = any available professional (the business assigns one).
  const [staffId, setStaffId] = useState('');
  const [address, setAddress] = useState('');
  const [notes, setNotes] = useState('');
  const [done, setDone] = useState<string | null>(null);

  useEffect(() => { if (open) { setSelectedService(serviceId ?? business.services[0]?.id ?? ''); setDone(null); setSlot(null); } }, [open, serviceId, business.services]);
  useEffect(() => setSlot(null), [day, selectedService, staffId]);

  // The team members who do the chosen service and take bookings.
  const team = useQuery({
    queryKey: ['team', business.card.id],
    queryFn: () => api.get<TeamMember[]>(`/api/businesses/${business.card.id}/team`),
    enabled: open,
    staleTime: 5 * 60_000,
  });
  const professionals = (team.data ?? []).filter((m) => m.acceptsBookings && m.serviceIds.includes(selectedService));
  useEffect(() => { if (staffId && !professionals.some((m) => m.id === staffId)) setStaffId(''); }, [selectedService, staffId, professionals]);

  const service = business.services.find((s) => s.id === selectedService);
  const closedDays = new Set(business.hours.filter((h) => h.isClosed).map((h) => h.dayOfWeek));

  const slots = useQuery({
    queryKey: ['slots', business.card.id, selectedService, day, staffId],
    queryFn: () => api.get<Slot[]>(`/api/businesses/${business.card.id}/slots`, { serviceId: selectedService, date: day, staffId: staffId || null }),
    enabled: open && !!selectedService,
  });

  const book = useMutation({
    mutationFn: () => api.post<CreatedReference>('/api/me/bookings', {
      businessId: business.card.id, serviceId: selectedService, scheduledStart: slot, serviceAddress: address || null, notes: notes || null,
      staffId: staffId || null,
    }),
    onSuccess: ({ data }) => {
      setDone(data.reference);
      enqueueSnackbar('Booking requested', { variant: 'success' });
      void queryClient.invalidateQueries({ queryKey: ['me', 'bookings'] });
    },
    onError: (e) => {
      enqueueSnackbar(errorMessage(e), { variant: 'error' });
      void slots.refetch();
    },
  });

  const needsAddress = service?.type === 'AtHome';
  const canSubmit = !!slot && (!needsAddress || address.trim().length >= 10);

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle fontWeight={700}>Book with {business.card.name}</DialogTitle>
      {done ? (
        <>
          <DialogContent>
            <Alert severity="success" sx={{ mb: 2 }}>Booking <strong>{done}</strong> requested for {dayjs(slot).format('ddd, D MMM [at] h:mm A')}.</Alert>
            <p className="text-sm text-muted">The business will confirm shortly. You'll get a notification once it's confirmed.</p>
          </DialogContent>
          <DialogActions sx={{ px: 3, pb: 2.5 }}><Button variant="contained" onClick={onClose}>Done</Button></DialogActions>
        </>
      ) : (
        <>
          <DialogContent className="space-y-5" sx={{ pt: '8px !important' }}>
            <TextField select label="Service" value={selectedService} onChange={(e) => setSelectedService(e.target.value)}>
              {business.services.map((s) => <MenuItem key={s.id} value={s.id}>{s.name} · {money(s.price)} {s.priceUnit && s.price > 0 ? s.priceUnit : ''}</MenuItem>)}
            </TextField>

            {professionals.length > 0 && (
              <div>
                <div className="mb-2 text-sm font-semibold">Professional</div>
                <div className="flex flex-wrap gap-2" role="radiogroup" aria-label="Professional">
                  {[{ id: '', fullName: 'Any available', title: 'Fastest confirmation' } as Pick<TeamMember, 'id' | 'fullName' | 'title'>, ...professionals].map((m) => (
                    <button key={m.id || 'any'} type="button" role="radio" aria-checked={staffId === m.id} onClick={() => setStaffId(m.id)}
                      className={`rounded-lg border px-3 py-1.5 text-left text-sm transition-colors ${staffId === m.id ? 'border-inverse bg-inverse text-on-inverse' : 'border-line bg-surface hover:border-line-strong'}`}>
                      <span className="block font-medium">{m.fullName}</span>
                      {m.title && <span className={`block text-xs ${staffId === m.id ? 'opacity-80' : 'text-muted'}`}>{m.title}</span>}
                    </button>
                  ))}
                </div>
              </div>
            )}

            <div>
              <div className="mb-2 text-sm font-semibold">Choose a date</div>
              <div className="scroll-row" style={{ gridAutoColumns: '64px' }}>
                {days.map((d) => {
                  const value = d.format('YYYY-MM-DD');
                  const closed = closedDays.has(d.day());
                  return (
                    <button key={value} type="button" disabled={closed} onClick={() => setDay(value)}
                      className={`rounded-lg border px-1 py-2 text-center text-xs transition-colors disabled:cursor-not-allowed disabled:opacity-40 ${day === value ? 'border-inverse bg-inverse text-on-inverse' : 'border-line bg-surface hover:border-line-strong'}`}>
                      <div className="font-medium">{d.format('ddd')}</div>
                      <div className="text-lg font-bold leading-6">{d.format('D')}</div>
                      <div>{closed ? 'Closed' : d.format('MMM')}</div>
                    </button>
                  );
                })}
              </div>
            </div>

            <div>
              <div className="mb-2 text-sm font-semibold">Available times</div>
              {slots.isLoading ? <Skeleton variant="rounded" height={88} /> : !slots.data?.some((s) => s.available) ? (
                <p className="rounded-lg bg-canvas p-4 text-sm text-muted">No slots left on this day. Please pick another date.</p>
              ) : (
                <div className="grid grid-cols-3 gap-2 sm:grid-cols-5">
                  {slots.data.map((s) => (
                    <button key={s.start} type="button" disabled={!s.available} onClick={() => setSlot(s.start)}
                      className={`rounded-lg border py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:border-transparent disabled:bg-subtle disabled:text-faint disabled:line-through ${slot === s.start ? 'border-inverse bg-inverse text-on-inverse' : 'border-line bg-surface hover:border-line-strong'}`}>
                      {dayjs(s.start).format('h:mm A')}
                    </button>
                  ))}
                </div>
              )}
            </div>

            {needsAddress && <TextField label="Service address" placeholder="Flat, building, street, area" value={address} onChange={(e) => setAddress(e.target.value)} required
              error={address.length > 0 && address.trim().length < 10} helperText="The professional will visit this address" />}
            <TextField label="Notes for the business (optional)" multiline minRows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
            {service && (
              <div className="flex items-center justify-between rounded-lg bg-canvas px-4 py-3 text-sm">
                <span className="text-muted">{service.name} · {service.durationMinutes} min</span>
                <span className="text-base font-bold">{money(service.price)}</span>
              </div>
            )}
          </DialogContent>
          <DialogActions sx={{ px: 3, pb: 2.5 }}>
            <Button onClick={onClose} color="inherit">Cancel</Button>
            <Button variant="contained" disabled={!canSubmit || book.isPending} onClick={() => book.mutate()}>{book.isPending ? 'Booking…' : 'Request booking'}</Button>
          </DialogActions>
        </>
      )}
    </Dialog>
  );
}

/* ---------------- Review ---------------- */
const reviewSchema = z.object({
  rating: z.number().min(1, 'Choose a rating').max(5),
  title: z.string().trim().max(120).optional(),
  comment: z.string().trim().min(20, 'Please write at least 20 characters').max(2000),
});
type ReviewForm = z.infer<typeof reviewSchema>;

export function ReviewDialog({ business, open, onClose }: { business: { id: string; name: string; slug: string }; open: boolean; onClose: () => void }) {
  const { enqueueSnackbar } = useSnackbar();
  const queryClient = useQueryClient();
  const { control, register, handleSubmit, reset, formState: { errors, isSubmitting } } = useForm<ReviewForm>({
    resolver: zodResolver(reviewSchema), defaultValues: { rating: 0, title: '', comment: '' },
  });
  useEffect(() => { if (open) reset({ rating: 0, title: '', comment: '' }); }, [open, reset]);

  const submit = async (v: ReviewForm) => {
    try {
      await api.post<Review>(`/api/businesses/${business.id}/reviews`, v);
      enqueueSnackbar('Thanks! Your review is live.', { variant: 'success' });
      void queryClient.invalidateQueries({ queryKey: ['business', business.slug] });
      void queryClient.invalidateQueries({ queryKey: ['reviews', business.slug] });
      void queryClient.invalidateQueries({ queryKey: ['me', 'bookings'] });
      onClose();
    } catch (e) {
      enqueueSnackbar(errorMessage(e), { variant: 'error' });
    }
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle fontWeight={700}>Review {business.name}</DialogTitle>
      <form onSubmit={handleSubmit(submit)} noValidate>
        <DialogContent className="space-y-4" sx={{ pt: '8px !important' }}>
          <Controller control={control} name="rating" render={({ field }) => (
            <div>
              <MuiRating value={field.value} onChange={(_, v) => field.onChange(v ?? 0)} size="large" />
              {errors.rating && <p className="text-xs text-danger">{errors.rating.message}</p>}
            </div>
          )} />
          <TextField label="Title (optional)" {...register('title')} />
          <TextField label="Your review" multiline minRows={4} placeholder="What went well? What could be better?" {...register('comment')}
            error={!!errors.comment} helperText={errors.comment?.message} />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button onClick={onClose} color="inherit">Cancel</Button>
          <Button type="submit" variant="contained" disabled={isSubmitting}>{isSubmitting ? 'Posting…' : 'Post review'}</Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

/** Opens auth when needed, otherwise runs the action. */
export function useRequireLogin() {
  const user = useAuth((s) => s.user);
  const navigate = useNavigate();
  const location = useLocation();
  return (action: () => void) => {
    if (!user) navigate(`/login?returnUrl=${encodeURIComponent(location.pathname)}`);
    else action();
  };
}
