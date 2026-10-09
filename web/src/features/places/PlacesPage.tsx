import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useSnackbar } from 'notistack';
import { Button, CircularProgress, IconButton, MenuItem, Skeleton, TextField, Tooltip } from '@mui/material';
import SearchRounded from '@mui/icons-material/SearchRounded';
import MyLocationRounded from '@mui/icons-material/MyLocationRounded';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import ChevronLeftRounded from '@mui/icons-material/ChevronLeftRounded';
import ChevronRightRounded from '@mui/icons-material/ChevronRightRounded';
import TravelExploreRounded from '@mui/icons-material/TravelExploreRounded';
import LocationOffRounded from '@mui/icons-material/LocationOffRounded';
import CloseRounded from '@mui/icons-material/CloseRounded';
import StarRounded from '@mui/icons-material/StarRounded';
import NearMeOutlined from '@mui/icons-material/NearMeOutlined';
import MapOutlined from '@mui/icons-material/MapOutlined';
import ContentCopyRounded from '@mui/icons-material/ContentCopyRounded';
import InfoOutlined from '@mui/icons-material/InfoOutlined';
import TrendingUpRounded from '@mui/icons-material/TrendingUpRounded';
import ExpandMoreRounded from '@mui/icons-material/ExpandMoreRounded';
import ExpandLessRounded from '@mui/icons-material/ExpandLessRounded';
import { api, ApiError } from '@/lib/api';
import { useCategories, useDocumentTitle } from '@/lib/hooks';
import { compactNumber, number } from '@/lib/format';
import type { Category, ExternalPlace, GooglePlace, GooglePlacesPage, PopularSearch, SubCategory } from '@/lib/types';
import { useCity } from '@/stores/city';
import { namesPlace, parseSearch, placeLabel as formatPlace } from '@/lib/searchParse';
import { CitySelect } from '@/components/LocationPicker';
import { EmptyState, ErrorState, Img } from '@/components/ui';
import { PlaceholderTicker } from '@/components/PlaceholderTicker';
import { PlaceActions } from './PlaceParts';
import { countryName, useBrowsingCountryCode, useVisitorCountry } from '@/components/VisitorCountry';
import { ResultDetailDrawer } from '@/features/search/ResultDetail';

interface Coords { lat: number; lon: number }

/** Approximate location from the visitor's IP (shares the cache with the district detection used across the site). */
interface IpLocation { place?: string | null; district?: { cityName: string } | null; latitude?: number | null; longitude?: number | null }

type SortKey = 'best' | 'rating' | 'reviews' | 'distance';

const sorts: { value: SortKey; label: string }[] = [
  { value: 'best', label: 'Best match' },
  { value: 'rating', label: 'Highest rated' },
  { value: 'reviews', label: 'Most reviewed' },
  { value: 'distance', label: 'Nearest' },
];

/** Coordinates are rounded to ~100 m so small GPS jitter doesn't start a new (billed) search. */
const round = (v: number) => Math.round(v * 1000) / 1000;

/** Orders the loaded places; "best" keeps Google's relevance order. Places without the value go last. */
function sortPlaces(places: GooglePlace[], sort: SortKey): GooglePlace[] {
  if (sort === 'best') return places;
  const value = (p: GooglePlace) => (sort === 'rating' ? (p.userRatingCount ? p.rating ?? null : null)
    : sort === 'reviews' ? p.userRatingCount ?? null : p.distanceKm ?? null);
  return [...places].sort((a, b) => {
    const va = value(a), vb = value(b);
    if (va == null || vb == null) return va == null ? (vb == null ? 0 : 1) : -1;
    if (sort === 'distance') return va - vb;
    // Highest rated: ties broken by more reviews.
    return vb - va || (sort === 'rating' ? (b.userRatingCount ?? 0) - (a.userRatingCount ?? 0) : 0);
  });
}

/**
 * Google Maps businesses for a category, sub-category or free-text search, in the selected city or area (from the site-wide city picker).
 * Without a city it searches around the visitor's shared location, or their approximate location from their IP.
 */
