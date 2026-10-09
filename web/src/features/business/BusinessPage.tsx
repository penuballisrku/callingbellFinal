import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Chip, Dialog, IconButton, LinearProgress, MenuItem, Pagination, Skeleton, TextField, Tooltip } from '@mui/material';
import CallRounded from '@mui/icons-material/CallRounded';
import ChatBubbleOutlineRounded from '@mui/icons-material/ChatBubbleOutlineRounded';
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
import PlayArrowRounded from '@mui/icons-material/PlayArrowRounded';
import { useSnackbar } from 'notistack';
import { api, errorMessage } from '@/lib/api';
import { ago, date, hhmm, money, number } from '@/lib/format';
import { useDocumentTitle, useLookup } from '@/lib/hooks';
import { usePresence } from '@/lib/realtime';
import type { BusinessDetail, BusinessImage, Review } from '@/lib/types';
import { BusinessCard } from '@/components/BusinessCard';
import { AvailabilityBadge, EmptyState, ErrorState, Img, Panel, Rating, Stars, VerifiedMark } from '@/components/ui';
import { BookingDialog, EnquiryDialog, ReviewDialog, useRequireLogin } from './Dialogs';
import { ChatDrawer } from '@/features/chat/Chat';
import type { TeamMember } from '@/lib/types';
import { Breadcrumbs, FaqSection, LinkGroups, useSeoPage } from '@/features/seo/seo';

