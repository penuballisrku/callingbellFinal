import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Button, Drawer, IconButton, Skeleton, Tooltip, useMediaQuery, type Theme } from '@mui/material';
import CloseRounded from '@mui/icons-material/CloseRounded';
import CallRounded from '@mui/icons-material/CallRounded';
import WhatsApp from '@mui/icons-material/WhatsApp';
import LanguageRounded from '@mui/icons-material/LanguageRounded';
import DirectionsRounded from '@mui/icons-material/DirectionsRounded';
import ShareRounded from '@mui/icons-material/ShareRounded';
import OpenInNewRounded from '@mui/icons-material/OpenInNewRounded';
import StarRounded from '@mui/icons-material/StarRounded';
import MapRounded from '@mui/icons-material/MapRounded';
import AutoAwesomeRounded from '@mui/icons-material/AutoAwesomeRounded';
import { useSnackbar } from 'notistack';
import { api, errorMessage } from '@/lib/api';
import { date, money, number } from '@/lib/format';
import { directionsHref, telHref, whatsAppHref } from '@/lib/contact';
import type { BusinessDetail, ExternalPlace, PlaceDetails } from '@/lib/types';
import { AvailabilityBadge, CallingBellBadge, ErrorState, Img, Rating } from '@/components/ui';
import { ClaimBusinessBanner } from './ExternalResults';
import { coordinates, ExternalLink, Field, PhotoCredit, prettyUrl, Section, SocialLinks } from '@/features/places/PlaceParts';

/** The search result whose details are shown: a Calling Bell business (by slug) or a Google Maps / AI recommended place. */
export type SelectedResult =
  | { kind: 'business'; slug: string; name: string }
  | { kind: 'place'; source: 'google' | 'ai'; place: ExternalPlace };

/**
 * A search result's full details in a side panel (full screen on phones), over the search results, which stay as they were: closing it
 * returns to the same results, filters, tab and scroll position. Details are fetched only when a result is opened, and cached, so opening
 * the same result again is instant.
 */
export function ResultDetailDrawer({ selected, onClose }: { selected: SelectedResult | null; onClose: () => void }) {
  const phone = useMediaQuery((theme: Theme) => theme.breakpoints.down('sm'));
  const title = selected ? (selected.kind === 'business' ? selected.name : selected.place.name) : '';
  return (
    <Drawer anchor="right" open={!!selected} onClose={onClose}
      slotProps={{ paper: { sx: { width: phone ? '100%' : { sm: 560, md: 640 }, maxWidth: '100%', bgcolor: 'var(--cb-canvas)' }, role: 'dialog', 'aria-label': `${title} details` } }}>
      {selected && (
        <div className="flex h-full flex-col">
          <header className="sticky top-0 z-10 flex items-center gap-2 border-b border-line bg-surface px-4 py-3">
            <div className="min-w-0 flex-1">
              <h2 className="truncate text-base font-semibold">{title}</h2>
              <p className="truncate text-xs text-muted">
                {selected.kind === 'business' ? 'Registered on Calling Bell' : selected.source === 'google' ? 'From Google Maps · not on Calling Bell' : 'AI recommended · not on Calling Bell'}
              </p>
            </div>
            <Tooltip title="Close"><IconButton onClick={onClose} aria-label="Close details" edge="end"><CloseRounded /></IconButton></Tooltip>
          </header>
          <div className="flex-1 overflow-y-auto">
            {selected.kind === 'business' ? <BusinessDetailView slug={selected.slug} /> : <PlaceDetailView source={selected.source} place={selected.place} />}
          </div>
        </div>
      )}
    </Drawer>
  );
}

/* ---------- Calling Bell business ---------- */

let regionNames: Intl.DisplayNames | null = null;
const countryName = (code?: string | null) => {
  if (!code) return null;
  try { regionNames ??= new Intl.DisplayNames(['en'], { type: 'region' }); return regionNames.of(code) ?? code; } catch { return code; }
};