export default function PlacesPage() {
  // The result whose details are open, in the same side panel as the search page; the list stays as it was underneath.
  const [selected, setSelected] = useState<GooglePlace | null>(null);
  useDocumentTitle('Explore nearby');
  const { enqueueSnackbar } = useSnackbar();
  const [params, setParams] = useSearchParams();
  const q = params.get('q')?.trim() ?? '';
  const categorySlug = params.get('category');
  const subSlug = params.get('sub');
  const sort = (sorts.some((s) => s.value === params.get('sort')) ? params.get('sort') : 'best') as SortKey;
  const [input, setInput] = useState(q);
  useEffect(() => setInput(q), [q]);

  // What: a sub-category, else a category, else the typed text.
  const { data: categories, isLoading: categoriesLoading } = useCategories();
  // Rotating "e.g. … in <city>" placeholder: the most-listed sub-categories alternating with categories, from the catalogue.
  const tickerNames = useMemo(() => {
    const cats = (categories ?? []).map((c) => c.name);
    const subs = (categories ?? []).flatMap((c) => c.subCategories).sort((a, b) => b.businessCount - a.businessCount).map((s) => s.name);
    return Array.from({ length: Math.max(cats.length, subs.length) }, (_, i) => [subs[i], cats[i]]).flat().filter((n): n is string => !!n);
  }, [categories]);
  const category = categories?.find((c) => c.slug === categorySlug)
    ?? (subSlug ? categories?.find((c) => c.subCategories.some((s) => s.slug === subSlug)) : undefined);
  const sub = category?.subCategories.find((s) => s.slug === subSlug);
  const textQuery = sub?.name ?? category?.name ?? q;

  // Where: the selected area or city; without one, the shared GPS location, then the approximate location from the IP.
  const { citySlug, areaId } = useCity();
  const ip = useQuery({ queryKey: ['geo', 'district'], queryFn: () => api.get<IpLocation>('/api/geo/district'), staleTime: Infinity, retry: false });
  const [gps, setGps] = useState<Coords | null>(null);
  const [locating, setLocating] = useState(false);
  // The visitor's city from their IP (the listed district it falls in, else the place the IP service names), for the search example.
  const ipCity = ip.data?.district?.cityName ?? ip.data?.place ?? null;
  const ipCoords = ip.data?.latitude != null && ip.data.longitude != null ? { lat: ip.data.latitude, lon: ip.data.longitude } : null;
  // A place typed in the search that isn't a listed city or area ("lawyers in Nellore") wins until a location is picked from the menu.
  const placeParam = params.get('place')?.trim() || null;
  const point = citySlug ? null : (gps ?? ipCoords);
  const where = placeParam ? { place: placeParam }
    : citySlug ? { city: citySlug, area: areaId }
      : point ? { lat: round(point.lat), lon: round(point.lon) } : null;
  const lastLocation = useRef(`${citySlug}|${areaId}`);
  useEffect(() => {
    const key = `${citySlug}|${areaId}`;
    if (key === lastLocation.current) return;
    lastLocation.current = key;
    if (params.has('place')) setParams((prev) => { const p = new URLSearchParams(prev); p.delete('place'); return p; }, { replace: true });
  }, [citySlug, areaId, params, setParams]);

  const locate = () => {
    if (!('geolocation' in navigator)) { enqueueSnackbar('Your browser can’t share its location.', { variant: 'warning' }); return; }
    setLocating(true);
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        setGps({ lat: pos.coords.latitude, lon: pos.coords.longitude });
        // Searching around the visitor overrides the picked city.
        useCity.getState().setCity(null);
        setLocating(false);
      },
      (err) => {
        setLocating(false);
        enqueueSnackbar(err.code === err.PERMISSION_DENIED
          ? 'Location permission was denied. Pick a city or area instead.'
          : 'We couldn’t get your location. Please try again.', { variant: 'warning' });
      },
      { enableHighAccuracy: true, timeout: 10_000, maximumAge: 300_000 },
    );
  };

  const results = useInfiniteQuery({
    queryKey: ['places', textQuery, where],
    queryFn: ({ pageParam }) => api.get<GooglePlacesPage>('/api/places/search', { textQuery, ...where, pageToken: pageParam }),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextPageToken ?? undefined,
    enabled: textQuery.length > 0 && where != null,
    staleTime: 300_000,
    retry: (count, err) => count < 1 && !(err instanceof ApiError && err.status < 500),
  });
  const loaded = useMemo(() => results.data?.pages.flatMap((p) => p.places) ?? [], [results.data]);
  const places = useMemo(() => sortPlaces(loaded, sort), [loaded, sort]);
  const first = results.data?.pages[0];

  /** Sets what is searched for; `place` is only changed when given. */
  const setSearch = (next: { q?: string; category?: string; sub?: string; place?: string | null }) =>
    setParams((prev) => {
      const p = new URLSearchParams(prev);
      for (const key of ['q', 'category', 'sub'] as const) {
        const value = next[key];
        if (value) p.set(key, value); else p.delete(key);
      }
      if ('place' in next) { if (next.place) p.set('place', next.place); else p.delete('place'); }
      return p;
    });
  const setOption = (key: 'sort', value: string, fallback: string) => {
    const p = new URLSearchParams(params);
    if (value === fallback) p.delete(key); else p.set(key, value);
    setParams(p, { replace: true });
  };

  // "lawyers in Madhapur" searches lawyers and selects Madhapur in the location menu; an unlisted place ("in Nellore") is searched by name.
  const [parsing, setParsing] = useState(false);
  const submitText = async () => {
    const value = input.trim();
    if (!value) return;
    if (!namesPlace(value)) { setSearch({ q: value }); return; }
    setParsing(true);
    try {
      const parsed = await parseSearch(value, citySlug);
      const what = parsed.subCategorySlug ? { sub: parsed.subCategorySlug, category: categories?.find((c) => c.subCategories.some((s) => s.slug === parsed.subCategorySlug))?.slug }
        : parsed.categorySlug ? { category: parsed.categorySlug } : { q: parsed.query };
      if (parsed.place) {
        lastLocation.current = `${parsed.place.citySlug}|${parsed.place.areaId ?? null}`;
        useCity.getState().setCity(parsed.place.citySlug, parsed.place.areaId ?? null);
        enqueueSnackbar(`Location set to ${formatPlace(parsed.place)}`, { variant: 'info' });
        setSearch({ ...what, place: null });
      } else {
        setSearch({ ...what, place: parsed.placeText ?? null });
      }
      setInput(what.q ?? '');
    } catch {
      setSearch({ q: value });
    } finally {
      setParsing(false);
    }
  };

  const placeLabel = first
    ? first.location.source === 'coordinates'
      ? gps ? 'your current location' : 'your approximate location'
      : [first.location.areaName, first.location.cityName].filter(Boolean).join(', ')
    : null;
  // Only said when the location isn't the one shown in the location menu.
  const locationNote = placeParam ? `Searching in ${first?.location.cityName ?? placeParam} · not a Calling Bell city yet`
    : citySlug ? null
      : gps ? 'Searching around your current location'
        : ipCoords ? 'No city selected · searching around your approximate location'
          : ip.isLoading ? 'Finding your area…' : 'Pick a city or share your location';

  return (
    <div>
      {/* ---------- Search band ---------- */}
      <section className="border-b border-line bg-surface">
        <div className="container-page pb-5 pt-6 md:pb-6 md:pt-8">
          <nav aria-label="Breadcrumb" className="mb-3 text-[13px] text-muted">
            <Link to="/" className="hover:text-ink hover:underline">Home</Link><span className="mx-1.5 text-faint">/</span><span className="text-ink">Explore nearby</span>
          </nav>
          <div className="flex flex-wrap items-end justify-between gap-3">
            <div className="min-w-0">
              <h1 className="text-[26px] font-bold leading-tight tracking-[-0.02em] md:text-[32px]">Explore businesses near you</h1>
              <p className="mt-1.5 max-w-2xl text-[15px] text-muted">Photos, ratings and directions for local businesses and services, from Google Maps.</p>
            </div>
            <span className="inline-flex items-center gap-1.5 rounded-full border border-line bg-subtle px-3 py-1 text-xs font-medium text-muted">
              <MapOutlined sx={{ fontSize: 15 }} className="text-info" />Powered by Google Maps
            </span>
          </div>

          <form role="search" onSubmit={(e) => { e.preventDefault(); void submitText(); }}
            className="mt-5 flex flex-col gap-2 rounded-2xl border border-line bg-surface p-2 shadow-[var(--cb-shadow-sm)] transition-[border-color,box-shadow] focus-within:border-line-strong focus-within:shadow-[var(--cb-shadow-md)] md:flex-row md:items-center md:gap-0 md:rounded-full md:p-1.5">
            <label className="flex min-w-0 flex-1 items-center gap-2.5 rounded-xl px-3 py-2 md:rounded-full md:px-4">
              <SearchRounded className="shrink-0 text-muted" fontSize="small" />
              <span className="min-w-0 flex-1">
                <span className="block text-[11px] font-semibold uppercase tracking-wide text-muted">What</span>
                <span className="relative block">
                  <input value={input} onChange={(e) => setInput(e.target.value)} maxLength={200} aria-label="What are you looking for?"
                    placeholder={tickerNames.length ? '' : `Service or business, e.g. lawyers in ${ipCity ?? 'your city'}`}
                    className="w-full bg-transparent text-[15px] text-ink outline-none placeholder:text-faint" />
                  {!input && <PlaceholderTicker names={tickerNames} prefix="e.g." suffix={`in ${ipCity ?? 'your city'}`} />}
                </span>
              </span>
              {input && (
                <IconButton size="small" aria-label="Clear text" onClick={() => setInput('')} sx={{ color: 'text.secondary' }}><CloseRounded fontSize="small" /></IconButton>
              )}
            </label>
            <span aria-hidden className="mx-1 hidden h-9 w-px bg-line md:block" />
            <div className="flex min-w-0 items-center gap-1 rounded-xl border border-line px-1 md:w-72 md:shrink-0 md:rounded-full md:border-0">
              <div className="min-w-0 flex-1"><CitySelect size="medium" fullWidth bare height={48} /></div>
              <Tooltip title="Use my current location">
                <span>
                  <IconButton aria-label="Use my current location" onClick={locate} disabled={locating} size="small" sx={{ color: 'text.secondary' }}>
                    {locating ? <CircularProgress size={18} /> : <MyLocationRounded fontSize="small" />}
                  </IconButton>
                </span>
              </Tooltip>
            </div>
            <Button type="submit" variant="contained" size="large" disabled={!input.trim() || (!where && !namesPlace(input)) || parsing}
              startIcon={parsing ? <CircularProgress size={16} color="inherit" /> : <SearchRounded />}
              sx={{ minWidth: 124, height: 48, borderRadius: { xs: '12px', md: '999px' }, ml: { md: 1 }, flexShrink: 0 }}>
              Search
            </Button>
          </form>
          {locationNote && (
            <p className="mt-2 flex items-center gap-1.5 px-1 text-[13px] text-muted"><NearMeOutlined sx={{ fontSize: 15 }} />{locationNote}</p>
          )}

          <CategoryRail categories={categories} loading={categoriesLoading} activeSlug={category?.slug}
            onPick={(c) => setSearch(c.slug === category?.slug ? {} : { category: c.slug })} />
          {category && category.subCategories.length > 0 && (
            <SubCategoryRail category={category} activeSlug={sub?.slug}
              onPick={(s) => setSearch(s.slug === sub?.slug ? { category: category.slug } : { category: category.slug, sub: s.slug })} />
          )}
        </div>
      </section>

      {/* ---------- Results ---------- */}
      <section className="container-page py-6" aria-live="polite" aria-busy={results.isLoading}>
        {!where && !ip.isLoading ? (
          <div className="card">
            <EmptyState icon={<LocationOffRounded />} title="Choose where to search"
              message="Pick a city or area in the search bar, or share your location to find places near you."
              action={<Button variant="contained" startIcon={<MyLocationRounded />} onClick={locate} disabled={locating}>Use my current location</Button>} />
          </div>
        ) : !textQuery ? (
          <PopularSearches onPick={(s) => setSearch(s.subCategorySlug ? { category: s.categorySlug ?? undefined, sub: s.subCategorySlug } : { q: s.searchText })} />
        ) : (
          <>
            <div className="mb-5 flex flex-wrap items-end justify-between gap-3">
              <div className="min-w-0">
                <h2 className="text-xl font-bold tracking-[-0.01em] md:text-[22px]">
                  {textQuery}{placeLabel && <span className="font-normal text-muted"> near {placeLabel}</span>}
                </h2>
                <p className="mt-0.5 text-sm text-muted">
                  {results.isLoading ? 'Searching Google Maps…'
                    : `${number(places.length)}${results.hasNextPage ? '+' : ''} ${places.length === 1 ? 'place' : 'places'} · not registered on Calling Bell`}
                </p>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                {(category || q) && (
                  <Button size="small" startIcon={<CloseRounded />} onClick={() => { setInput(''); setSearch({}); }} sx={{ color: 'text.secondary' }}>Clear</Button>
                )}
                <TextField select size="small" fullWidth={false} value={sort} onChange={(e) => setOption('sort', e.target.value, 'best')} sx={{ minWidth: 168 }}
                  slotProps={{ htmlInput: { 'aria-label': 'Sort by' } }}>
                  {sorts.map((s) => <MenuItem key={s.value} value={s.value}>{s.label}</MenuItem>)}
                </TextField>
              </div>
            </div>

            {results.isLoading ? (
              <PlaceSkeletons />
            ) : results.isError ? (
              <div className="card"><ErrorState message={results.error.message} onRetry={() => void results.refetch()} /></div>
            ) : places.length === 0 ? (
              <div className="card">
                <EmptyState icon={<TravelExploreRounded />} title="No places found"
                  message={`Google Maps has no ${textQuery.toLowerCase()} near ${placeLabel ?? 'you'}. Try another category or area.`} />
              </div>
            ) : (
              <>
                <ul className="space-y-3">
                  {places.map((p, i) => (
                    <li key={`${p.name}-${p.latitude}-${p.longitude}-${i}`} className="min-w-0"><PlaceRow place={p} onSelect={setSelected} /></li>
                  ))}
                </ul>
                {results.hasNextPage && (
                  <div className="mt-8 flex flex-col items-center gap-2">
                    <Button variant="outlined" size="large" onClick={() => void results.fetchNextPage()} disabled={results.isFetchingNextPage}
                      startIcon={results.isFetchingNextPage ? <CircularProgress size={16} /> : undefined} sx={{ minWidth: 200 }}>
                      {results.isFetchingNextPage ? 'Loading…' : 'Show more places'}
                    </Button>
                    <span className="text-xs text-muted">Showing {number(places.length)} so far</span>
                  </div>
                )}
                <p className="mt-8 flex items-start gap-1.5 border-t border-line pt-4 text-xs text-faint">
                  <InfoOutlined sx={{ fontSize: 14, mt: '1px' }} />
                  Results, ratings and photos from Google Maps. These businesses are not registered on Calling Bell, so details may be out of date.
                </p>
              </>
            )}
          </>
        )}
      </section>
      <ResultDetailDrawer selected={selected ? { kind: 'place', source: 'google', place: toExternalPlace(selected) } : null} onClose={() => setSelected(null)} />
    </div>
  );
}

