import { useState } from 'react';
import { useInfiniteQuery, useQuery, useQueryClient, type InfiniteData } from '@tanstack/react-query';
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
import StarRounded from '@mui/icons-material/StarRounded';
import { LogoMark } from '@/components/Logo';
import { Link, useSearchParams } from 'react-router';
import { api } from '@/lib/api';
import { number } from '@/lib/format';
import type { ExternalPlace, ExternalSearch, ExternalTier, PlaceContact } from '@/lib/types';
import { Img } from '@/components/ui';
import { joinHint, joinHref, telHref, whatsAppHref } from '@/lib/contact';

const PAGE = 8;
/** Polls of the external search while the AI works (every 8 s); after that the visitor can check again by hand. */
const MAX_POLLS = 75;
const FAST_POLLS = 20;

/** `place`: a place typed in the search that isn't a listed city or area ("lawyers in Nellore"); results are near it instead. */
export interface ExternalSearchParams { q?: string | null; category?: string | null; sub?: string | null; city?: string | null; areaId?: string | null; place?: string | null }

/** `aiWhenGoogleBelow`: search OpenStreetMap only when Google Maps returns fewer places than this (unset: always, as on the search page). */
export interface ExternalSearchOptions { aiWhenGoogleBelow?: number }

/**
 * The Google results the page shows, sent back with each AI tier request (only what the server needs: duplicates and the overview).
 * At most 60, Google's limit per search and the server's.
 */
const shownGoogle = (items: ExternalPlace[]) => items.slice(0, 60).map((g) => ({
  name: g.name, kind: g.kind, latitude: g.latitude, longitude: g.longitude, phone: g.phone, distanceKm: g.distanceKm,
  rating: g.rating, ratingCount: g.ratingCount, openingHours: g.openingHours,
}));

/** While the AI tier's first answer is on its way, so the Google tab can already show its results. */
const AI_LOADING: ExternalTier = { status: 'ready', aiStatus: 'pending', total: 0, duplicates: 0, items: [], searching: true };

/** Google's pages of one search as a single tier: every page's places in order, and the token for the next page (if any). */
function mergeGooglePages(pages: ExternalSearch[]): ExternalTier {
  const first = pages[0].google;
  if (first.status !== 'ready') return first;
  const seen = new Set<string>();
  const items = pages.flatMap((p) => p.google.items).filter((p) => !seen.has(p.id) && !!seen.add(p.id));
  return { ...first, items, total: items.length, duplicates: pages.reduce((n, p) => n + p.google.duplicates, 0),
    nextPageToken: pages[pages.length - 1].google.nextPageToken ?? null };
}

/**
 * Results beyond the platform for the current search, after the registered businesses: Google Maps businesses, then AI-recommended real
 * places from OpenStreetMap.
 *
 * Google is asked once per search (it is a paid service and its results don't change while the visitor reads them), 20 at a time: the
 * first page shows at once and the next is loaded only when the visitor asks for more. Only the AI tier is polled while the local AI picks
 * places, writes its overview or OpenStreetMap is still being searched; each poll sends back the Google results shown, so the server can
 * leave out duplicates without calling Google again (nothing from Google is stored on the server).
 */
