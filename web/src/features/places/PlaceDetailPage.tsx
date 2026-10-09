import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { useSnackbar } from 'notistack';
import { Button, IconButton, Skeleton, Tooltip } from '@mui/material';
import ArrowBackRounded from '@mui/icons-material/ArrowBackRounded';
import StarRounded from '@mui/icons-material/StarRounded';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import NearMeOutlined from '@mui/icons-material/NearMeOutlined';
import MapRounded from '@mui/icons-material/MapRounded';
import LanguageRounded from '@mui/icons-material/LanguageRounded';
import ShareRounded from '@mui/icons-material/ShareRounded';
import ContentCopyRounded from '@mui/icons-material/ContentCopyRounded';
import CallRounded from '@mui/icons-material/CallRounded';
import ScheduleRounded from '@mui/icons-material/ScheduleRounded';
import InfoOutlined from '@mui/icons-material/InfoOutlined';
import { api, ApiError, errorMessage } from '@/lib/api';
import { number } from '@/lib/format';
import { directionsHref, telHref } from '@/lib/contact';
import type { GooglePlace, PlaceDetails } from '@/lib/types';
import { useSeoPage } from '@/features/seo/seo';
import { EmptyState, ErrorState, Img } from '@/components/ui';
import { ClaimBusinessBanner } from '@/features/search/ExternalResults';
import { coordinates, ExternalLink, Field, PhotoCredit, PlaceActions, prettyUrl, Section, SocialLinks } from './PlaceParts';

/** What the Explore nearby list passes along: the result itself (shown while the details load) and the list's address. */
interface FromList { place?: GooglePlace; from?: string }

const today = new Date().toLocaleDateString('en-US', { weekday: 'long' });

/**
 * A Google Maps business from Explore nearby, on its own page (/nearby/place/{Google place id}): photos, rating, hours, contact, a map and
 * the same actions as the list. Details come from GET /api/places/details and are kept in memory only (Google's terms don't allow storing
 * them), under the same query as the search detail panel. The page works when opened directly, without the list.
 */
export default function PlaceDetailPage() {
  const { id = '' } = useParams();
  const { state } = useLocation() as { state: FromList | null };
  const navigate = useNavigate();
  const seed = state?.place;
  const backTo = state?.from ?? '/nearby';

  const { data: d, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['place-details', 'google', id],
    queryFn: () => api.get<PlaceDetails>('/api/places/details', { source: 'google', id }),
    staleTime: Infinity,
    gcTime: 30 * 60_000,
    retry: 1,
    enabled: !!id,
  });

  // The server names the page generically (it doesn't look places up); the business name replaces that once both are in.
  const seo = useSeoPage();
  const name = d?.name ?? seed?.name;
  useEffect(() => { if (name && seo.data) document.title = `${name} · Calling Bell`; }, [name, seo.data]);

  const back = () => (state?.from ? navigate(-1) : navigate('/nearby'));

  return (
    <div className="container-page py-6 md:py-8">
      <nav aria-label="Breadcrumb" className="mb-4 flex items-center gap-2 text-sm text-muted">
        <Tooltip title="Back to results">
          <IconButton size="small" onClick={back} aria-label="Back to results" sx={{ border: 1, borderColor: 'divider', borderRadius: '8px' }}>
            <ArrowBackRounded fontSize="small" />
          </IconButton>
        </Tooltip>
        <ol className="flex min-w-0 flex-wrap items-center gap-x-1.5">
          <li><Link to="/" className="hover:text-ink hover:underline">Home</Link></li>
          <li aria-hidden className="text-faint">/</li>
          <li><Link to={backTo} className="hover:text-ink hover:underline">Explore nearby</Link></li>
          {name && <><li aria-hidden className="text-faint">/</li><li aria-current="page" className="max-w-[16rem] truncate text-ink sm:max-w-md">{name}</li></>}
        </ol>
      </nav>

      {isLoading ? (
        <DetailSkeleton seed={seed} />
      ) : isError || !d ? (
        <div className="card">
          {error instanceof ApiError && (error.status === 400 || error.status === 404)
            ? <EmptyState icon={<MapRounded />} title="Business not found" message="This place may have closed or moved on Google Maps."
                action={<Button component={Link} to="/nearby" variant="contained">Explore nearby</Button>} />
            : <ErrorState message={errorMessage(error)} onRetry={() => void refetch()} />}
        </div>
      ) : (
        <Details d={d} seed={seed} />
      )}
    </div>
  );
}