/** A Google Maps result in the shape the search page's details panel takes (ids there are "google:<place id>"). */
function toExternalPlace(p: GooglePlace): ExternalPlace {
  return {
    id: `google:${p.id}`, name: p.name, address: p.address, phone: p.phone, rating: p.rating, ratingCount: p.userRatingCount,
    distanceKm: p.distanceKm ?? Number.NaN, directionsUrl: p.directionsUrl, sourceUrl: p.mapsUrl, photoUrl: p.photos[0]?.url,
    latitude: p.latitude, longitude: p.longitude, openNow: p.openNow,
  };
}

/* ---------- Category rails ---------- */

/** Arrow buttons scroll a rail by most of its width; hidden at the ends and on touch screens (swipe there). */
function useRail() {
  const ref = useRef<HTMLDivElement>(null);
  const [edges, setEdges] = useState({ start: true, end: true });
  // Only sets state when an edge actually changes, so scrolling and re-renders don't loop.
  const update = useCallback(() => {
    const el = ref.current;
    if (!el) return;
    const start = el.scrollLeft <= 4, end = el.scrollLeft + el.clientWidth >= el.scrollWidth - 4;
    setEdges((prev) => (prev.start === start && prev.end === end ? prev : { start, end }));
  }, []);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    update();
    // Width changes (window resize) and content changes (tiles loading) both move the edges.
    const observer = new ResizeObserver(update);
    observer.observe(el);
    const content = new MutationObserver(update);
    content.observe(el, { childList: true });
    return () => { observer.disconnect(); content.disconnect(); };
  }, [update]);
  const scroll = (dir: number) => ref.current?.scrollBy({ left: dir * ref.current.clientWidth * 0.8 });
  return { ref, edges, update, scroll };
}

