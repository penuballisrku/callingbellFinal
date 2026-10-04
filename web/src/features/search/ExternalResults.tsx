import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button, CircularProgress, Skeleton, Tab, Tabs, Tooltip, useMediaQuery, type Theme } from '@mui/material';
import AutoAwesomeRounded from '@mui/icons-material/AutoAwesomeRounded';
import MapRounded from '@mui/icons-material/MapRounded';
import VerifiedRounded from '@mui/icons-material/VerifiedRounded';
import DirectionsRounded from '@mui/icons-material/DirectionsRounded';
import CallRounded from '@mui/icons-material/CallRounded';
import LanguageRounded from '@mui/icons-material/LanguageRounded';
import ScheduleRounded from '@mui/icons-material/ScheduleRounded';
import PlaceRounded from '@mui/icons-material/PlaceRounded';
import InfoOutlined from '@mui/icons-material/InfoOutlined';
import WhatsApp from '@mui/icons-material/WhatsApp';
import { LogoMark } from '@/components/Logo';
import { Link } from 'react-router';
import { api } from '@/lib/api';
import { initials, number } from '@/lib/format';
import type { ExternalPlace, ExternalSearch, ExternalTier } from '@/lib/types';
import { Img, Rating } from '@/components/ui';

const PAGE = 8;

/** `place`: a place typed in the search that isn't a listed city or area ("lawyers in Nellore"); results are near it instead. */
export interface ExternalSearchParams { q?: string | null; category?: string | null; sub?: string | null; city?: string | null; areaId?: string | null; place?: string | null }

/**
 * Results beyond the platform for the current search, after the registered businesses: Google Maps businesses, then AI-recommended real
 * places from OpenStreetMap. Polls while the local AI is still picking places or OpenStreetMap is still being searched.
 */
export function useExternalSearch(p: ExternalSearchParams) {
  const enabled = !!(p.q?.trim() || p.category || p.sub);
  return useQuery({
    queryKey: ['external-search', p.q ?? '', p.category ?? '', p.sub ?? '', p.city ?? '', p.areaId ?? '', p.place ?? ''],
    queryFn: () => api.get<ExternalSearch>('/api/geo/external-search', { q: p.q, category: p.category, sub: p.sub, city: p.city, area: p.areaId, place: p.place }),
    enabled,
    staleTime: 300_000,
    retry: 1,
    // Poll while the AI is ranking or the full OpenStreetMap search is still finishing (about five minutes at most).
    refetchInterval: (q) => ((q.state.data?.ai.aiStatus === 'pending' || q.state.data?.ai.searching) && q.state.dataUpdateCount < 40 ? 8_000 : false),
  });
}

/** Search priority: registered businesses from our database, then Google Maps, then places picked by the free AI agents. */
export const SOURCE_PRIORITY = { db: 1, google: 2, ai: 3 } as const;

export type ResultSource = keyof typeof SOURCE_PRIORITY;

/** Tab id/panel id pairs, so each tab names the panel it controls. */
export const sourceTabId = (s: ResultSource) => `results-tab-${s}`;
export const sourcePanelId = (s: ResultSource) => `results-panel-${s}`;

/** One tab per source, in priority order, each with its result count. */
export function SourceTabs({ value, onChange, dbCount, dbLoading, ext, loading }: {
  value: ResultSource; onChange: (s: ResultSource) => void; dbCount?: number; dbLoading: boolean; ext?: ExternalSearch; loading: boolean;
}) {
  // Phones: three equal tabs with short labels and no icons, so all fit without scrolling.
  const phone = useMediaQuery((theme: Theme) => theme.breakpoints.down('sm'));
  const tierCount = (t?: ExternalTier) =>
    !t ? (loading ? <CircularProgress size={12} aria-label="Searching" /> : '–')
      : t.status === 'off' ? 'off'
        : t.status === 'unavailable' && !t.searching ? '!'
          : t.status === 'skipped' ? '–'
            : <>{number(t.total)}{(t.searching || t.aiStatus === 'pending') && <CircularProgress size={10} sx={{ ml: 0.75 }} aria-label="Still searching" />}</>;

  const tabs: { key: ResultSource; icon: React.ReactNode; label: string; short: string; count: React.ReactNode }[] = [
    { key: 'db', icon: <VerifiedRounded sx={{ fontSize: 18 }} className="text-success" />, label: 'Calling Bell', short: 'Calling Bell',
      count: dbLoading || dbCount == null ? <CircularProgress size={12} aria-label="Loading" /> : number(dbCount) },
    { key: 'google', icon: <MapRounded sx={{ fontSize: 18 }} className="text-info" />, label: 'Google Maps', short: 'Google', count: tierCount(ext?.google) },
    { key: 'ai', icon: <AutoAwesomeRounded sx={{ fontSize: 18 }} className="text-accent-ink" />, label: 'AI recommended', short: 'AI', count: tierCount(ext?.ai) },
  ];
  return (
    <Tabs value={value} onChange={(_, v: ResultSource) => onChange(v)} variant={phone ? 'fullWidth' : 'scrollable'} scrollButtons="auto"
      aria-label="Results by source, in priority order" sx={{ mb: 3, borderBottom: 1, borderColor: 'divider', minHeight: 48 }}>
      {tabs.map((t) => (
        <Tab key={t.key} value={t.key} id={sourceTabId(t.key)} aria-controls={sourcePanelId(t.key)} disableRipple
          sx={{ textTransform: 'none', minHeight: 48, minWidth: 0, px: { xs: 0.5, sm: 1.5 }, mr: { xs: 0, sm: 1 }, fontWeight: 600, fontSize: { xs: 13, sm: 14 } }}
          label={
            <span className="inline-flex items-center gap-1.5 whitespace-nowrap sm:gap-2">
              <span className="hidden sm:contents"><PriorityBadge n={SOURCE_PRIORITY[t.key]} /></span><span className="hidden sm:contents">{t.icon}</span>
              <span className="hidden sm:inline">{t.label}</span><span className="sm:hidden">{t.short}</span>
              <span className="inline-flex min-w-6 items-center justify-center rounded-full bg-subtle px-2 py-0.5 text-xs font-semibold text-ink-2 tabular">{t.count}</span>
            </span>
          } />
      ))}
    </Tabs>
  );
}