function Details({ d, seed }: { d: PlaceDetails; seed?: GooglePlace }) {
  const { enqueueSnackbar } = useSnackbar();
  const directions = directionsHref(d.latitude, d.longitude, [d.name, d.address].filter(Boolean).join(', ')) ?? seed?.directionsUrl ?? '';
  const mapsUrl = d.mapsUrl ?? seed?.mapsUrl ?? directions;
  const phone = d.phone ?? d.mobile;
  const rated = d.rating != null && !!d.ratingCount;
  const distanceKm = seed?.distanceKm;
  const closedForGood = d.businessStatus && d.businessStatus !== 'Open for business';

  const share = async () => {
    const url = window.location.href;
    try {
      if (navigator.share) { await navigator.share({ title: d.name, url }); return; }
      await navigator.clipboard.writeText(url);
      enqueueSnackbar('Link copied', { variant: 'success' });
    } catch (e) {
      if ((e as DOMException)?.name !== 'AbortError') enqueueSnackbar('Could not share this link', { variant: 'error' });
    }
  };
  const copyAddress = async () => {
    if (!d.address) return;
    try { await navigator.clipboard.writeText(`${d.name}, ${d.address}`); enqueueSnackbar('Address copied', { variant: 'success' }); }
    catch { enqueueSnackbar('Couldn’t copy the address', { variant: 'warning' }); }
  };

  return (
    <article>
      <Gallery photos={d.photos} name={d.name} />

      <header className="mt-5 flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-bold tracking-tight md:text-[28px]">{d.name}</h1>
            <span className="inline-flex items-center gap-1 rounded-full border border-dashed border-line-strong px-2 py-0.5 text-[11px] font-medium text-muted">
              <MapRounded sx={{ fontSize: 12 }} className="text-info" />From Google Maps · not on Calling Bell
            </span>
          </div>
          {d.category && <p className="mt-1 text-sm text-muted">{d.category}</p>}
          <p className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-muted">
            {rated ? (
              <span className="inline-flex items-center gap-1" aria-label={`Rated ${d.rating!.toFixed(1)} out of 5 from ${number(d.ratingCount!)} reviews`}>
                <StarRounded sx={{ fontSize: 18, color: '#F4A62C' }} aria-hidden />
                <span className="font-semibold text-ink tabular">{d.rating!.toFixed(1)}</span>
                <span className="tabular">({number(d.ratingCount!)} {d.ratingCount === 1 ? 'review' : 'reviews'})</span>
              </span>
            ) : <span>No ratings yet</span>}
            {distanceKm != null && <span className="inline-flex items-center gap-1 tabular"><NearMeOutlined sx={{ fontSize: 15 }} aria-hidden />{distanceKm.toFixed(1)} km away</span>}
            {closedForGood ? <span className="font-semibold text-danger">{d.businessStatus}</span>
              : d.openNow != null && (
                <span className={`inline-flex items-center gap-1 font-semibold ${d.openNow ? 'text-success' : 'text-danger'}`}>
                  <span aria-hidden className={`h-1.5 w-1.5 rounded-full ${d.openNow ? 'bg-success' : 'bg-danger'}`} />{d.openNow ? 'Open now' : 'Closed now'}
                </span>
              )}
            {d.priceLevel && <span>{d.priceLevel}</span>}
          </p>
          {d.address && (
            <p className="mt-2 flex items-start gap-1 text-sm text-ink-2">
              <PlaceOutlined sx={{ fontSize: 18 }} className="shrink-0 text-muted" aria-hidden />
              <span>{d.address}</span>
              <Tooltip title="Copy name and address">
                <IconButton size="small" aria-label="Copy address" onClick={() => void copyAddress()} sx={{ p: '2px', color: 'text.secondary' }}>
                  <ContentCopyRounded sx={{ fontSize: 15 }} />
                </IconButton>
              </Tooltip>
            </p>
          )}
        </div>
        <div className="flex shrink-0 gap-2">
          {d.website && <Button variant="outlined" size="small" startIcon={<LanguageRounded />} href={d.website} target="_blank" rel="noreferrer nofollow">Website</Button>}
          <Button variant="outlined" size="small" startIcon={<ShareRounded />} onClick={() => void share()}>Share</Button>
        </div>
      </header>

      <PlaceActions className="mt-4 border-y border-line py-3 sm:border-0 sm:py-0"
        place={{ name: d.name, phone: d.phone ?? seed?.phone, internationalPhone: d.internationalPhone ?? seed?.internationalPhone, address: d.address, directionsUrl: directions, mapsUrl, sourceId: `${d.source}:${d.sourceId}` }} />

      <div className="mt-8 grid gap-8 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0 space-y-8">
          {(d.description || d.tags.length > 0) && (
            <Block title="About">
              {d.description && <p className="leading-7 text-ink-2">{d.description}</p>}
              {d.tags.length > 0 && (
                <div className="mt-3 flex flex-wrap gap-1.5">{d.tags.map((t) => <span key={t} className="rounded-full border border-line bg-surface px-2.5 py-0.5 text-xs text-ink-2">{t}</span>)}</div>
              )}
            </Block>
          )}

          {d.openingHours.length > 0 && (
            <Block title="Opening hours" icon={<ScheduleRounded sx={{ fontSize: 18 }} />}>
              <ul className="divide-y divide-line overflow-hidden rounded-xl border border-line bg-surface text-sm">
                {d.openingHours.map((h) => {
                  const at = h.indexOf(': ');
                  const day = at > 0 ? h.slice(0, at) : h, time = at > 0 ? h.slice(at + 2) : '';
                  const isToday = day === today;
                  return (
                    <li key={h} className={`flex justify-between gap-4 px-4 py-2.5 ${isToday ? 'bg-subtle font-semibold' : ''}`}>
                      <span>{day}{isToday && <span className="ml-1.5 text-xs font-normal text-muted">today</span>}</span>
                      <span className={`text-right ${/closed/i.test(time) ? 'text-muted' : ''}`}>{time}</span>
                    </li>
                  );
                })}
              </ul>
            </Block>
          )}

          {d.photos.length > 5 && (
            <Block title={`All photos (${number(d.photos.length)})`}>
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
                {d.photos.map((ph) => (
                  <figure key={ph.url}>
                    <Img src={ph.url} alt={`Photo of ${d.name}`} aspect="4/3" className="w-full" fallbackText={d.name} />
                    <PhotoCredit photo={ph} className="mt-0.5 text-faint" />
                  </figure>
                ))}
              </div>
            </Block>
          )}

          {d.otherFields.length > 0 && (
            <Block title="More details">
              <div className="space-y-2">{d.otherFields.map((f) => <Field key={f.label} label={f.label} value={f.value} />)}</div>
            </Block>
          )}

          <ClaimBusinessBanner place={{ name: d.name, phone: d.phone ?? d.mobile, website: d.website, address: d.address, sourceId: `${d.source}:${d.sourceId}` }} />
        </div>

        <aside className="space-y-4 lg:sticky lg:top-[88px] lg:self-start">
          <div className="card space-y-5 p-4">
            <Section title="Contact">
              <Field label="Phone" value={phone && <a href={telHref(phone)} className="inline-flex items-center gap-1 font-semibold hover:underline"><CallRounded sx={{ fontSize: 15 }} />{phone}</a>} />
              <Field label="International" value={d.internationalPhone !== phone ? d.internationalPhone : null} />
              <Field label="Email" value={d.email && <a href={`mailto:${d.email}`} className="hover:underline">{d.email}</a>} />
              <Field label="Website" value={d.website && <ExternalLink href={d.website}>{prettyUrl(d.website)}</ExternalLink>} />
              {d.socialLinks.length > 0 && <Field label="Social" value={<SocialLinks links={d.socialLinks} />} />}
            </Section>
            {!phone && !d.website && !d.email && <p className="text-sm text-muted">Google Maps has no contact details for this business.</p>}
          </div>

          <div className="card overflow-hidden">
            {d.latitude != null && d.longitude != null && (
              <iframe title={`Map of ${d.name}`} loading="lazy" referrerPolicy="no-referrer-when-downgrade"
                src={`https://maps.google.com/maps?q=${d.latitude},${d.longitude}&z=16&output=embed`}
                className="block aspect-[4/3] w-full border-0 bg-subtle" />
            )}
            <div className="space-y-2 p-4">
              <Section title="Location">
                <Field label="City" value={d.city} />
                <Field label="State" value={d.state} />
                <Field label="Country" value={d.country} />
                <Field label="Postal code" value={d.postalCode} />
                <Field label="Coordinates" value={coordinates(d.latitude, d.longitude)} />
              </Section>
              <div className="flex gap-2 pt-2">
                <Button size="small" variant="contained" disableElevation href={directions} target="_blank" rel="noreferrer" sx={{ flex: 1 }}>Directions</Button>
                <Button size="small" variant="outlined" href={mapsUrl} target="_blank" rel="noreferrer" sx={{ flex: 1 }}>Open in Maps</Button>
              </div>
            </div>
          </div>

          <p className="flex items-start gap-1.5 px-1 text-xs text-muted">
            <InfoOutlined sx={{ fontSize: 14, mt: '1px' }} />
            Details, ratings and photos from {d.dataSources.join(' and ')}. This business hasn’t joined Calling Bell, so please confirm details with it before you visit or pay.
          </p>
        </aside>
      </div>
    </article>
  );
}