/** Fades whichever end of a rail has more to scroll, so cut-off items read as "more this way". */
function railFade(edges: { start: boolean; end: boolean }): React.CSSProperties | undefined {
  if (edges.start && edges.end) return undefined;
  const mask = `linear-gradient(to right, ${edges.start ? '#000' : 'transparent'}, #000 40px, #000 calc(100% - 40px), ${edges.end ? '#000' : 'transparent'})`;
  return { maskImage: mask, WebkitMaskImage: mask };
}

function RailArrows({ edges, scroll }: { edges: { start: boolean; end: boolean }; scroll: (dir: number) => void }) {
  const sx = {
    position: 'absolute', top: '50%', transform: 'translateY(-50%)', zIndex: 1, bgcolor: 'background.paper', border: 1, borderColor: 'divider',
    boxShadow: 'var(--cb-shadow-sm)', '&:hover': { bgcolor: 'background.paper' }, display: { xs: 'none', md: 'inline-flex' },
  } as const;
  return (
    <>
      {!edges.start && <IconButton size="small" aria-label="Scroll left" onClick={() => scroll(-1)} sx={{ ...sx, left: -14 }}><ChevronLeftRounded fontSize="small" /></IconButton>}
      {!edges.end && <IconButton size="small" aria-label="Scroll right" onClick={() => scroll(1)} sx={{ ...sx, right: -14 }}><ChevronRightRounded fontSize="small" /></IconButton>}
    </>
  );
}