export default function BusinessPage() {
  const platforms = useLookup('SocialPlatform');
  const { slug = '' } = useParams();
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const requireLogin = useRequireLogin();
  const serviceTypes = useLookup('ServiceType');
  const { data: b, isLoading, isError, error, refetch } = useQuery({ queryKey: ['business', slug], queryFn: () => api.get<BusinessDetail>(`/api/businesses/${slug}`) });
  useDocumentTitle(b ? `${b.card.name} - ${b.card.area}, ${b.card.city}` : undefined);
  // Breadcrumbs, the factual summary, questions and related links (the same ones the server renders for crawlers).
  const { data: seo } = useSeoPage();
  const navigate = useNavigate();
  // A renamed business: its old address moves to the new one.
  useEffect(() => { if (seo?.redirectTo) navigate(seo.redirectTo, { replace: true }); }, [seo?.redirectTo, navigate]);

  const [chatOpen, setChatOpen] = useState(false);
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

  if (seo?.kind === 'Gone' || seo?.kind === 'NotFound') {
    return (
      <div className="container-page py-20">
        <EmptyState title={seo.content.heading ?? 'Business not found'} message={seo.content.summary ?? undefined}
          action={<Button component={Link} to="/categories" variant="contained">Browse categories</Button>} />
      </div>
    );
  }
  if (isError) {
    return <div className="container-page py-16"><ErrorState message={errorMessage(error)} onRetry={() => refetch()} /></div>;
  }
  if (isLoading || !b) return <BusinessSkeleton />;

  const c = b.card;
  const typeName = (code: string) => serviceTypes.find((t) => t.code === code)?.name ?? code;
  const totalReviews = Object.values(b.ratingBreakdown).reduce((a, n) => a + n, 0);
  const mapUrl = b.latitude && b.longitude ? `https://www.google.com/maps/search/?api=1&query=${b.latitude},${b.longitude}` : undefined;
  const today = b.hours.find((h) => h.isToday);

  const kind = c.subCategoryName ?? c.categoryName;
  // Without repeating a part the street line already holds ("Gokhale Road, Dadar West" + "Dadar West").
  const address = [b.addressLine, c.area, c.city, b.state, b.pincode].reduce<string[]>((parts, p) => {
    const part = p?.trim();
    return part && !parts.some((x) => x.toLowerCase().includes(part.toLowerCase())) ? [...parts, part] : parts;
  }, []).join(', ');
  const near = b.landmark && (/^near\s/i.test(b.landmark) ? b.landmark : `Near ${b.landmark}`);

  return (
    <div className="pb-24 md:pb-8">
      <article>
      <div className="relative bg-navy">
        <Img src={b.images.find((i) => i.isPrimary)?.desktopImageUrl ?? c.coverImageUrl} alt={`${c.name}, ${kind} in ${c.city}`} rounded="rounded-none"
          className="mx-auto h-[180px] w-full max-w-[1240px] md:h-[300px]" fallbackText={c.name} eager width={1240} height={300} />
      </div>

      <div className="container-page">
        <header className="relative flex flex-col gap-4 md:flex-row md:items-start md:gap-5">
          <div className="-mt-12 w-fit shrink-0 rounded-2xl border-4 border-surface bg-surface md:-mt-16">
            <Img src={c.logoUrl} alt={`${c.name} logo`} className="h-24 w-24 md:h-32 md:w-32" rounded="rounded-xl" fallbackText={c.name} width={128} height={128} />
          </div>
          <div className="min-w-0 flex-1 md:pt-4">
            {seo?.document.breadcrumbs.length ? <Breadcrumbs items={seo.document.breadcrumbs} className="mb-1" /> : (
              <nav className="mb-1 text-xs text-muted" aria-label="Breadcrumb">
                <Link to="/" className="hover:underline">Home</Link> / <Link to={`/category/${c.subCategorySlug ?? c.categorySlug}`} className="hover:underline">{kind}</Link>
              </nav>
            )}
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-2xl font-bold tracking-tight md:text-3xl">{c.name}</h1>
              {c.isVerified && <VerifiedMark />}
              {c.isFeatured && <Chip size="small" label="Featured" sx={{ bgcolor: 'var(--cb-accent-soft)', color: 'var(--cb-accent-ink)' }} />}
            </div>
            {c.tagline && <p className="mt-0.5 text-muted">{c.tagline}</p>}
            <p className="mt-0.5 text-sm text-muted">{kind}{c.area ? ` · ${c.area}` : ''}, {c.city}</p>
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
            <Button variant="outlined" startIcon={<ChatBubbleOutlineRounded />} onClick={() => requireLogin(() => setChatOpen(true))}>Chat</Button>
            {c.acceptsOnlineBooking
              ? <Button variant="contained" startIcon={<EventAvailableRounded />} onClick={() => requireLogin(() => setBooking({}))}>Book now</Button>
              : <Button variant="contained" startIcon={<RequestQuoteOutlined />} onClick={() => setEnquiry({ type: 'Quotation' })}>Get a quote</Button>}
          </div>
        </header>

        <div className="mt-8 grid gap-6 lg:grid-cols-[1fr_340px]">
          <div className="min-w-0 space-y-6">
            <Panel title={seo?.content.heading ?? `About ${c.name}`} headingLevel="h2">
              {seo?.content.summary && <p className="mb-3 leading-7 text-ink">{seo.content.summary}</p>}
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

            <Panel title="Services & pricing" subtitle={`${b.services.length} services`} noPad headingLevel="h2">
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

            <TeamSection businessId={c.id} services={b.services} />

            {b.images.length > 0 && (
              <Panel title="Photos" headingLevel="h2">
                <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                  {b.images.map((img, i) => (
                    <button key={img.id} type="button" onClick={() => setLightbox(img)} className="group overflow-hidden rounded-lg text-left" aria-label={`Open photo: ${img.caption ?? img.altText ?? `${c.name} photo ${i + 1}`}`}>
                      <Img src={img.thumbnailUrl ?? img.imageUrl} alt={img.altText || img.caption || `${c.name}, ${kind} in ${c.city} - photo ${i + 1}`} aspect="4/3"
                        className="w-full transition-transform group-hover:scale-[1.02]" width={320} height={240} />
                      {img.caption && <span className="mt-1 block truncate text-xs text-muted">{img.caption}</span>}
                    </button>
                  ))}
                </div>
              </Panel>
            )}

            {b.videos?.length > 0 && (
              <Panel headingLevel="h2" title="Videos" subtitle={`${b.videos.length} video${b.videos.length === 1 ? '' : 's'} from ${c.name}`}>
                <div className="grid gap-4 sm:grid-cols-2">{b.videos.map((v) => <ProfileVideo key={v.id} title={v.title} src={v.videoUrl} poster={v.posterUrl} />)}</div>
              </Panel>
            )}

            <ReviewsSection b={b} totalReviews={totalReviews} onWrite={() => requireLogin(() => setReviewOpen(true))} />
          </div>

          <aside className="space-y-6">
            <Panel title="Contact" headingLevel="h2">
              <div className="space-y-3 text-sm">
                <div className="grid grid-cols-2 gap-2">
                  {b.phoneNumber && <Button variant="outlined" startIcon={<CallRounded />} href={`tel:${b.phoneNumber.replace(/\s/g, '')}`}>Call</Button>}
                  {b.whatsAppNumber && <Button variant="outlined" startIcon={<WhatsApp />} href={`https://wa.me/${b.whatsAppNumber.replace(/\D/g, '')}`} target="_blank" rel="noopener">WhatsApp</Button>}
                  <Button variant="outlined" startIcon={<ChatBubbleOutlineRounded />} onClick={() => requireLogin(() => setChatOpen(true))}>Chat</Button>
                  <Button variant="outlined" startIcon={<RequestQuoteOutlined />} onClick={() => setEnquiry({ type: 'Quotation' })}>Get quote</Button>
                  <Button variant="outlined" startIcon={<PhoneCallbackOutlined />} onClick={() => setEnquiry({ type: 'Callback' })}>Callback</Button>
                </div>
                {c.acceptsOnlineBooking && <Button fullWidth variant="contained" size="large" startIcon={<EventAvailableRounded />} onClick={() => requireLogin(() => setBooking({}))}>Book an appointment</Button>}
                <div className="space-y-2.5 border-t border-line pt-3">
                  {b.phoneNumber && <Row icon={<CallRounded fontSize="small" />}>{b.phoneNumber}</Row>}
                  {b.email && <Row icon={<MailOutlineRounded fontSize="small" />}><a href={`mailto:${b.email}`} className="break-all hover:underline">{b.email}</a></Row>}
                  {b.website && <Row icon={<LanguageOutlined fontSize="small" />}><a href={b.website} target="_blank" rel="noopener" className="break-all hover:underline">{b.website.replace(/^https?:\/\//, '')}</a></Row>}
                  {b.socialLinks?.length > 0 && (
                    <div className="flex flex-wrap gap-1.5 pt-1" aria-label="Social media">
                      {b.socialLinks.map((l) => (
                        <a key={l.platform} href={l.url} target="_blank" rel="noopener nofollow ugc"
                          className="rounded-md border border-line px-2 py-1 text-xs font-semibold text-ink-2 transition-colors hover:border-line-strong hover:bg-subtle">
                          {platforms.find((p) => p.code === l.platform)?.name ?? l.platform}
                        </a>
                      ))}
                    </div>
                  )}
                  <Row icon={<PlaceOutlined fontSize="small" />}><address className="not-italic">{address}{near && <span className="block text-muted">{near}</span>}</address></Row>
                </div>
                {mapUrl && <Button fullWidth startIcon={<DirectionsOutlined />} href={mapUrl} target="_blank" rel="noopener">Get directions</Button>}
              </div>
            </Panel>

            <Panel title="Working hours" headingLevel="h2" action={<ScheduleOutlined fontSize="small" sx={{ color: 'var(--cb-faint)' }} />}>
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

        {seo && <FaqSection items={seo.content.faq} className="mt-10" />}

        {b.similar.length > 0 && (
          <section className="mt-12" aria-labelledby="similar-h">
            <h2 id="similar-h" className="mb-4 text-xl font-bold">Similar {c.subCategoryName?.toLowerCase()} you may like</h2>
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">{b.similar.map((s) => <BusinessCard key={s.id} b={s} />)}</div>
          </section>
        )}

        {seo && <LinkGroups groups={seo.content.links.filter((g) => !g.title.startsWith('Other '))} />}
        {seo?.content.updatedOn && (
          <p className="mt-8 text-xs text-muted">Business information last updated: <time dateTime={seo.content.updatedOn}>{date(seo.content.updatedOn)}</time></p>
        )}
      </div>
      </article>

      {/* Mobile action bar */}
      <div className="fixed inset-x-0 bottom-0 z-30 flex gap-2 border-t border-line bg-surface p-3 md:hidden">
        {b.phoneNumber && <Button variant="outlined" href={`tel:${b.phoneNumber.replace(/\s/g, '')}`} startIcon={<CallRounded />}>Call</Button>}
        <Button variant="outlined" startIcon={<ChatBubbleOutlineRounded />} onClick={() => requireLogin(() => setChatOpen(true))}>Chat</Button>
        {c.acceptsOnlineBooking ? <Button fullWidth variant="contained" onClick={() => requireLogin(() => setBooking({}))}>Book now</Button>
          : <Button fullWidth variant="contained" onClick={() => setEnquiry({ type: 'Enquiry' })}>Enquire</Button>}
      </div>

      <EnquiryDialog business={b} open={!!enquiry} initialType={enquiry?.type} serviceId={enquiry?.serviceId} onClose={() => setEnquiry(null)} />
      {booking && <BookingDialog business={b} open serviceId={booking.serviceId} onClose={() => setBooking(null)} />}
      {chatOpen && <ChatDrawer businessId={c.id} open onClose={() => setChatOpen(false)} />}
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
    <Panel title="Ratings & reviews" headingLevel="h2" action={<Button size="small" variant="outlined" onClick={onWrite}>Write a review</Button>}>
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

/** Poster first; the video streams only after the visitor presses play. */
function ProfileVideo({ title, src, poster }: { title: string; src: string; poster?: string | null }) {
  const [play, setPlay] = useState(false);
  return (
    <figure className="min-w-0">
      <div className="overflow-hidden rounded-lg border border-line bg-black">
        {play ? <video src={src} poster={poster ?? undefined} controls autoPlay playsInline className="aspect-video w-full" aria-label={title} />
          : (
            <button type="button" onClick={() => setPlay(true)} className="group relative block w-full" aria-label={`Play video: ${title}`}>
              <Img src={poster} alt="" aspect="16/9" rounded="rounded-none" className="w-full opacity-90" fallbackText={title} />
              <span className="absolute inset-0 flex items-center justify-center">
                <span className="flex h-12 w-12 items-center justify-center rounded-full bg-white/95 text-navy shadow-lg transition-transform group-hover:scale-105"><PlayArrowRounded /></span>
              </span>
            </button>
          )}
      </div>
      <figcaption className="mt-1.5 truncate text-sm font-medium">{title}</figcaption>
    </figure>
  );
}

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

/** "Our team": who works here, what they do and their experience (no contact details). Hidden when the business lists no team. */
function TeamSection({ businessId, services }: { businessId: string; services: { id: string; name: string }[] }) {
  const team = useQuery({ queryKey: ['team', businessId], queryFn: () => api.get<TeamMember[]>(`/api/businesses/${businessId}/team`), staleTime: 5 * 60_000 });
  if (!team.data?.length) return null;
  return (
    <Panel title="Our team" subtitle={`${team.data.length} ${team.data.length === 1 ? 'professional' : 'professionals'}`} headingLevel="h2">
      <ul className="grid gap-3 sm:grid-cols-2">
        {team.data.map((m) => {
          const does = services.filter((s) => m.serviceIds.includes(s.id)).map((s) => s.name);
          return (
            <li key={m.id} className="flex gap-3 rounded-lg border border-line p-3">
              <span aria-hidden className="grid h-11 w-11 shrink-0 place-items-center rounded-full bg-accent-soft text-sm font-bold text-accent-ink">
                {m.fullName.split(' ').filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('')}
              </span>
              <div className="min-w-0">
                <p className="font-semibold leading-tight">{m.fullName}</p>
                <p className="text-sm text-muted">{[m.title, m.yearsExperience ? `${m.yearsExperience} yrs experience` : null].filter(Boolean).join(' · ')}</p>
                {m.languages && <p className="text-xs text-muted">Speaks {m.languages}</p>}
                {does.length > 0 && <p className="mt-1 line-clamp-2 text-xs text-ink-2">{does.join(', ')}</p>}
              </div>
            </li>
          );
        })}
      </ul>
    </Panel>
  );
}