export function useExternalSearch(p: ExternalSearchParams, options: ExternalSearchOptions = {}) {
  const enabled = !!(p.q?.trim() || p.category || p.sub);
  const search = { q: p.q, category: p.category, sub: p.sub, city: p.city, area: p.areaId, place: p.place };
  const baseKey = ['external-search', p.q ?? '', p.category ?? '', p.sub ?? '', p.city ?? '', p.areaId ?? '', p.place ?? ''];
  const queryClient = useQueryClient();

  const googleKey = [...baseKey, 'google'];
  const googleQuery = useInfiniteQuery({
    queryKey: googleKey,
    queryFn: ({ pageParam }) => api.get<ExternalSearch>('/api/geo/external-search', { ...search, tiers: 'google', googlePage: pageParam }),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.google.nextPageToken ?? undefined,
    enabled,
    staleTime: 300_000,
    retry: 1,
  });
  const googleTier = googleQuery.data ? mergeGooglePages(googleQuery.data.pages) : undefined;
  // OpenStreetMap is the last resort when only a few places are wanted: asked only when Google is off, failed or found too few.
  const aiNeeded = options.aiWhenGoogleBelow === undefined
    || (googleTier?.status === 'ready' ? googleTier.items.length : 0) < options.aiWhenGoogleBelow;

  const aiKey = [...baseKey, 'ai'];
  const aiQuery = useQuery({
    queryKey: aiKey,
    // Read at request time (not part of the key), so a poll always sends the Google results currently shown.
    queryFn: async () => {
      const pages = queryClient.getQueryData<InfiniteData<ExternalSearch>>(googleKey)?.pages;
      const google = pages?.length ? mergeGooglePages(pages) : undefined;
      const res = await api.post<ExternalSearch>('/api/geo/external-search/ai',
        { ...search, googlePlaces: google?.status === 'ready' ? shownGoogle(google.items) : [] });
      return res.data;
    },
    // After Google has answered (or failed), so the first AI request already leaves out places Google shows.
    enabled: enabled && !googleQuery.isPending && aiNeeded,
    staleTime: 300_000,
    retry: 1,
    // Poll while the AI is ranking or writing its overview, or the full OpenStreetMap search is still finishing (ten minutes at most;
    // the server drops AI work nobody polls for, so polling is what keeps it queued).
    // Every 3 s for the first minute (map results and quick AI answers show as soon as they are ready), then every 8 s.
    refetchInterval: (q) => {
      const d = q.state.data;
      const waiting = (d?.ai.aiStatus === 'pending' || d?.ai.searching || d?.insight?.status === 'pending') && q.state.dataUpdateCount < MAX_POLLS;
      return waiting ? (q.state.dataUpdateCount < FAST_POLLS ? 3_000 : 8_000) : false;
    },
  });

  // One result in the usual shape: Google's tier from its pages, everything else from the latest AI poll.
  const g = googleQuery.data?.pages[0];
  const a = aiQuery.data;
  const data: ExternalSearch | undefined = g || a
    ? { ...(a ?? g!), ai: a?.ai ?? (g?.ai.status === 'skipped' && aiQuery.isPending && aiNeeded ? AI_LOADING : (g ?? a)!.ai), google: googleTier ?? a!.google }
    : undefined;
  /** False once automatic refreshing has given up; the page then offers to check again by hand. */
  const polling = (queryClient.getQueryState(aiKey)?.dataUpdateCount ?? 0) < MAX_POLLS;
  return {
    data,
    // The page shows results as soon as either part has arrived.
    isLoading: enabled && !data && (googleQuery.isLoading || aiQuery.isLoading || (googleQuery.isPending && !googleQuery.isError)),
    isError: googleQuery.isError && aiQuery.isError,
    polling,
    refetch: () => aiQuery.refetch(),
    /** Loads Google's next 20 results for the same search (shown below the ones already listed). */
    moreGoogle: googleQuery.hasNextPage ? () => { if (!googleQuery.isFetchingNextPage) void googleQuery.fetchNextPage(); } : undefined,
    loadingMoreGoogle: googleQuery.isFetchingNextPage,
  };
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
            : <>{number(t.total)}{t.nextPageToken ? '+' : ''}{(t.searching || t.aiStatus === 'pending') && <CircularProgress size={10} sx={{ ml: 0.75 }} aria-label="Still searching" />}</>;

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
export function ExternalResults({ ext, loading, isError, only, polling, onRefresh, onSelect, moreGoogle, loadingMoreGoogle }: {
  ext?: ExternalSearch; loading: boolean; isError: boolean; only: 'google' | 'ai';
  /** Loads Google's next page; undefined when Google has no more. */
  moreGoogle?: () => void; loadingMoreGoogle?: boolean;
  /** Opens a result's full details (the search page's detail panel). */
  onSelect?: (place: ExternalPlace, source: 'google' | 'ai') => void;
  /** Whether results are still refreshed automatically; when not, a pending AI overview offers to check again. */
  polling?: boolean; onRefresh?: () => void;
}) {
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
        <TierSection tier={ext.google} source="google" title="Google Maps Businesses" where={where} onSelect={onSelect}
          onLoadMore={moreGoogle} loadingMore={loadingMoreGoogle}
          subtitle="Nearest first · not on Calling Bell"
          empty={`Google Maps has no ${what} near ${where?.label ?? 'this location'}.`}
          attribution={<>Results from Google Maps</>} />
      )}
      {only === 'ai' && <AiInsight insight={ext.insight} stalled={polling === false} onRefresh={onRefresh} />}
      {only === 'ai' && (
        <TierSection tier={ext.ai} source="ai" title="AI Recommended Businesses" where={where} onSelect={onSelect}
          subtitle={ext.ai.aiStatus === 'ranked'
            ? 'Best matches picked by AI · not on Calling Bell'
            : ext.ai.aiStatus === 'pending'
              ? 'Nearest first · AI is picking the best matches…'
              : 'Nearest first · not on Calling Bell'}
          empty={`OpenStreetMap has no ${what} mapped near ${where?.label ?? 'this location'} yet.`}
          attribution={<>Map data © <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer" className="underline">OpenStreetMap contributors</a></>} />
      )}
    </>
  );
}