function Block({ title, icon, children }: { title: string; icon?: React.ReactNode; children: React.ReactNode }) {
  return (
    <section>
      <h2 className="mb-3 flex items-center gap-1.5 text-lg font-semibold tracking-tight">{icon && <span className="flex text-muted">{icon}</span>}{title}</h2>
      {children}
    </section>
  );
}

/**
 * Up to five photos: one large and four small from 768px (two small when there are fewer), a swipeable strip on phones. Each photo carries
 * the credit Google requires.
 */
function Gallery({ photos, name }: { photos: PlaceDetails['photos']; name: string }) {
  const [failed, setFailed] = useState(false);
  if (photos.length === 0 || failed) {
    return <Img src={null} alt={name} fallbackText={name} aspect="21/9" className="w-full" rounded="rounded-xl" />;
  }
  const shown = photos.slice(0, photos.length >= 5 ? 5 : Math.min(photos.length, 3));
  const [hero, ...rest] = shown;
  return (
    <>
      <div className="-mx-4 flex snap-x snap-mandatory gap-2 overflow-x-auto px-4 pb-1 md:hidden" aria-label="Photos">
        {photos.slice(0, 8).map((ph, i) => (
          <figure key={ph.url} className={`relative shrink-0 snap-center ${photos.length > 1 ? 'w-[85%]' : 'w-full'}`}>
            <Img src={ph.url} alt={`Photo ${i + 1} of ${name}`} aspect="4/3" className="w-full" rounded="rounded-xl" fallbackText={name} eager={i === 0} />
            <PhotoCredit photo={ph} className="absolute bottom-1.5 right-2 max-w-[80%] rounded bg-black/55 px-1.5 text-white/90" />
          </figure>
        ))}
      </div>
      <div className={`hidden gap-2 overflow-hidden rounded-xl md:grid ${rest.length > 0 ? 'md:h-[380px] lg:h-[420px]' : 'md:h-[320px]'} ${rest.length >= 4 ? 'grid-cols-4 grid-rows-2' : rest.length > 1 ? 'grid-cols-3 grid-rows-2' : rest.length === 1 ? 'grid-cols-3 grid-rows-1' : 'grid-cols-1'}`}>
        <figure className={`relative ${rest.length > 0 ? 'col-span-2 row-span-2' : ''}`}>
          <img src={hero.url} alt={`Photo of ${name}`} className="h-full w-full bg-subtle object-cover" fetchPriority="high" onError={() => setFailed(true)} />
          <PhotoCredit photo={hero} className="absolute bottom-1.5 right-2 max-w-[80%] rounded bg-black/55 px-1.5 text-white/90" />
        </figure>
        {rest.map((ph, i) => (
          <figure key={ph.url} className={`relative ${rest.length < 4 ? 'col-span-1' : ''}`}>
            <Img src={ph.url} alt={`Photo ${i + 2} of ${name}`} className="h-full w-full" rounded="rounded-none" fallbackText={name} />
            <PhotoCredit photo={ph} className="absolute bottom-1 right-1.5 max-w-[90%] rounded bg-black/55 px-1 text-white/90" />
          </figure>
        ))}
      </div>
    </>
  );
}

