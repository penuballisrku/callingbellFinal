import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Chip, Dialog, IconButton, LinearProgress, MenuItem, Pagination, Skeleton, TextField, Tooltip } from '@mui/material';
import CallRounded from '@mui/icons-material/CallRounded';
import WhatsApp from '@mui/icons-material/WhatsApp';
import EventAvailableRounded from '@mui/icons-material/EventAvailableRounded';
import RequestQuoteOutlined from '@mui/icons-material/RequestQuoteOutlined';
import PhoneCallbackOutlined from '@mui/icons-material/PhoneCallbackOutlined';
import FavoriteBorderRounded from '@mui/icons-material/FavoriteBorderRounded';
import FavoriteRounded from '@mui/icons-material/FavoriteRounded';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import LanguageOutlined from '@mui/icons-material/LanguageOutlined';
import MailOutlineRounded from '@mui/icons-material/MailOutlineRounded';
import DirectionsOutlined from '@mui/icons-material/DirectionsOutlined';
import ScheduleOutlined from '@mui/icons-material/ScheduleOutlined';
import VerifiedUserOutlined from '@mui/icons-material/VerifiedUserOutlined';
import CloseRounded from '@mui/icons-material/CloseRounded';
import { useSnackbar } from 'notistack';
import { api, errorMessage } from '@/lib/api';
import { ago, date, hhmm, money, number } from '@/lib/format';
import { useDocumentTitle, useLookup } from '@/lib/hooks';
import { usePresence } from '@/lib/realtime';
import type { BusinessDetail, BusinessImage, Review } from '@/lib/types';
import { BusinessCard } from '@/components/BusinessCard';
import { AvailabilityBadge, EmptyState, ErrorState, Img, Panel, Rating, Stars, VerifiedMark } from '@/components/ui';
import { BookingDialog, EnquiryDialog, ReviewDialog, useRequireLogin } from './Dialogs';