/** The AI's overview of these results, written by the local AI from the places found and Calling Bell's own prices and bookings. */
function AiInsight({ insight, stalled, onRefresh }: { insight?: ExternalSearch['insight']; stalled: boolean; onRefresh?: () => void }) {
  if (!insight || insight.status === 'off') return null;
  if (insight.status === 'pending' && stalled) {
    return (
      <div className="card mb-6 flex flex-wrap items-center gap-3 p-4 text-sm text-muted" role="status">
        <AutoAwesomeRounded sx={{ fontSize: 18 }} className="text-accent-ink" />
        <span className="min-w-0 flex-1">The AI overview is taking longer than usual. The places below are ready to use.</span>
        {onRefresh && <Button size="small" variant="outlined" onClick={onRefresh}>Check again</Button>}
      </div>
    );
  }
  return (
    <section aria-labelledby="ai-insight" className="card mb-6 border-[color-mix(in_srgb,var(--cb-accent)_35%,transparent)] p-4" aria-busy={insight.status === 'pending'}>
      <h2 id="ai-insight" className="flex items-center gap-2 text-sm font-semibold">
        <span aria-hidden className="grid h-7 w-7 place-items-center rounded-lg bg-accent-soft text-accent-ink"><AutoAwesomeRounded sx={{ fontSize: 16 }} /></span>
        AI overview
      </h2>
      {insight.status === 'pending' || !insight.text ? (
        <div className="mt-3 space-y-1.5" role="status">
          <Skeleton width="92%" /><Skeleton width="85%" /><Skeleton width="60%" />
          <p className="pt-1 text-xs text-muted">Our AI assistant is reviewing these places and Calling Bell’s prices and bookings…</p>
        </div>
      ) : (
        <InsightText text={insight.text} />
      )}
    </section>
  );
}