function CategoryRail({ categories, loading, activeSlug, onPick }: {
  categories?: Category[]; loading: boolean; activeSlug?: string; onPick: (c: Category) => void;
}) {
  const rail = useRail();
  // Keep the chosen category in view (e.g. when arriving from a link).
  useEffect(() => {
    rail.ref.current?.querySelector('[aria-pressed="true"]')?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }, [activeSlug, categories, rail.ref]);
  return (
    <div className="relative mt-6">
      <h2 className="sr-only" id="places-categories">Browse by category</h2>
      <RailArrows edges={rail.edges} scroll={rail.scroll} />
      <div ref={rail.ref} onScroll={rail.update} className="rail -mx-1 px-1 pb-1" role="group" aria-labelledby="places-categories" style={railFade(rail.edges)}>
        {loading
          ? Array.from({ length: 10 }, (_, i) => <Skeleton key={i} variant="rounded" width={104} height={84} />)
          : (categories ?? []).map((c) => {
            const active = c.slug === activeSlug;
            return (
              <button key={c.id} type="button" aria-pressed={active} onClick={() => onPick(c)} title={c.name}
                className={`group flex w-[88px] flex-col items-center sm:w-[104px] gap-2 rounded-xl border px-2 pb-2.5 pt-3 text-center transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent ${
                  active ? 'border-accent bg-accent-soft' : 'border-transparent hover:border-line hover:bg-subtle'}`}>
                <span className="grid h-10 w-10 place-items-center rounded-xl" style={{ background: `${c.colorHex ?? '#667085'}1f` }}>
                  <Img src={c.iconUrl} alt="" className="icon-boost h-6 w-6" rounded="rounded" fit="contain" fallbackText={c.name} />
                </span>
                <span className={`line-clamp-2 text-xs leading-tight ${active ? 'font-semibold text-ink' : 'font-medium text-ink-2'}`}>{c.name}</span>
              </button>
            );
          })}
      </div>
    </div>
  );
}