export function PriorityBadge({ n }: { n: number }) {
  return (
    <span className="grid h-5 w-5 shrink-0 place-items-center rounded-full bg-subtle text-[11px] font-bold text-ink-2 tabular" aria-label={`Priority ${n}`}>{n}</span>
  );
}

/** The "Google Maps Businesses" or "AI Recommended Businesses" tab's content. */
export function ExternalResults({ ext, loading, isError, only }: { ext?: ExternalSearch; loading: boolean; isError: boolean; only: 'google' | 'ai' }) {
  const sourceName = only === 'google' ? 'Google Maps' : 'AI recommendations';
  if (loading) {
    return (
      <section aria-busy="true" aria-label={`Searching ${sourceName}`}>
        <Skeleton width={260} height={28} />
        <div className="mt-3 space-y-3">{Array.from({ length: 4 }, (_, i) => <Skeleton key={i} variant="rounded" height={130} />)}</div>
      </section>
    );
  }
  if (isError) {
    return (
      <div className="card flex items-center gap-2 p-4 text-sm text-muted">
        <InfoOutlined fontSize="small" />{sourceName} couldn’t be loaded right now. Please try again in a few minutes.
      </div>
    );
  }
  if (!ext) return null;
  const where = searchLocation(ext);
  const what = (ext.query ?? 'businesses').toLowerCase();
  const tier = only === 'google' ? ext.google : ext.ai;
  if (tier.status === 'off' || tier.status === 'skipped') {
    return (
      <div className="card flex items-center gap-2 p-4 text-sm text-muted">
        <InfoOutlined fontSize="small" />
        {tier.status === 'off' ? `${sourceName} results aren’t enabled.`
          : ext.origin === 'place-not-found' ? 'We couldn’t find that place, so there is nothing to search near.'
            : 'Search for a service or category to see results here.'}
      </div>
    );
  }
  return (
    <>
      {only === 'google' && (
        <TierSection tier={ext.google} source="google" title="Google Maps Businesses" where={where}
          subtitle="Listed on Google Maps, nearest first. Not registered on Calling Bell."
          empty={`Google Maps has no ${what} near ${where?.label ?? 'this location'}.`}
          attribution={<>Results from Google Maps</>} />
      )}
      {only === 'ai' && (
        <TierSection tier={ext.ai} source="ai" title="AI Recommended Businesses" where={where}
          subtitle={ext.ai.aiStatus === 'ranked'
            ? `More real places from OpenStreetMap, picked for “${ext.query}” by our free AI assistant. Not registered on Calling Bell.`
            : ext.ai.aiStatus === 'pending'
              ? 'More real places from OpenStreetMap, nearest first. Our free AI assistant is picking the best matches…'
              : 'More real places from OpenStreetMap, nearest first. Not registered on Calling Bell.'}
          empty={`OpenStreetMap has no ${what} mapped near ${where?.label ?? 'this location'} yet.`}
          attribution={<>Map data © <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer" className="underline">OpenStreetMap contributors</a></>} />
      )}
    </>
  );
}

interface SearchLocation { label: string; source: string }

/** Where the Google Maps and AI results are: the area/city selected in the location menu, a place typed in the search, or the visitor's area. */
function searchLocation(ext: ExternalSearch): SearchLocation | null {
  if (!ext.placeName) return null;
  const label = !ext.cityName || ext.cityName === ext.placeName ? ext.placeName : `${ext.placeName}, ${ext.cityName}`;
  const source = ext.origin === 'area' ? 'Selected area'
    : ext.origin === 'city' ? 'Selected city (city centre)'
      : ext.origin === 'place' ? 'From your search'
        : 'Your approximate location';
  return { label, source };
}

function TierSection({ tier, source, title, subtitle, where, empty, attribution }: {
  tier: ExternalTier; source: 'ai' | 'google'; title: string; subtitle: string; where: SearchLocation | null; empty: string; attribution: React.ReactNode;
}) {
  const [shown, setShown] = useState(PAGE);
  const Icon = source === 'ai' ? AutoAwesomeRounded : MapRounded;
  return (
    <section aria-labelledby={`ext-${source}`}>
      <div className="mb-3 flex items-start gap-3">
        <span aria-hidden className={`grid h-9 w-9 shrink-0 place-items-center rounded-xl ${source === 'ai' ? 'bg-accent-soft text-accent-ink' : 'bg-subtle text-info'}`}>
          <Icon sx={{ fontSize: 20 }} />
        </span>
        <div className="min-w-0">
          <h2 id={`ext-${source}`} className="flex flex-wrap items-center gap-x-2 text-lg font-semibold">
            <PriorityBadge n={SOURCE_PRIORITY[source]} />
            {title} <span className="text-sm font-medium text-muted tabular">{tier.status === 'ready' ? number(tier.total) : ''}</span>
          </h2>
          {where && (
            <p className="mt-0.5 inline-flex max-w-full items-center gap-1 text-sm">
              <PlaceRounded sx={{ fontSize: 16 }} className="shrink-0 text-muted" />
              <span className="truncate font-medium text-ink">{where.label}</span>
              <span className="shrink-0 text-xs text-muted">· {where.source}</span>
            </p>
          )}
          <p className="text-sm text-muted">{subtitle}</p>
        </div>
      </div>

      {tier.searching && tier.items.length === 0 ? (
        <div className="card flex items-center gap-2 p-4 text-sm text-muted" role="status">
          <CircularProgress size={16} />Searching OpenStreetMap{where ? ` around ${where.label}` : ''}… results will appear here shortly.
        </div>
      ) : tier.status === 'unavailable' ? (
        <div className="card flex items-center gap-2 p-4 text-sm text-muted"><InfoOutlined fontSize="small" />This source isn’t responding right now. Please try again in a few minutes.</div>
      ) : tier.items.length === 0 ? (
        <div className="card p-4 text-sm text-muted">{empty}</div>
      ) : (
        <>
          {/* One business per row. */}
          <ul className="space-y-3">
            {tier.items.slice(0, shown).map((p) => <li key={p.id} className="min-w-0"><PlaceCard p={p} source={source} /></li>)}
          </ul>
          {tier.items.length > shown && (
            <div className="mt-4 flex justify-center">
              <Button variant="outlined" onClick={() => setShown((n) => n + PAGE)}>Show more ({number(tier.items.length - shown)})</Button>
            </div>
          )}
          {tier.searching && (
            <p className="mt-3 flex items-center gap-2 text-xs text-muted" role="status"><CircularProgress size={12} />Still searching OpenStreetMap for more places…</p>
          )}
        </>
      )}
      <p className="mt-3 text-xs text-faint">
        {attribution}{tier.duplicates > 0 && ` · ${number(tier.duplicates)} already listed above ${tier.duplicates === 1 ? 'was' : 'were'} left out`}
      </p>
    </section>
  );
}

/**
 * WhatsApp chat link for a place's phone, or null when it isn't a mobile number (WhatsApp needs one). Indian numbers are written many
 * ways ("098480 12345", "+91 98480 12345", "9848012345"); they are reduced to the 10-digit mobile number with the 91 country code.
 */
function whatsAppHref(p: ExternalPlace): string | null {
  if (!p.phone) return null;
  let digits = p.phone.replace(/\D/g, '');
  if (digits.length === 12 && digits.startsWith('91')) digits = digits.slice(2);
  else if (digits.length === 11 && digits.startsWith('0')) digits = digits.slice(1);
  if (!/^[6-9]\d{9}$/.test(digits)) return null;
  const text = `Hi ${p.name}, I found your business on Calling Bell and would like to know more about your services.`;
  return `https://wa.me/91${digits}?text=${encodeURIComponent(text)}`;
}

/** Business sign-up, pre-filled with what is known about the place (the owner can change everything). */
function joinHref(p: ExternalPlace): string {
  const params = new URLSearchParams({ type: 'business', name: p.name });
  if (p.phone) params.set('phone', p.phone);
  if (p.website) params.set('website', p.website);
  if (p.address) params.set('address', p.address);
  return `/register?${params}`;
}

function PlaceCard({ p, source }: { p: ExternalPlace; source: 'ai' | 'google' }) {
  const hours = p.openingHours?.replace(/;\s*/g, ' · ');
  const whatsApp = whatsAppHref(p);
  return (
    <article className="card flex h-full gap-3 p-4">
      {p.photoUrl ? (
        <div className="w-20 shrink-0 sm:w-24">
          <Img src={p.photoUrl} alt={`Photo of ${p.name}`} fallbackText={p.name} rounded="rounded-xl" aspect="1/1" className="w-full" />
          {p.photoCredit && (
            <p className="mt-1 truncate text-[10px] leading-tight text-faint" title={`Photo: ${p.photoCredit}`}>
              {p.photoCreditUrl
                ? <a href={p.photoCreditUrl} target="_blank" rel="noreferrer nofollow" className="hover:underline">{p.photoCredit}</a>
                : p.photoCredit}
            </p>
          )}
        </div>
      ) : (
        <span aria-hidden className="grid h-12 w-12 shrink-0 place-items-center rounded-xl bg-subtle text-sm font-bold text-muted">{initials(p.name)}</span>
      )}
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <h3 className="min-w-0 truncate text-[15px] font-semibold">{p.name}</h3>
          <span className={`inline-flex shrink-0 items-center gap-0.5 rounded px-1.5 py-0.5 text-[11px] font-semibold ${source === 'ai' ? 'bg-accent-soft text-accent-ink' : 'bg-subtle text-info'}`}>
            {source === 'ai' ? <><AutoAwesomeRounded sx={{ fontSize: 12 }} />AI Recommended</> : <><MapRounded sx={{ fontSize: 12 }} />Google Maps</>}
          </span>
        </div>
        <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-[13px] text-muted">
          {p.kind && <span>{p.kind}</span>}
          <span className="inline-flex items-center gap-0.5"><PlaceRounded sx={{ fontSize: 15 }} />{p.distanceKm.toFixed(1)} km away</span>
          {p.rating != null && <Rating value={p.rating} count={p.ratingCount ?? undefined} />}
        </div>
        {p.address && <p className="mt-1 line-clamp-2 text-[13px] text-ink-2">{p.address}</p>}
        {hours && (
          <Tooltip title={hours}>
            <p className="mt-1 inline-flex max-w-full items-center gap-1 text-xs text-muted"><ScheduleRounded sx={{ fontSize: 14 }} /><span className="truncate">{hours}</span></p>
          </Tooltip>
        )}
        <div className="mt-3 flex flex-wrap gap-2">
          <Button size="small" variant="contained" startIcon={<DirectionsRounded />} href={p.directionsUrl} target="_blank" rel="noreferrer">Directions</Button>
          {p.phone && <Button size="small" variant="outlined" startIcon={<CallRounded />} href={`tel:${p.phone.replace(/[^\d+]/g, '')}`}>Call</Button>}
          {whatsApp && (
            <Button size="small" variant="outlined" startIcon={<WhatsApp />} href={whatsApp} target="_blank" rel="noreferrer"
              sx={{ color: '#128C7E', borderColor: 'rgba(18,140,126,0.5)', '&:hover': { borderColor: '#128C7E', bgcolor: 'rgba(37,211,102,0.08)' } }}>
              WhatsApp
            </Button>
          )}
          {p.website && <Button size="small" variant="outlined" startIcon={<LanguageRounded />} href={p.website} target="_blank" rel="noreferrer nofollow">Website</Button>}
          {p.sourceUrl && (
            <Button size="small" href={p.sourceUrl} target="_blank" rel="noreferrer">{source === 'ai' ? 'View on map' : 'Open in Google Maps'}</Button>
          )}
          <Tooltip describeChild title="Own this business? List it on Calling Bell for free to get enquiries and bookings">
            {/* Highlighted brand call to action: amber with the Calling Bell mark. */}
            <Button size="small" variant="contained" disableElevation component={Link} to={joinHref(p)}
              startIcon={<LogoMark size={18} />}
              sx={{
                ml: { sm: 'auto' }, bgcolor: 'var(--cb-accent)', color: 'var(--cb-on-accent)', fontWeight: 700, px: 1.5,
                boxShadow: '0 0 0 3px var(--cb-ring)',
                '&:hover': { bgcolor: 'color-mix(in srgb, var(--cb-accent) 88%, #000)', boxShadow: '0 0 0 4px var(--cb-ring)' },
                '& .MuiButton-startIcon': { mr: 0.75 },
              }}>
              Join Calling Bell
            </Button>
          </Tooltip>
        </div>
      </div>
    </article>
  );
}