export default function BusinessPage() {
  const { slug = '' } = useParams();
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const requireLogin = useRequireLogin();
  const serviceTypes = useLookup('ServiceType');
  const { data: b, isLoading, isError, error, refetch } = useQuery({ queryKey: ['business', slug], queryFn: () => api.get<BusinessDetail>(`/api/businesses/${slug}`) });
  useDocumentTitle(b ? `${b.card.name} - ${b.card.area}, ${b.card.city}` : undefined);

  const [enquiry, setEnquiry] = useState<{ type: 'Enquiry' | 'Quotation' | 'Callback'; serviceId?: string } | null>(null);
  const [booking, setBooking] = useState<{ serviceId?: string } | null>(null);
  const [reviewOpen, setReviewOpen] = useState(false);
  const [lightbox, setLightbox] = useState<BusinessImage | null>(null);

  usePresence(b ? [b.card.id] : [], (e) => {
    queryClient.setQueryData<BusinessDetail>(['business', slug], (old) => old && { ...old, card: { ...old.card, availabilityStatus: e.status, lastSeenOn: e.lastSeenOn } });
  });

  const favorite = useMutation({
    mutationFn: () => api.post<boolean>(`/api/businesses/${b!.card.id}/favorite`),
    onSuccess: (res) => {
      queryClient.setQueryData<BusinessDetail>(['business', slug], (old) => old && { ...old, isFavorite: res.data });
      enqueueSnackbar(res.message, { variant: 'success' });
      void queryClient.invalidateQueries({ queryKey: ['me', 'favorites'] });
    },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });

  if (isError) {
    return <div className="container-page py-16"><ErrorState message={errorMessage(error)} onRetry={() => refetch()} /></div>;
  }
  if (isLoading || !b) return <BusinessSkeleton />;

  const c = b.card;
  const typeName = (code: string) => serviceTypes.find((t) => t.code === code)?.name ?? code;
  const totalReviews = Object.values(b.ratingBreakdown).reduce((a, n) => a + n, 0);
  const mapUrl = b.latitude && b.longitude ? `https://www.google.com/maps/search/?api=1&query=${b.latitude},${b.longitude}` : undefined;
  const today = b.hours.find((h) => h.isToday);

  return (
    <div className="pb-24 md:pb-8">
      <div className="relative bg-navy">
        <Img src={b.images.find((i) => i.isPrimary)?.desktopImageUrl ?? c.coverImageUrl} alt={`${c.name} cover`} rounded="rounded-none"
          className="mx-auto h-[180px] w-full max-w-[1240px] md:h-[300px]" fallbackText={c.name} eager />
      </div>

      <div className="container-page">
        <div className="relative flex flex-col gap-4 md:flex-row md:items-start md:gap-5">
          <div className="-mt-12 w-fit shrink-0 rounded-2xl border-4 border-surface bg-surface md:-mt-16">
            <Img src={c.logoUrl} alt={`${c.name} logo`} className="h-24 w-24 md:h-32 md:w-32" rounded="rounded-xl" fallbackText={c.name} />
          </div>
          <div className="min-w-0 flex-1 md:pt-4">
            <nav className="mb-1 text-xs text-muted" aria-label="Breadcrumb">
              <Link to="/" className="hover:underline">Home</Link> / <Link to={`/search?sub=${c.subCategorySlug}`} className="hover:underline">{c.subCategoryName}</Link> / <Link to={`/search?sub=${c.subCategorySlug}&city=${b.citySlug}`} className="hover:underline">{c.city}</Link>
            </nav>
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-2xl font-bold tracking-tight md:text-3xl">{c.name}</h1>
              {c.isVerified && <VerifiedMark />}
              {c.isFeatured && <Chip size="small" label="Featured" sx={{ bgcolor: 'var(--cb-accent-soft)', color: 'var(--cb-accent-ink)' }} />}
            </div>
            {c.tagline && <p className="mt-0.5 text-muted">{c.tagline}</p>}
            <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-2 text-sm">
              <Rating value={c.averageRating} count={c.reviewCount} size="md" />
              <AvailabilityBadge status={c.availabilityStatus} lastSeenOn={c.lastSeenOn} />
              <span className={`font-medium ${c.isOpenNow ? 'text-success' : 'text-danger'}`}>{c.isOpenNow ? 'Open now' : 'Closed now'}{today && !today.isClosed ? ` · ${hhmm(today.open)} - ${hhmm(today.close)}` : ''}</span>
              <span className="inline-flex items-center gap-1 text-muted"><PlaceOutlined sx={{ fontSize: 17 }} />{c.area}, {c.city}</span>
            </div>
          </div>
          <div className="hidden gap-2 md:flex md:pt-4">
            <Tooltip title={b.isFavorite ? 'Remove from saved' : 'Save business'}>
              <IconButton onClick={() => requireLogin(() => favorite.mutate())} aria-label="Save business" sx={{ border: '1px solid var(--cb-line)' }}>
                {b.isFavorite ? <FavoriteRounded sx={{ color: '#E34948' }} /> : <FavoriteBorderRounded />}
              </IconButton>
            </Tooltip>
            {b.phoneNumber && <Button variant="outlined" startIcon={<CallRounded />} href={`tel:${b.phoneNumber.replace(/\s/g, '')}`}>Call</Button>}
            {c.acceptsOnlineBooking
              ? <Button variant="contained" startIcon={<EventAvailableRounded />} onClick={() => requireLogin(() => setBooking({}))}>Book now</Button>
              : <Button variant="contained" startIcon={<RequestQuoteOutlined />} onClick={() => setEnquiry({ type: 'Quotation' })}>Get a quote</Button>}
          </div>
        </div>

        <div className="mt-8 grid gap-6 lg:grid-cols-[1fr_340px]">
          <div className="min-w-0 space-y-6">
            <Panel title="About">
              <p className="leading-7 text-ink-2">{b.description}</p>
              <dl className="mt-5 grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
                {b.yearEstablished && <Fact label="In business since" value={String(b.yearEstablished)} />}
                {b.teamSize && <Fact label="Team size" value={`${number(b.teamSize)} people`} />}
                {b.languages && <Fact label="Languages" value={b.languages} />}
                {c.responseTimeMinutes && <Fact label="Typically replies" value={`in ${c.responseTimeMinutes} min`} />}
              </dl>
              <div className="mt-5 flex flex-wrap gap-2">
                {c.offersHomeService && <Chip variant="outlined" label="Home visits" />}
                {c.offersVideoConsultation && <Chip variant="outlined" label="Video consultation" />}
                {c.acceptsOnlineBooking && <Chip variant="outlined" label="Instant online booking" />}
                {c.isVerified && b.verifiedOn && <Chip variant="outlined" icon={<VerifiedUserOutlined />} label={`Verified ${date(b.verifiedOn)}`} />}
              </div>
            </Panel>

            <Panel title="Services & pricing" subtitle={`${b.services.length} services`} noPad>
              <ul className="divide-y divide-line">
                {b.services.map((s) => (
                  <li key={s.id} className="flex flex-col gap-3 p-5 sm:flex-row sm:items-center">
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <h3 className="font-semibold">{s.name}</h3>
                        {s.isPopular && <span className="rounded bg-accent-soft px-1.5 py-0.5 text-[11px] font-semibold text-accent-ink">Popular</span>}
                      </div>
                      <p className="mt-0.5 text-sm text-muted">{s.description}</p>
                      <p className="mt-1 text-xs text-muted">{typeName(s.type)} · approx. {s.durationMinutes >= 60 ? `${Math.round(s.durationMinutes / 60 * 10) / 10} hr` : `${s.durationMinutes} min`}</p>
                    </div>
                    <div className="flex items-center gap-3 sm:flex-col sm:items-end">
                      <div className="text-right"><div className="text-lg font-bold">{money(s.price)}</div>{s.price > 0 && s.priceUnit && <div className="text-xs text-muted">{s.priceUnit}</div>}</div>
                      {c.acceptsOnlineBooking
                        ? <Button size="small" variant="outlined" onClick={() => requireLogin(() => setBooking({ serviceId: s.id }))}>Book</Button>
                        : <Button size="small" variant="outlined" onClick={() => setEnquiry({ type: 'Quotation', serviceId: s.id })}>Enquire</Button>}
                    </div>
                  </li>
                ))}
              </ul>
            </Panel>

            {b.images.length > 0 && (
              <Panel title="Photos">
                <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                  {b.images.map((img) => (
                    <button key={img.id} type="button" onClick={() => setLightbox(img)} className="group overflow-hidden rounded-lg text-left" aria-label={`Open photo: ${img.caption ?? img.altText}`}>
                      <Img src={img.thumbnailUrl ?? img.imageUrl} alt={img.altText ?? c.name} aspect="4/3" className="w-full transition-transform group-hover:scale-[1.02]" />
                      {img.caption && <span className="mt-1 block truncate text-xs text-muted">{img.caption}</span>}
                    </button>
                  ))}
                </div>
              </Panel>
            )}

            <ReviewsSection b={b} totalReviews={totalReviews} onWrite={() => requireLogin(() => setReviewOpen(true))} />
          </div>

          <aside className="space-y-6">
            <Panel title="Contact">
              <div className="space-y-3 text-sm">
                <div className="grid grid-cols-2 gap-2">
                  {b.phoneNumber && <Button variant="outlined" startIcon={<CallRounded />} href={`tel:${b.phoneNumber.replace(/\s/g, '')}`}>Call</Button>}
                  {b.whatsAppNumber && <Button variant="outlined" startIcon={<WhatsApp />} href={`https://wa.me/${b.whatsAppNumber.replace(/\D/g, '')}`} target="_blank" rel="noopener">WhatsApp</Button>}
                  <Button variant="outlined" startIcon={<RequestQuoteOutlined />} onClick={() => setEnquiry({ type: 'Quotation' })}>Get quote</Button>
                  <Button variant="outlined" startIcon={<PhoneCallbackOutlined />} onClick={() => setEnquiry({ type: 'Callback' })}>Callback</Button>
                </div>
                {c.acceptsOnlineBooking && <Button fullWidth variant="contained" size="large" startIcon={<EventAvailableRounded />} onClick={() => requireLogin(() => setBooking({}))}>Book an appointment</Button>}
                <div className="space-y-2.5 border-t border-line pt-3">
                  {b.phoneNumber && <Row icon={<CallRounded fontSize="small" />}>{b.phoneNumber}</Row>}
                  {b.email && <Row icon={<MailOutlineRounded fontSize="small" />}><a href={`mailto:${b.email}`} className="break-all hover:underline">{b.email}</a></Row>}
                  {b.website && <Row icon={<LanguageOutlined fontSize="small" />}><a href={b.website} target="_blank" rel="noopener" className="break-all hover:underline">{b.website.replace(/^https?:\/\//, '')}</a></Row>}
                  <Row icon={<PlaceOutlined fontSize="small" />}>{b.addressLine}, {c.area}, {c.city} {b.pincode}{b.landmark && <span className="block text-muted">{b.landmark}</span>}</Row>
                </div>
                {mapUrl && <Button fullWidth startIcon={<DirectionsOutlined />} href={mapUrl} target="_blank" rel="noopener">Get directions</Button>}
              </div>
            </Panel>

            <Panel title="Working hours" action={<ScheduleOutlined fontSize="small" sx={{ color: 'var(--cb-faint)' }} />}>
              <ul className="space-y-2 text-sm">
                {b.hours.map((h) => (
                  <li key={h.dayOfWeek} className={`flex justify-between ${h.isToday ? 'font-semibold text-ink' : 'text-ink-2'}`}>
                    <span>{h.day}{h.isToday && ' (today)'}</span>
                    <span>{h.isClosed ? 'Closed' : h.open === '00:00' && h.close === '23:59' ? 'Open 24 hours' : `${hhmm(h.open)} - ${hhmm(h.close)}`}</span>
                  </li>
                ))}
              </ul>
            </Panel>
          </aside>
        </div>

        {b.similar.length > 0 && (
          <section className="mt-12">
            <h2 className="mb-4 text-xl font-bold">Similar {c.subCategoryName?.toLowerCase()} you may like</h2>
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">{b.similar.map((s) => <BusinessCard key={s.id} b={s} />)}</div>
          </section>
        )}
      </div>

      {/* Mobile action bar */}
      <div className="fixed inset-x-0 bottom-0 z-30 flex gap-2 border-t border-line bg-surface p-3 md:hidden">
        {b.phoneNumber && <Button variant="outlined" href={`tel:${b.phoneNumber.replace(/\s/g, '')}`} startIcon={<CallRounded />}>Call</Button>}
        <Button variant="outlined" onClick={() => setEnquiry({ type: 'Enquiry' })}>Enquire</Button>
        {c.acceptsOnlineBooking && <Button fullWidth variant="contained" onClick={() => requireLogin(() => setBooking({}))}>Book now</Button>}
      </div>

      <EnquiryDialog business={b} open={!!enquiry} initialType={enquiry?.type} serviceId={enquiry?.serviceId} onClose={() => setEnquiry(null)} />
      {booking && <BookingDialog business={b} open serviceId={booking.serviceId} onClose={() => setBooking(null)} />}
      <ReviewDialog business={{ id: c.id, name: c.name, slug }} open={reviewOpen} onClose={() => setReviewOpen(false)} />
      <Dialog open={!!lightbox} onClose={() => setLightbox(null)} maxWidth="lg">
        {lightbox && (
          <div className="relative">
            <IconButton onClick={() => setLightbox(null)} aria-label="Close photo" sx={{ position: 'absolute', right: 8, top: 8, bgcolor: 'background.paper' }}><CloseRounded /></IconButton>
            <Img src={lightbox.desktopImageUrl ?? lightbox.imageUrl} alt={lightbox.altText ?? c.name} rounded="rounded-none" className="max-h-[80vh] w-full" fit="contain" />
            {lightbox.caption && <p className="px-4 py-3 text-sm">{lightbox.caption}</p>}
          </div>
        )}
      </Dialog>
    </div>
  );
}

function ReviewsSection({ b, totalReviews, onWrite }: { b: BusinessDetail; totalReviews: number; onWrite: () => void }) {
  const [page, setPage] = useState(1);
  const [rating, setRating] = useState<number | ''>('');
  const [sort, setSort] = useState('recent');
  const slug = b.card.slug;
  const { data, isFetching } = useQuery({
    queryKey: ['reviews', slug, page, rating, sort],
    queryFn: () => api.paged<Review>(`/api/businesses/${slug}/reviews`, { page, pageSize: 6, rating: rating || undefined, sort }),
    placeholderData: keepPreviousData,
  });

  return (
    <Panel title="Ratings & reviews" action={<Button size="small" variant="outlined" onClick={onWrite}>Write a review</Button>}>
      {totalReviews === 0 ? <EmptyState title="No reviews yet" message="Be the first to share your experience." /> : (
        <>
          <div className="grid gap-6 sm:grid-cols-[180px_1fr] sm:items-center">
            <div className="text-center sm:text-left">
              <div className="text-5xl font-bold tracking-tight">{b.card.averageRating.toFixed(1)}</div>
              <Stars value={b.card.averageRating} size={20} />
              <div className="text-sm text-muted">{number(totalReviews)} reviews</div>
            </div>
            <ul className="space-y-1.5">
              {[5, 4, 3, 2, 1].map((r) => {
                const n = b.ratingBreakdown[r] ?? 0;
                return (
                  <li key={r}>
                    <button type="button" className="flex w-full items-center gap-3 text-sm" onClick={() => { setRating(rating === r ? '' : r); setPage(1); }} aria-pressed={rating === r}>
                      <span className={`w-6 ${rating === r ? 'font-bold' : ''}`}>{r}★</span>
                      <LinearProgress variant="determinate" value={(n / totalReviews) * 100} sx={{ flex: 1, height: 8, borderRadius: 4, bgcolor: 'var(--cb-subtle)', '& .MuiLinearProgress-bar': { bgcolor: '#F4A62C', borderRadius: 4 } }} />
                      <span className="w-10 text-right tabular-nums text-muted">{n}</span>
                    </button>
                  </li>
                );
              })}
            </ul>
          </div>

          <div className="mt-6 flex flex-wrap items-center justify-between gap-3 border-t border-line pt-5">
            <span className="text-sm text-muted">{rating ? `Showing ${rating}★ reviews` : 'All reviews'}</span>
            <TextField select size="small" value={sort} onChange={(e) => { setSort(e.target.value); setPage(1); }} sx={{ width: 170 }} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Sort reviews' } }}>
              <MenuItem value="recent">Most recent</MenuItem><MenuItem value="highest">Highest rated</MenuItem>
              <MenuItem value="lowest">Lowest rated</MenuItem><MenuItem value="helpful">Most helpful</MenuItem>
            </TextField>
          </div>

          <ul className={`mt-2 divide-y divide-line transition-opacity ${isFetching ? 'opacity-60' : ''}`}>
            {!data ? Array.from({ length: 3 }, (_, i) => <li key={i} className="py-4"><Skeleton height={80} /></li>) : data.items.map((r) => (
              <li key={r.id} className="py-5">
                <div className="flex items-center justify-between gap-3">
                  <div className="flex items-center gap-2"><Stars value={r.rating} /><span className="text-sm font-semibold">{r.title}</span></div>
                  <span className="shrink-0 text-xs text-muted">{ago(r.createdOn)}</span>
                </div>
                <p className="mt-1.5 text-sm leading-6 text-ink-2">{r.comment}</p>
                <p className="mt-1.5 text-xs text-muted">{r.customerName}{r.isVerifiedVisit && ' · Verified booking'}{r.helpfulCount > 0 && ` · ${r.helpfulCount} found helpful`}</p>
                {r.ownerReply && (
                  <div className="mt-3 rounded-lg border-l-2 border-accent bg-canvas px-4 py-3 text-sm">
                    <span className="font-semibold">Response from the business</span>
                    <p className="mt-0.5 text-ink-2">{r.ownerReply}</p>
                  </div>
                )}
              </li>
            ))}
          </ul>
          {data && data.pagination.totalPages > 1 && (
            <div className="mt-4 flex justify-center"><Pagination count={data.pagination.totalPages} page={page} onChange={(_, p) => setPage(p)} shape="rounded" size="small" /></div>
          )}
        </>
      )}
    </Panel>
  );
}

const Fact = ({ label, value }: { label: string; value: string }) => (
  <div><dt className="text-xs text-muted">{label}</dt><dd className="mt-0.5 font-semibold">{value}</dd></div>
);
const Row = ({ icon, children }: { icon: React.ReactNode; children: React.ReactNode }) => (
  <div className="flex gap-2.5"><span className="mt-0.5 text-faint">{icon}</span><div className="min-w-0">{children}</div></div>
);

function BusinessSkeleton() {
  return (
    <div>
      <Skeleton variant="rectangular" sx={{ height: { xs: 180, md: 300 } }} />
      <div className="container-page mt-6 space-y-4">
        <Skeleton width="40%" height={40} /><Skeleton width="60%" />
        <div className="grid gap-6 lg:grid-cols-[1fr_340px]"><Skeleton variant="rounded" height={400} /><Skeleton variant="rounded" height={300} /></div>
      </div>
    </div>
  );
}