function BusinessDetailView({ slug }: { slug: string }) {
  // Same query as the business page (and the cards' hover preloading), so either one's data serves the other.
  const { data: b, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['business', slug],
    queryFn: () => api.get<BusinessDetail>(`/api/businesses/${slug}`),
    staleTime: 60_000,
  });
  if (isLoading) return <DetailSkeleton />;
  if (isError || !b) return <div className="p-6"><ErrorState message={errorMessage(error)} onRetry={() => void refetch()} /></div>;

  const c = b.card;
  const whatsApp = whatsAppHref(b.whatsAppNumber ?? b.phoneNumber, c.name);
  const directions = directionsHref(b.latitude, b.longitude, [c.name, b.addressLine, c.city].filter(Boolean).join(', '));
  return (
    <div>
      <div className="relative">
        <Img src={c.coverImageUrl} alt={`${c.name} cover`} aspect="16/7" rounded="rounded-none" className="w-full" fallbackText={c.name} eager />
        <div className="absolute -bottom-7 left-4 rounded-xl border-2 border-surface bg-surface">
          <Img src={c.logoUrl} alt={`${c.name} logo`} className="h-14 w-14" rounded="rounded-[10px]" fallbackText={c.name} />
        </div>
      </div>
      <div className="space-y-5 px-4 pb-8 pt-10">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <h3 className="text-xl font-bold tracking-tight">{c.name}</h3>
            <CallingBellBadge verified={c.isVerified} />
          </div>
          <p className="mt-0.5 text-sm text-muted">{[c.categoryName, c.subCategoryName].filter(Boolean).join(' · ')}</p>
          <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
            <Rating value={c.averageRating} count={c.reviewCount} />
            <AvailabilityBadge status={c.availabilityStatus} lastSeenOn={c.lastSeenOn} compact />
            <span className={c.isOpenNow ? 'font-semibold text-success' : 'text-muted'}>{c.isOpenNow ? 'Open now' : 'Closed now'}</span>
          </div>
        </div>

        <Actions name={c.name} phone={b.phoneNumber} whatsApp={whatsApp} website={b.website} directions={directions}
          shareUrl={`${window.location.origin}/business/${c.slug}`}
          extra={<Button size="small" component={Link} to={`/business/${c.slug}`} endIcon={<OpenInNewRounded />}>Full page</Button>} />

        <Section title="About">
          {b.description && <p className="whitespace-pre-line text-sm leading-relaxed text-ink-2">{b.description}</p>}
          {c.tagline && <p className="text-sm italic text-muted">{c.tagline}</p>}
        </Section>

        <Section title="Contact">
          <Field label="Phone" value={b.phoneNumber && <a href={telHref(b.phoneNumber)} className="font-semibold hover:underline">{b.phoneNumber}</a>} />
          <Field label="WhatsApp" value={b.whatsAppNumber} />
          <Field label="Email" value={b.email && <a href={`mailto:${b.email}`} className="hover:underline">{b.email}</a>} />
          <Field label="Website" value={b.website && <ExternalLink href={b.website}>{prettyUrl(b.website)}</ExternalLink>} />
          {b.socialLinks.length > 0 && <Field label="Social" value={<SocialLinks links={b.socialLinks} />} />}
        </Section>

        <Section title="Location">
          <Field label="Address" value={b.addressLine} />
          <Field label="Landmark" value={b.landmark} />
          <Field label="Area" value={c.area} />
          <Field label="City" value={c.city} />
          <Field label="State" value={b.state} />
          <Field label="Country" value={countryName(b.countryCode)} />
          <Field label="PIN code" value={b.pincode} />
          <Field label="Coordinates" value={coordinates(b.latitude, b.longitude)} />
        </Section>

        {b.hours.length > 0 && (
          <Section title="Opening hours">
            <ul className="divide-y divide-line rounded-xl border border-line bg-surface text-sm">
              {b.hours.map((h) => (
                <li key={h.dayOfWeek} className={`flex justify-between px-3 py-2 ${h.isToday ? 'font-semibold' : ''}`}>
                  <span>{h.day}{h.isToday && <span className="ml-1.5 text-xs font-normal text-muted">today</span>}</span>
                  <span className={h.isClosed ? 'text-muted' : ''}>{h.isClosed || !h.open ? 'Closed' : `${h.open} – ${h.close}`}</span>
                </li>
              ))}
            </ul>
          </Section>
        )}

        {b.services.length > 0 && (
          <Section title={`Services (${number(b.services.length)})`}>
            <ul className="divide-y divide-line rounded-xl border border-line bg-surface">
              {b.services.map((s) => (
                <li key={s.id} className="flex items-start justify-between gap-3 px-3 py-2.5">
                  <div className="min-w-0">
                    <div className="text-sm font-semibold">{s.name}{s.isPopular && <span className="ml-1.5 rounded bg-accent-soft px-1.5 py-0.5 text-[10px] font-semibold text-accent-ink">Popular</span>}</div>
                    {s.description && <p className="line-clamp-2 text-xs text-muted">{s.description}</p>}
                    <p className="text-xs text-muted">{[s.type, s.durationMinutes ? `${s.durationMinutes} min` : null].filter(Boolean).join(' · ')}</p>
                  </div>
                  <div className="shrink-0 text-right text-sm font-semibold">{money(s.price)}{s.price > 0 && s.priceUnit && <div className="text-xs font-normal text-muted">{s.priceUnit}</div>}</div>
                </li>
              ))}
            </ul>
          </Section>
        )}

        {b.images.length > 0 && (
          <Section title={`Photos (${number(b.images.length)})`}>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
              {b.images.map((i) => <Img key={i.id} src={i.thumbnailUrl ?? i.imageUrl} alt={i.altText ?? i.caption ?? c.name} aspect="4/3" rounded="rounded-lg" className="w-full" fallbackText={c.name} />)}
            </div>
          </Section>
        )}

        <Section title="Business details">
          <Field label="Established" value={b.yearEstablished} />
          <Field label="Team size" value={b.teamSize ? `${number(b.teamSize)} people` : null} />
          <Field label="Languages" value={b.languages} />
          <Field label="Plan" value={b.planName} />
          <Field label="Verified on" value={b.verifiedOn ? date(b.verifiedOn) : null} />
          <Field label="Business ID" value={<code className="break-all text-xs">{c.id}</code>} />
          <Field label="Source" value="Calling Bell" />
        </Section>
      </div>
    </div>
  );
}