function SubCategoryRail({ category, activeSlug, onPick }: { category: Category; activeSlug?: string; onPick: (s: SubCategory) => void }) {
  const rail = useRail();
  return (
    <div className="relative mt-3 border-t border-line pt-3">
      <RailArrows edges={rail.edges} scroll={rail.scroll} />
      <div ref={rail.ref} onScroll={rail.update} className="rail -mx-1 px-1" role="group" aria-label={`${category.name} services`} style={railFade(rail.edges)}>
        {category.subCategories.map((s) => {
          const active = s.slug === activeSlug;
          return (
            <button key={s.id} type="button" aria-pressed={active} onClick={() => onPick(s)}
              className={`whitespace-nowrap rounded-full border px-3.5 py-1.5 text-[13px] transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent ${
                active ? 'border-transparent bg-inverse font-semibold text-on-inverse' : 'border-line bg-surface font-medium text-ink-2 hover:border-line-strong'}`}>
              {s.name}
            </button>
          );
        })}
      </div>
    </div>
  );
}

/* ---------- Before a search: popular searches from the database ---------- */

/** Shown first; "Show more" reveals the rest (up to POPULAR_MAX). Three rows of four on wide screens. */
const POPULAR_FIRST = 12;
const POPULAR_MAX = 48;

/**
 * The most searched services and searches for the country being browsed (dbo.PopularSearches via GET /api/places/popular-searches,
 * ranked by searches made here). A service opens its category; a typed search ("Emergency plumber") is searched as text.
 */