function DetailSkeleton({ seed }: { seed?: GooglePlace }) {
  return (
    <div aria-busy="true" aria-label="Loading details">
      {seed?.photos[0]
        ? <Img src={seed.photos[0].url} alt={`Photo of ${seed.name}`} className="h-[240px] w-full md:h-[380px] lg:h-[420px]" rounded="rounded-xl" eager />
        : <Skeleton variant="rounded" sx={{ height: { xs: 240, md: 380, lg: 420 }, borderRadius: '12px' }} />}
      <div className="mt-5">
        {seed ? <h1 className="text-2xl font-bold tracking-tight md:text-[28px]">{seed.name}</h1> : <Skeleton width="50%" height={40} />}
        <Skeleton width="30%" /><Skeleton width="60%" />
      </div>
      <div className="mt-4 grid grid-cols-5 gap-1 sm:flex sm:gap-2">
        {Array.from({ length: 5 }, (_, j) => <Skeleton key={j} variant="rounded" sx={{ height: { xs: 50, sm: 36 }, width: { sm: 110 }, borderRadius: '8px' }} />)}
      </div>
      <div className="mt-8 grid gap-8 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="space-y-3"><Skeleton width="25%" height={28} /><Skeleton variant="rounded" height={260} sx={{ borderRadius: '12px' }} /></div>
        <Skeleton variant="rounded" height={320} sx={{ borderRadius: '12px' }} />
      </div>
    </div>
  );
}