/* ---------- Google Maps / AI recommended place ---------- */

function PlaceDetailView({ source, place }: { source: 'google' | 'ai'; place: ExternalPlace }) {
  // Result ids are "google:<place id>" or "osm:node/123".
  const [kind, ...rest] = place.id.split(':');
  const id = rest.join(':');
  const apiSource = kind === 'google' ? 'google' : 'osm';
  const { data: d, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['place-details', apiSource, id],
    queryFn: () => api.get<PlaceDetails>('/api/places/details',
      { source: apiSource, id, name: apiSource === 'osm' ? place.name : null, lat: place.latitude, lon: place.longitude }),
    // Kept for this visit only (in memory, never saved to the browser): Google's terms don't allow storing its data.
    staleTime: Infinity,
    gcTime: 30 * 60_000,
    retry: 1,
  });
  if (isLoading) return <DetailSkeleton />;
  if (isError || !d) return <div className="p-6"><ErrorState message={errorMessage(error)} onRetry={() => void refetch()} /></div>;

  const phone = d.phone ?? d.mobile;
  const whatsApp = whatsAppHref(d.mobile ?? d.phone, d.name);
  const directions = directionsHref(d.latitude, d.longitude, [d.name, d.address].filter(Boolean).join(', '));
  const [hero, ...more] = d.photos;
  return (
    <div>
      {hero ? (
        <figure className="relative">
          <Img src={hero.url} alt={`Photo of ${d.name}`} aspect="16/9" rounded="rounded-none" className="w-full" fallbackText={d.name} eager />
          <PhotoCredit photo={hero} className="absolute bottom-1 right-2 rounded bg-black/50 px-1.5 text-white/90" />
        </figure>
      ) : null}
      <div className="space-y-5 px-4 pb-8 pt-4">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <h3 className="text-xl font-bold tracking-tight">{d.name}</h3>
            <span className="inline-flex items-center gap-1 rounded-full border border-dashed border-line-strong px-2 py-0.5 text-[11px] font-medium text-muted">
              {source === 'ai' ? <AutoAwesomeRounded sx={{ fontSize: 12 }} className="text-accent-ink" /> : <MapRounded sx={{ fontSize: 12 }} className="text-info" />}
              Not on Calling Bell
            </span>
          </div>
          {d.category && <p className="mt-0.5 text-sm text-muted">{d.category}</p>}
          <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
            {d.rating != null && d.ratingCount ? (
              <span className="inline-flex items-center gap-1">
                <StarRounded sx={{ fontSize: 17, color: '#F4A62C' }} aria-hidden />
                <span className="font-semibold tabular">{d.rating.toFixed(1)}</span>
                <span className="text-muted">({number(d.ratingCount)} reviews)</span>
              </span>
            ) : <span className="text-muted">No reviews yet</span>}
            {d.openNow != null && <span className={`font-semibold ${d.openNow ? 'text-success' : 'text-danger'}`}>{d.openNow ? 'Open now' : 'Closed now'}</span>}
            {d.businessStatus && d.businessStatus !== 'Open for business' && <span className="font-semibold text-danger">{d.businessStatus}</span>}
            {d.priceLevel && <span className="text-muted">{d.priceLevel}</span>}
          </div>
        </div>

        <Actions name={d.name} phone={phone} whatsApp={whatsApp} website={d.website} directions={directions} shareUrl={d.mapsUrl ?? d.sourceUrl}
          extra={<>
            {/* A Google Maps place also has its own page (shareable, works without the results). */}
            {d.source === 'google' && <Button size="small" component={Link} to={`/nearby/place/${encodeURIComponent(d.sourceId)}`} endIcon={<OpenInNewRounded />}>Full page</Button>}
            {d.mapsUrl
              ? <Button size="small" href={d.mapsUrl} target="_blank" rel="noreferrer" endIcon={<OpenInNewRounded />}>Google Maps</Button>
              : d.sourceUrl ? <Button size="small" href={d.sourceUrl} target="_blank" rel="noreferrer" endIcon={<OpenInNewRounded />}>OpenStreetMap</Button> : null}
          </>} />

        <Section title="About">
          {d.description && <p className="text-sm leading-relaxed text-ink-2">{d.description}</p>}
          {d.tags.length > 0 && (
            <div className="flex flex-wrap gap-1.5">{d.tags.map((t) => <span key={t} className="rounded-full border border-line bg-surface px-2.5 py-0.5 text-xs text-ink-2">{t}</span>)}</div>
          )}
        </Section>

        <Section title="Contact">
          <Field label="Phone" value={d.phone && <a href={telHref(d.phone)} className="font-semibold hover:underline">{d.phone}</a>} />
          <Field label="International" value={d.internationalPhone !== d.phone ? d.internationalPhone : null} />
          <Field label="Mobile" value={d.mobile && <a href={telHref(d.mobile)} className="hover:underline">{d.mobile}</a>} />
          <Field label="Email" value={d.email && <a href={`mailto:${d.email}`} className="hover:underline">{d.email}</a>} />
          <Field label="Website" value={d.website && <ExternalLink href={d.website}>{prettyUrl(d.website)}</ExternalLink>} />
          {d.socialLinks.length > 0 && <Field label="Social" value={<SocialLinks links={d.socialLinks} />} />}
        </Section>

        <Section title="Location">
          <Field label="Address" value={d.address} />
          <Field label="City" value={d.city} />
          <Field label="State" value={d.state} />
          <Field label="Country" value={d.country} />
          <Field label="Postal code" value={d.postalCode} />
          <Field label="Coordinates" value={coordinates(d.latitude, d.longitude)} />
          <Field label="Distance" value={Number.isFinite(place.distanceKm) ? `${place.distanceKm.toFixed(1)} km from the searched location` : null} />
        </Section>

        {d.openingHours.length > 0 && (
          <Section title="Opening hours">
            <ul className="divide-y divide-line rounded-xl border border-line bg-surface text-sm">
              {d.openingHours.map((h) => <li key={h} className="px-3 py-2">{h}</li>)}
            </ul>
          </Section>
        )}

        {more.length > 0 && (
          <Section title={`More photos (${number(more.length)})`}>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
              {more.map((ph) => (
                <figure key={ph.url}>
                  <Img src={ph.url} alt={`Photo of ${d.name}`} aspect="4/3" rounded="rounded-lg" className="w-full" fallbackText={d.name} />
                  <PhotoCredit photo={ph} className="mt-0.5 text-faint" />
                </figure>
              ))}
            </div>
          </Section>
        )}

        {d.otherFields.length > 0 && (
          <Section title="More details">
            {d.otherFields.map((f) => <Field key={f.label} label={f.label} value={f.value} />)}
          </Section>
        )}

        <Section title="Source">
          <Field label="Source" value={d.source === 'google' ? 'Google Maps' : 'OpenStreetMap'} />
          <Field label="Source ID" value={<code className="break-all text-xs">{d.sourceId}</code>} />
          <Field label="Google place ID" value={d.source === 'osm' && d.googlePlaceId ? <code className="break-all text-xs">{d.googlePlaceId}</code> : null} />
          <Field label="Details from" value={d.dataSources.join(' and ')} />
          {source === 'ai' && place.aiReason && <Field label="Why AI picked it" value={place.aiReason} />}
        </Section>
        <ClaimBusinessBanner place={{ name: d.name, phone: d.phone ?? d.mobile, website: d.website, address: d.address }} />
        <p className="text-xs text-muted">This business hasn't joined Calling Bell. Please confirm details with the business before you visit or pay.</p>
      </div>
    </div>
  );
}