/** The overview kept short: three lines, the rest on request. */
function InsightText({ text }: { text: string }) {
  const [open, setOpen] = useState(false);
  const long = text.length > 220;
  return (
    <>
      <p id="ai-insight-text" className={`mt-2 whitespace-pre-line text-sm leading-relaxed text-ink ${open || !long ? '' : 'line-clamp-3'}`}>{text}</p>
      <div className="mt-1.5 flex flex-wrap items-center justify-between gap-2">
        <span className="text-[11px] text-faint">Written by AI · check details with the business</span>
        {long && (
          <button type="button" onClick={() => setOpen((v) => !v)} aria-expanded={open} aria-controls="ai-insight-text"
            className="text-xs font-semibold text-ink-2 hover:text-accent-ink hover:underline">{open ? 'Show less' : 'Read more'}</button>
        )}
      </div>
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

function TierSection({ tier, source, title, subtitle, where, empty, attribution, onSelect, onLoadMore, loadingMore }: {
  tier: ExternalTier; source: 'ai' | 'google'; title: string; subtitle: string; where: SearchLocation | null; empty: string; attribution: React.ReactNode;
  onSelect?: (place: ExternalPlace, source: 'google' | 'ai') => void;
  /** Loads the source's next page (Google: 20 more); undefined when there is no more. */
  onLoadMore?: () => void; loadingMore?: boolean;
}) {
  const [shown, setShown] = useState(PAGE);
  const showMore = () => {
    const next = shown + PAGE;
    setShown(next);
    // Ask for the next page while the visitor reads these, so the following "Show more" is instant.
    if (onLoadMore && next + PAGE > tier.items.length) onLoadMore();
  };
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
            {title} <span className="text-sm font-medium text-muted tabular">{tier.status === 'ready' ? `${number(tier.total)}${tier.nextPageToken ? '+' : ''}` : ''}</span>
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
            {tier.items.slice(0, shown).map((p) => <li key={p.id} className="min-w-0"><PlaceCard p={p} source={source} onSelect={onSelect} /></li>)}
          </ul>
          {(tier.items.length > shown || onLoadMore) && (
            <div className="mt-4 flex justify-center">
              <Button variant="outlined" onClick={showMore} disabled={loadingMore && tier.items.length <= shown}
                startIcon={loadingMore && tier.items.length <= shown ? <CircularProgress size={14} /> : undefined}>
                {tier.items.length > shown ? `Show more (${number(tier.items.length - shown)}${onLoadMore ? '+' : ''})` : 'Show more'}
              </Button>
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
 * Everything a place card shows: phone, rating, open now and a photo. Google Maps results carry these already. AI picks come from
 * OpenStreetMap, which rarely has them, so they are looked up on Google Maps (by name, near the place) when the card is shown. Each
 * lookup is a billed Google request, so it runs only for the cards on screen (eight at a time) and once per place per visit.
 */
function usePlaceDetails(p: ExternalPlace) {
  const canLookup = !p.id.startsWith('google:') && p.latitude != null && p.longitude != null;
  const lookup = useQuery({
    queryKey: ['place-contact', p.id],
    queryFn: () => api.get<PlaceContact>('/api/places/contact', { name: p.name, lat: p.latitude, lon: p.longitude }),
    enabled: canLookup,
    staleTime: Infinity,
    gcTime: Infinity,
    retry: false,
  });
  const g = lookup.data?.found ? lookup.data : undefined;
  const phone = p.phone ?? g?.phone ?? null;
  const photoFromGoogle = !p.photoUrl && !!g?.photoUrl;
  return {
    phone,
    website: p.website ?? g?.website ?? null,
    whatsApp: whatsAppHref(phone, p.name),
    rating: p.rating ?? g?.rating ?? null,
    ratingCount: p.ratingCount ?? g?.ratingCount ?? null,
    openNow: p.openNow ?? g?.openNow ?? null,
    photoUrl: p.photoUrl ?? g?.photoUrl ?? null,
    photoCredit: photoFromGoogle ? g?.photoCredit : p.photoCredit,
    photoCreditUrl: photoFromGoogle ? g?.photoCreditUrl : p.photoCreditUrl,
    /** Some of the details came from Google Maps (credited on the card, as Google requires). */
    fromGoogle: !!g,
    loading: canLookup && lookup.isPending,
    /** Looked up, but Google Maps has no number for it (or the lookup failed). */
    noPhone: !phone && (!canLookup || !lookup.isPending),
  };
}

type PlaceDetails = ReturnType<typeof usePlaceDetails>;


/**
 * Highlighted invitation for the owner of a place that isn't on Calling Bell to list it, with the sign-up pre-filled from what is known.
 * Accent-tinted so it stands out from the card's own details and actions.
 */
export function ClaimBusinessBanner({ place, className }: {
  place: { name: string; phone?: string | null; website?: string | null; address?: string | null; sourceId?: string | null }; className?: string;
}) {
  // The search the result came from suggests the category when the map's own is vague.
  const [search] = useSearchParams();
  return (
    <div className={`flex flex-col gap-3 rounded-xl border border-[color-mix(in_srgb,var(--cb-accent)_45%,transparent)] bg-accent-soft p-3 sm:flex-row sm:items-center ${className ?? ''}`}>
      <span aria-hidden className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-surface shadow-[var(--cb-shadow-xs)]"><LogoMark size={22} /></span>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-bold text-ink">Own {place.name}?</p>
        <p className="text-xs text-ink-2">List it free on Calling Bell and get enquiries, bookings and reviews from customers nearby.</p>
      </div>
      <Button size="small" variant="contained" disableElevation component={Link} to={joinHref(place, joinHint(search))}
        sx={{
          flexShrink: 0, bgcolor: 'var(--cb-accent)', color: 'var(--cb-on-accent)', fontWeight: 700, px: 2,
          '&:hover': { bgcolor: 'color-mix(in srgb, var(--cb-accent) 88%, #000)' },
        }}>
        List it free
      </Button>
    </div>
  );
}

/** Rating with review count, or a plain "No reviews yet" (external places have no Calling Bell reviews). */
function PlaceRating({ d }: { d: PlaceDetails }) {
  if (d.rating == null || !d.ratingCount) return <span className="text-muted">No reviews yet</span>;
  return (
    <span className="inline-flex items-center gap-1" aria-label={`Rated ${d.rating.toFixed(1)} out of 5 from ${number(d.ratingCount)} reviews`}>
      <StarRounded sx={{ fontSize: 16, color: '#F4A62C' }} aria-hidden />
      <span className="font-semibold text-ink tabular">{d.rating.toFixed(1)}</span>
      <span className="text-muted">({number(d.ratingCount)} {d.ratingCount === 1 ? 'review' : 'reviews'})</span>
    </span>
  );
}

/** "Open now" / "Closed now" from Google's hours; the opening hours (when known) in a tooltip. */
function OpenBadge({ openNow, hours }: { openNow: boolean | null; hours?: string | null }) {
  if (openNow == null && !hours) return null;
  const badge = openNow == null
    ? <span className="inline-flex items-center gap-1 text-muted"><ScheduleRounded sx={{ fontSize: 15 }} />Hours</span>
    : <span className={`inline-flex items-center gap-1 font-semibold ${openNow ? 'text-success' : 'text-danger'}`}>
        <span aria-hidden className={`h-1.5 w-1.5 rounded-full ${openNow ? 'bg-success' : 'bg-danger'}`} />{openNow ? 'Open now' : 'Closed now'}
      </span>;
  return hours ? <Tooltip title={hours}><span tabIndex={0} className="rounded outline-offset-2">{badge}</span></Tooltip> : badge;
}

/** Marks a result as an outside listing, so it can't be mistaken for a business on Calling Bell. */
function ExternalLabel({ source, compact }: { source: 'ai' | 'google'; compact?: boolean }) {
  const from = source === 'google' ? 'Google Maps' : 'OpenStreetMap';
  return (
    <Tooltip describeChild title={`This business hasn't joined Calling Bell. Details are from ${from}${source === 'ai' ? ' and Google Maps' : ''}, so please confirm them with the business.`}>
      <span tabIndex={0} className={`inline-flex shrink-0 items-center gap-1 rounded-full border border-dashed border-line-strong px-2 py-0.5 font-medium text-muted ${compact ? 'text-[10px]' : 'text-[11px]'}`}>
        {source === 'ai' ? <AutoAwesomeRounded sx={{ fontSize: 12 }} className="text-accent-ink" /> : <MapRounded sx={{ fontSize: 12 }} className="text-info" />}
        {source === 'ai' ? 'AI pick' : 'Google Maps'} · Not on Calling Bell
      </span>
    </Tooltip>
  );
}

/** Why the AI picked a place: one short line, the full reason in a tooltip. */
function AiReason({ text }: { text: string }) {
  return (
    <Tooltip title={text}>
      <p tabIndex={0} className="mt-1 flex min-w-0 items-center gap-1 text-xs text-muted">
        <AutoAwesomeRounded sx={{ fontSize: 13 }} className="shrink-0 text-accent-ink" aria-hidden />
        <span className="truncate"><span className="sr-only">Why the AI picked it: </span>{text}</span>
      </p>
    </Tooltip>
  );
}

/** Photo (or initials) at a fixed size, so cards line up whether or not a photo exists. Google photos keep their author credit. */
function PlaceImage({ p, d, size }: { p: ExternalPlace; d: PlaceDetails; size: 'sm' | 'md' }) {
  const box = size === 'md' ? 'h-20 w-20 sm:h-24 sm:w-24' : 'h-12 w-12';
  if (d.loading && !d.photoUrl) return <Skeleton variant="rounded" className={`${box} shrink-0`} sx={{ borderRadius: '12px' }} />;
  return (
    <div className={`${size === 'md' ? 'w-20 sm:w-24' : 'w-12'} shrink-0`}>
      <Img src={d.photoUrl} alt={d.photoUrl ? `Photo of ${p.name}` : `${p.name}`} fallbackText={p.name} rounded="rounded-xl" className={box} />
      {size === 'md' && d.photoUrl && d.photoCredit && (
        <p className="mt-1 truncate text-[10px] leading-tight text-faint" title={`Photo: ${d.photoCredit}`}>
          {d.photoCreditUrl
            ? <a href={d.photoCreditUrl} target="_blank" rel="noreferrer nofollow" className="hover:underline">{d.photoCredit}</a>
            : d.photoCredit}
        </p>
      )}
    </div>
  );
}

const whatsAppSx = { color: '#128C7E', borderColor: 'rgba(18,140,126,0.5)', '&:hover': { borderColor: '#128C7E', bgcolor: 'rgba(37,211,102,0.08)' } };

/**
 * One clear primary action: Call when there is a number (the fastest way to reach a business), otherwise Directions. WhatsApp and
 * Directions follow as secondary buttons; website and map links are plain text links.
 */
function PlaceActions({ p, d, source, compact }: { p: ExternalPlace; d: PlaceDetails; source: 'ai' | 'google'; compact?: boolean }) {
  return (
    <div className={`flex flex-wrap items-center ${compact ? 'mt-2 gap-1.5' : 'mt-3 gap-2'}`}>
      {d.phone
        ? <Button size="small" variant="contained" disableElevation startIcon={<CallRounded />} href={telHref(d.phone)}>Call {compact ? '' : d.phone}</Button>
        : <Button size="small" variant="contained" disableElevation startIcon={<DirectionsRounded />} href={p.directionsUrl} target="_blank" rel="noreferrer">Directions</Button>}
      {d.whatsApp && <Button size="small" variant="outlined" startIcon={<WhatsApp />} href={d.whatsApp} target="_blank" rel="noreferrer" sx={whatsAppSx}>WhatsApp</Button>}
      {d.phone && <Button size="small" variant="outlined" startIcon={<DirectionsRounded />} href={p.directionsUrl} target="_blank" rel="noreferrer">Directions</Button>}
      {!compact && d.website && <Button size="small" startIcon={<LanguageRounded />} href={d.website} target="_blank" rel="noreferrer nofollow">Website</Button>}
      {!compact && p.sourceUrl && <Button size="small" href={p.sourceUrl} target="_blank" rel="noreferrer">{source === 'ai' ? 'View on map' : 'Google Maps'}</Button>}
    </div>
  );
}

/** A dense place row for narrow panels such as the AI search assistant. */
export function CompactPlace({ p, source }: { p: ExternalPlace; source: 'ai' | 'google' }) {
  const d = usePlaceDetails(p);
  return (
    <article className="rounded-xl border border-line bg-surface p-3">
      <div className="flex gap-2.5">
        <PlaceImage p={p} d={d} size="sm" />
        <div className="min-w-0 flex-1">
          <h3 className="truncate text-sm font-semibold">{p.name}</h3>
          <div className="mt-0.5 flex flex-wrap items-center gap-x-2.5 gap-y-0.5 text-xs">
            <PlaceRating d={d} />
            <OpenBadge openNow={d.openNow} />
            <span className="inline-flex items-center gap-0.5 text-muted"><PlaceRounded sx={{ fontSize: 14 }} />{p.distanceKm.toFixed(1)} km</span>
          </div>
        </div>
      </div>
      <div className="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1">
        <ExternalLabel source={source} compact />
        {d.phone && <a href={telHref(d.phone)} className="text-xs font-semibold text-ink tabular hover:underline">{d.phone}</a>}
        {d.loading && !d.phone && <Skeleton width={90} height={16} aria-label="Finding phone number" />}
      </div>
      <PlaceActions p={p} d={d} source={source} compact />
    </article>
  );
}

/** `onSelect`: the name (a button) or a click anywhere on the card outside its own buttons and links opens the place's details. */
function PlaceCard({ p, source, onSelect }: { p: ExternalPlace; source: 'ai' | 'google'; onSelect?: (place: ExternalPlace, source: 'google' | 'ai') => void }) {
  const d = usePlaceDetails(p);
  const hours = p.openingHours?.replace(/;\s*/g, ' · ');
  return (
    <article className={`card p-4 ${onSelect ? 'cursor-pointer transition-colors hover:border-line-strong' : ''}`}
      onClick={onSelect ? (e) => { if (!(e.target as HTMLElement).closest('a, button, input, [role="button"]') && !window.getSelection()?.toString()) onSelect(p, source); } : undefined}>
      <div className="flex gap-3 sm:gap-4">
        <PlaceImage p={p} d={d} size="md" />
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <h3 className="min-w-0 truncate text-base font-semibold">
              {onSelect ? (
                <button type="button" onClick={() => onSelect(p, source)} className="max-w-full truncate rounded text-left outline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-accent"
                  aria-label={`View details of ${p.name}`}>{p.name}</button>
              ) : p.name}
            </h3>
            <ExternalLabel source={source} />
          </div>
          <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-[13px]">
            <PlaceRating d={d} />
            <OpenBadge openNow={d.openNow} hours={hours} />
            <span className="inline-flex items-center gap-0.5 text-muted"><PlaceRounded sx={{ fontSize: 15 }} />{p.distanceKm.toFixed(1)} km</span>
            {p.kind && <span className="text-muted">{p.kind}</span>}
          </div>
          {p.address && <p className="mt-1 truncate text-[13px] text-ink-2" title={p.address}>{p.address}</p>}
          {d.loading && !d.phone && <Skeleton width={140} height={20} aria-label="Finding phone number" />}
          {d.noPhone && <p className="mt-1 text-[13px] text-muted">No phone number listed</p>}
          {p.aiReason && <AiReason text={p.aiReason} />}
        </div>
      </div>
      <PlaceActions p={p} d={d} source={source} />
      <ClaimBusinessBanner className="mt-3" place={{ name: p.name, phone: d.phone, website: d.website, address: p.address, sourceId: p.id }} />
      <p className="mt-2 text-xs text-muted">
        {d.fromGoogle ? 'Phone, rating and photo from Google Maps' : source === 'google' ? 'Details from Google Maps' : 'Details from OpenStreetMap'}
      </p>
    </article>
  );
}