function PopularSearches({ onPick }: { onPick: (s: PopularSearch) => void }) {
  const visitor = useVisitorCountry();
  const country = useBrowsingCountryCode();
  const [expanded, setExpanded] = useState(false);
  const { data, isPending: isLoading, isError, refetch } = useQuery({
    queryKey: ['popular-searches', country ?? ''],
    queryFn: () => api.get<PopularSearch[]>('/api/places/popular-searches', { country, limit: POPULAR_MAX }),
    staleTime: 5 * 60_000,
    // Once the country is known (or couldn't be), so the list loads once, for the right country.
    enabled: !visitor.isLoading,
  });
  const items = data ?? [];
  const shown = expanded ? items : items.slice(0, POPULAR_FIRST);
  const countryLabel = country ? countryName(country) : null;

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-end justify-between gap-2">
        <div>
          <h2 className="text-xl font-bold tracking-[-0.01em]">Popular searches</h2>
          <p className="mt-0.5 text-sm text-muted">
            What people search for most{countryLabel ? ` in ${countryLabel}` : ''}. Pick one to see places near you, or choose a category above.
          </p>
        </div>
        {items.length > POPULAR_FIRST && (
          <span className="text-xs text-muted tabular">{expanded ? `All ${items.length}` : `${POPULAR_FIRST} of ${items.length}`}</span>
        )}
      </div>
      {isLoading ? (
        <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4" aria-hidden>
          {Array.from({ length: POPULAR_FIRST }, (_, i) => <li key={i}><Skeleton variant="rounded" height={68} sx={{ borderRadius: '12px' }} /></li>)}
        </ul>
      ) : isError ? (
        <div className="card"><ErrorState message="We couldn’t load popular searches." onRetry={() => void refetch()} /></div>
      ) : items.length === 0 ? (
        <div className="card"><EmptyState icon={<TravelExploreRounded />} title="What are you looking for?" message="Search for any service or business above." /></div>
      ) : (
        <>
          <ul id="popular-searches" className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {shown.map((s, i) => (
              <li key={s.code}>
                <button type="button" onClick={() => onPick(s)}
                  className="card group flex w-full items-center gap-3 p-3 text-left transition-[border-color,box-shadow] hover:border-line-strong hover:shadow-[var(--cb-shadow-md)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent">
                  <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl" style={{ background: `${s.colorHex ?? '#667085'}1f` }}>
                    {s.iconUrl
                      ? <Img src={s.iconUrl} alt="" className="icon-boost h-6 w-6" rounded="rounded" fit="contain" fallbackText={s.label} />
                      : <SearchRounded sx={{ fontSize: 22, color: s.colorHex ?? '#667085' }} />}
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-[15px] font-semibold">{s.label}</span>
                    <span className="flex min-w-0 items-center gap-1 text-xs text-muted">
                      {i < 3 && !expanded && <TrendingUpRounded sx={{ fontSize: 14 }} className="shrink-0 text-accent-ink" aria-label="Trending" />}
                      {s.group && <span className="min-w-0 truncate">{s.group}</span>}
                      {s.searchCount > 0 && <span className="shrink-0 tabular">{s.group && '· '}{compactNumber(s.searchCount)} searches</span>}
                    </span>
                  </span>
                  <ChevronRightRounded className="shrink-0 text-faint transition-transform group-hover:translate-x-0.5" fontSize="small" />
                </button>
              </li>
            ))}
          </ul>
          {items.length > POPULAR_FIRST && (
            <div className="mt-5 flex justify-center">
              <Button variant="outlined" onClick={() => setExpanded((v) => !v)} aria-expanded={expanded} aria-controls="popular-searches"
                endIcon={expanded ? <ExpandLessRounded /> : <ExpandMoreRounded />} sx={{ minWidth: 200 }}>
                {expanded ? 'Show fewer' : `Show ${items.length - POPULAR_FIRST} more`}
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

/* ---------- Result rows ---------- */

/**
 * One business per row: photo, name, rating, distance, open now and address, then the actions. Below 1024px the actions sit under the
 * details (an even five-column bar on phones); from 1024px they share the row. Call and WhatsApp stay in place, disabled, when Google
 * Maps has no number, so every row lines up the same way.
 */
function PlaceRow({ place: p, onSelect }: { place: GooglePlace; onSelect: (p: GooglePlace) => void }) {
  const { enqueueSnackbar } = useSnackbar();
  const photo = p.photos[0];
  const rated = p.rating != null && !!p.userRatingCount;
  const copyAddress = async () => {
    if (!p.address) return;
    try { await navigator.clipboard.writeText(`${p.name}, ${p.address}`); enqueueSnackbar('Address copied', { variant: 'success' }); }
    catch { enqueueSnackbar('Couldn’t copy the address', { variant: 'warning' }); }
  };

  return (
    <article className="card group relative flex flex-col gap-3 p-3 transition-[border-color,box-shadow] hover:border-line-strong hover:shadow-[var(--cb-shadow-md)] sm:p-4 lg:flex-row lg:items-center lg:gap-6">
      <div className="flex min-w-0 flex-1 gap-3 sm:gap-4">
        <Img src={photo?.url} alt={photo ? `Photo of ${p.name}` : p.name} fallbackText={p.name} aspect="1/1" width={96} height={96}
          className="h-[72px] w-[72px] shrink-0 sm:h-24 sm:w-24" />
        <div className="min-w-0 flex-1">
          <h3 className="line-clamp-2 text-[15px] font-semibold leading-snug text-ink sm:text-base">
            {/* The button covers the whole row (the actions, copy button and photo credit sit above it), so any part of the row opens the
                details panel, as on the search page. */}
            {p.id ? (
              <button type="button" onClick={() => onSelect(p)} aria-haspopup="dialog"
                className="rounded-sm text-left group-hover:underline underline-offset-2 after:absolute after:inset-0 after:rounded-xl after:content-[''] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--cb-accent)]">
                {p.name}
              </button>
            ) : p.name}
          </h3>
          <p className="mt-1 flex flex-wrap items-center gap-x-2.5 gap-y-1 text-[13px] text-muted">
            {rated ? (
              <span className="inline-flex items-center gap-0.5" aria-label={`Rated ${p.rating!.toFixed(1)} out of 5 from ${number(p.userRatingCount!)} reviews`}>
                <StarRounded sx={{ fontSize: 16, color: '#F4A62C' }} aria-hidden />
                <span className="font-semibold text-ink tabular">{p.rating!.toFixed(1)}</span>
                <span className="tabular">({number(p.userRatingCount!)})</span>
              </span>
            ) : <span>No ratings yet</span>}
            {p.distanceKm != null && (
              <span className="inline-flex items-center gap-0.5 tabular"><NearMeOutlined sx={{ fontSize: 14 }} aria-hidden />{p.distanceKm.toFixed(1)} km</span>
            )}
            {p.openNow != null && (
              <span className={`inline-flex items-center gap-1 font-medium ${p.openNow ? 'text-success' : 'text-danger'}`}>
                <span aria-hidden className={`h-1.5 w-1.5 rounded-full ${p.openNow ? 'bg-success' : 'bg-danger'}`} />{p.openNow ? 'Open now' : 'Closed'}
              </span>
            )}
          </p>
          {p.address && (
            <p className="mt-1.5 flex items-start gap-1 text-[13px] text-ink-2">
              <PlaceOutlined sx={{ fontSize: 16, mt: '1px' }} className="shrink-0 text-muted" aria-hidden />
              <span className="line-clamp-2 min-w-0 lg:line-clamp-1">{p.address}</span>
              <Tooltip title="Copy name and address">
                <IconButton size="small" aria-label={`Copy address of ${p.name}`} onClick={() => void copyAddress()} sx={{ p: '2px', mt: '-1px', color: 'text.secondary', position: 'relative', zIndex: 1 }}>
                  <ContentCopyRounded sx={{ fontSize: 14 }} />
                </IconButton>
              </Tooltip>
            </p>
          )}
          {photo && photo.attributions.length > 0 && (
            <p className="relative z-[1] mt-1 w-fit max-w-full truncate text-[11px] text-faint">
              Photo: {photo.attributions.map((a, i) => (
                <span key={a.displayName}>{i > 0 && ', '}{a.uri
                  ? <a href={a.uri} target="_blank" rel="noreferrer nofollow" className="underline-offset-2 hover:underline">{a.displayName}</a>
                  : a.displayName}</span>
              ))}
            </p>
          )}
        </div>
      </div>

      <PlaceActions place={{ ...p, mapsUrl: p.mapsUrl ?? p.directionsUrl, sourceId: p.id ? `google:${p.id}` : null }} compactMid
        className="border-t border-line pt-3 lg:shrink-0 lg:flex-nowrap lg:border-0 lg:pt-0" />
    </article>
  );
}

function PlaceSkeletons() {
  return (
    <ul className="space-y-3" aria-hidden>
      {Array.from({ length: 6 }, (_, i) => (
        <li key={i} className="card flex flex-col gap-3 p-3 sm:p-4 lg:flex-row lg:items-center lg:gap-6">
          <div className="flex flex-1 gap-3 sm:gap-4">
            <Skeleton variant="rounded" sx={{ width: { xs: 72, sm: 96 }, height: { xs: 72, sm: 96 }, flexShrink: 0, borderRadius: '8px' }} />
            <div className="flex-1"><Skeleton width="55%" height={24} /><Skeleton width="35%" /><Skeleton width="80%" /></div>
          </div>
          <div className="grid grid-cols-5 gap-1.5 sm:flex sm:gap-2">
            {Array.from({ length: 5 }, (_, j) => <Skeleton key={j} variant="rounded" sx={{ height: { xs: 50, sm: 36 }, width: { sm: 96 }, borderRadius: '8px' }} />)}
          </div>
        </li>
      ))}
    </ul>
  );
}