/* ---------- Shared pieces ---------- */

/** The actions this result supports; each only when its data exists. */
function Actions({ name, phone, whatsApp, website, directions, shareUrl, extra }: {
  name: string; phone?: string | null; whatsApp?: string | null; website?: string | null; directions?: string | null; shareUrl?: string | null; extra?: ReactNode;
}) {
  const { enqueueSnackbar } = useSnackbar();
  const share = async () => {
    if (!shareUrl) return;
    try {
      if (navigator.share) { await navigator.share({ title: name, url: shareUrl }); return; }
      await navigator.clipboard.writeText(shareUrl);
      enqueueSnackbar('Link copied', { variant: 'success' });
    } catch (e) {
      if ((e as DOMException)?.name !== 'AbortError') enqueueSnackbar('Could not share this link', { variant: 'error' });
    }
  };
  return (
    <div className="flex flex-wrap items-center gap-2">
      {phone && <Button size="small" variant="contained" disableElevation startIcon={<CallRounded />} href={telHref(phone)}>Call</Button>}
      {whatsApp && (
        <Button size="small" variant="outlined" startIcon={<WhatsApp />} href={whatsApp} target="_blank" rel="noreferrer"
          sx={{ color: '#128C7E', borderColor: 'rgba(18,140,126,0.5)', '&:hover': { borderColor: '#128C7E', bgcolor: 'rgba(37,211,102,0.08)' } }}>WhatsApp</Button>
      )}
      {website && <Button size="small" variant="outlined" startIcon={<LanguageRounded />} href={website} target="_blank" rel="noreferrer nofollow">Website</Button>}
      {directions && <Button size="small" variant={phone ? 'outlined' : 'contained'} disableElevation startIcon={<DirectionsRounded />} href={directions} target="_blank" rel="noreferrer">Directions</Button>}
      {shareUrl && <Button size="small" startIcon={<ShareRounded />} onClick={() => void share()}>Share</Button>}
      {extra}
    </div>
  );
}

function DetailSkeleton() {
  return (
    <div aria-busy="true" aria-label="Loading details">
      <Skeleton variant="rectangular" sx={{ aspectRatio: '16/7', height: 'auto' }} />
      <div className="space-y-4 p-4">
        <Skeleton width="60%" height={32} /><Skeleton width="40%" />
        <div className="flex gap-2">{[0, 1, 2, 3].map((i) => <Skeleton key={i} variant="rounded" width={88} height={30} />)}</div>
        {[0, 1, 2].map((i) => <div key={i} className="space-y-1.5"><Skeleton width="30%" /><Skeleton /><Skeleton width="85%" /></div>)}
      </div>
    </div>
  );
}
