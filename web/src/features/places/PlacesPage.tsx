import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useSnackbar } from 'notistack';
import { Button, CircularProgress, IconButton, MenuItem, Skeleton, TextField, ToggleButton, ToggleButtonGroup, Tooltip } from '@mui/material';
import SearchRounded from '@mui/icons-material/SearchRounded';
import MyLocationRounded from '@mui/icons-material/MyLocationRounded';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import DirectionsRounded from '@mui/icons-material/DirectionsRounded';
import ChevronLeftRounded from '@mui/icons-material/ChevronLeftRounded';
import ChevronRightRounded from '@mui/icons-material/ChevronRightRounded';
import TravelExploreRounded from '@mui/icons-material/TravelExploreRounded';
import LocationOffRounded from '@mui/icons-material/LocationOffRounded';
import CloseRounded from '@mui/icons-material/CloseRounded';
import StarRounded from '@mui/icons-material/StarRounded';
import NearMeOutlined from '@mui/icons-material/NearMeOutlined';
import MapOutlined from '@mui/icons-material/MapOutlined';
import ContentCopyRounded from '@mui/icons-material/ContentCopyRounded';
import GridViewRounded from '@mui/icons-material/GridViewRounded';
import ViewListRounded from '@mui/icons-material/ViewListRounded';
import InfoOutlined from '@mui/icons-material/InfoOutlined';
import { api, ApiError } from '@/lib/api';
import { useCategories, useDocumentTitle } from '@/lib/hooks';
import { number } from '@/lib/format';
import type { Category, GooglePlace, GooglePlacesPage, SubCategory } from '@/lib/types';
import { useCity } from '@/stores/city';
import { namesPlace, parseSearch, placeLabel as formatPlace } from '@/lib/searchParse';
import { CitySelect } from '@/layouts/CustomerLayout';
import { EmptyState, ErrorState, Img } from '@/components/ui';
import { PlaceholderTicker } from '@/components/PlaceholderTicker';

interface Coords { lat: number; lon: number }

/** Approximate location from the visitor's IP (shares the cache with the district detection used across the site). */
interface IpLocation { place?: string | null; district?: { cityName: string } | null; latitude?: number | null; longitude?: number | null }

type SortKey = 'best' | 'rating' | 'reviews' | 'distance';
type ViewKey = 'grid' | 'list';

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
  useDocumentTitle('Explore nearby');
  const { enqueueSnackbar } = useSnackbar();
  const [params, setParams] = useSearchParams();
  const q = params.get('q')?.trim() ?? '';
  const categorySlug = params.get('category');
  const subSlug = params.get('sub');
  const sort = (sorts.some((s) => s.value === params.get('sort')) ? params.get('sort') : 'best') as SortKey;
  const view: ViewKey = params.get('view') === 'list' ? 'list' : 'grid';
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
  const setOption = (key: 'sort' | 'view', value: string, fallback: string) => {
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
          <QuickStart categories={categories} onPick={(s) => setSearch({ category: s.categorySlug, sub: s.slug })} />
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
                <ToggleButtonGroup size="small" exclusive value={view} onChange={(_, v: ViewKey | null) => v && setOption('view', v, 'grid')} aria-label="Layout">
                  <ToggleButton value="grid" aria-label="Grid view"><GridViewRounded fontSize="small" /></ToggleButton>
                  <ToggleButton value="list" aria-label="List view"><ViewListRounded fontSize="small" /></ToggleButton>
                </ToggleButtonGroup>
              </div>
            </div>

            {results.isLoading ? (
              <PlaceSkeletons view={view} />
            ) : results.isError ? (
              <div className="card"><ErrorState message={results.error.message} onRetry={() => void results.refetch()} /></div>
            ) : places.length === 0 ? (
              <div className="card">
                <EmptyState icon={<TravelExploreRounded />} title="No places found"
                  message={`Google Maps has no ${textQuery.toLowerCase()} near ${placeLabel ?? 'you'}. Try another category or area.`} />
              </div>
            ) : (
              <>
                <ul className={view === 'grid' ? 'grid gap-5 sm:grid-cols-2 lg:grid-cols-3' : 'space-y-4'}>
                  {places.map((p, i) => (
                    <li key={`${p.name}-${p.latitude}-${p.longitude}-${i}`} className="min-w-0"><PlaceCard place={p} layout={view} /></li>
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
    </div>
  );
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

/* ---------- Before a search: popular services from the catalogue ---------- */

function QuickStart({ categories, onPick }: { categories?: Category[]; onPick: (s: SubCategory) => void }) {
  const popular = useMemo(() => (categories ?? []).flatMap((c) => c.subCategories)
    .sort((a, b) => b.businessCount - a.businessCount).slice(0, 8), [categories]);
  return (
    <div>
      <div className="mb-4">
        <h2 className="text-xl font-bold tracking-[-0.01em]">Popular searches</h2>
        <p className="mt-0.5 text-sm text-muted">Pick a service to see places near you, or choose a category above.</p>
      </div>
      {popular.length === 0 ? (
        <div className="card"><EmptyState icon={<TravelExploreRounded />} title="What are you looking for?" message="Search for any service or business above." /></div>
      ) : (
        <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          {popular.map((s) => (
            <li key={s.id}>
              <button type="button" onClick={() => onPick(s)}
                className="card flex w-full items-center gap-3 p-3 text-left focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent">
                <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl" style={{ background: `${s.colorHex ?? '#667085'}1f` }}>
                  <Img src={s.iconUrl} alt="" className="icon-boost h-6 w-6" rounded="rounded" fit="contain" fallbackText={s.name} />
                </span>
                <span className="min-w-0">
                  <span className="block truncate text-[15px] font-semibold">{s.name}</span>
                  <span className="block truncate text-xs text-muted">{s.categoryName}</span>
                </span>
                <ChevronRightRounded className="ml-auto shrink-0 text-faint" fontSize="small" />
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/* ---------- Result cards ---------- */

function PlaceCard({ place: p, layout }: { place: GooglePlace; layout: ViewKey }) {
  const { enqueueSnackbar } = useSnackbar();
  const [index, setIndex] = useState(0);
  const photo = p.photos[index];
  const count = p.photos.length;
  const step = (d: number) => setIndex((i) => (i + d + count) % count);
  const rated = p.rating != null && !!p.userRatingCount;
  const copyAddress = async () => {
    if (!p.address) return;
    try { await navigator.clipboard.writeText(`${p.name}, ${p.address}`); enqueueSnackbar('Address copied', { variant: 'success' }); }
    catch { enqueueSnackbar('Couldn’t copy the address', { variant: 'warning' }); }
  };
  const list = layout === 'list';
  const arrow = {
    position: 'absolute', top: '50%', transform: 'translateY(-50%)', bgcolor: 'rgba(255,255,255,0.92)', color: '#161616',
    '&:hover': { bgcolor: '#fff' }, boxShadow: 'var(--cb-shadow-sm)',
  } as const;

  return (
    <article className={`card group flex h-full overflow-hidden transition-[border-color,box-shadow] hover:border-line-strong hover:shadow-[var(--cb-shadow-md)] ${list ? 'flex-col sm:flex-row' : 'flex-col'}`}>
      <div className={`relative shrink-0 ${list ? 'sm:w-64' : ''}`}>
        <Img src={photo?.url} alt={photo ? `Photo of ${p.name}` : p.name} fallbackText={p.name} rounded="rounded-none" aspect="4/3" className="h-full w-full" />
        {/* Rating and distance over the photo; a soft gradient keeps them legible on any image. */}
        <span aria-hidden className="pointer-events-none absolute inset-x-0 top-0 h-16 bg-gradient-to-b from-black/45 to-transparent" />
        {rated && (
          <span className="absolute left-2.5 top-2.5 inline-flex items-center gap-0.5 rounded-md bg-white/95 px-1.5 py-0.5 text-xs font-bold text-[#161616] shadow-sm"
            aria-label={`Rated ${p.rating!.toFixed(1)} out of 5 from ${p.userRatingCount} reviews`}>
            <StarRounded sx={{ fontSize: 14, color: '#F4A62C' }} />{p.rating!.toFixed(1)}
            <span className="font-medium text-[#667085]">({number(p.userRatingCount!)})</span>
          </span>
        )}
        {p.distanceKm != null && (
          <span className="absolute right-2.5 top-2.5 inline-flex items-center gap-0.5 rounded-md bg-black/60 px-1.5 py-0.5 text-xs font-medium text-white tabular">
            <PlaceOutlined sx={{ fontSize: 13 }} />{p.distanceKm.toFixed(1)} km
          </span>
        )}
        {count > 1 && (
          <>
            {/* Arrows show on hover/focus with a mouse; always on touch screens. */}
            <div className="opacity-0 transition-opacity group-hover:opacity-100 group-focus-within:opacity-100 [@media(hover:none)]:opacity-100">
              <IconButton size="small" aria-label="Previous photo" onClick={() => step(-1)} sx={{ ...arrow, left: 8 }}><ChevronLeftRounded fontSize="small" /></IconButton>
              <IconButton size="small" aria-label="Next photo" onClick={() => step(1)} sx={{ ...arrow, right: 8 }}><ChevronRightRounded fontSize="small" /></IconButton>
            </div>
            <span className="absolute inset-x-0 bottom-2 flex justify-center gap-1" aria-label={`Photo ${index + 1} of ${count}`}>
              {p.photos.map((_, i) => (
                <span key={i} aria-hidden className={`h-1.5 rounded-full transition-all ${i === index ? 'w-4 bg-white' : 'w-1.5 bg-white/60'}`} />
              ))}
            </span>
          </>
        )}
      </div>

      <div className="flex min-w-0 flex-1 flex-col p-4">
        <h3 className="line-clamp-2 text-base font-semibold leading-snug">{p.name}</h3>
        {!rated && <p className="mt-1 text-xs text-muted">No ratings yet</p>}
        {p.address && (
          <p className="mt-2 flex items-start gap-1.5 text-[13px] text-ink-2">
            <PlaceOutlined sx={{ fontSize: 16, mt: '1px' }} className="shrink-0 text-muted" />
            <span className={list ? 'line-clamp-3' : 'line-clamp-2'}>{p.address}</span>
          </p>
        )}
        {photo && photo.attributions.length > 0 && (
          <p className="mt-2 truncate text-[11px] text-faint">
            Photo: {photo.attributions.map((a, i) => (
              <span key={a.displayName}>{i > 0 && ', '}{a.uri
                ? <a href={a.uri} target="_blank" rel="noreferrer nofollow" className="underline-offset-2 hover:underline">{a.displayName}</a>
                : a.displayName}</span>
            ))}
          </p>
        )}
        <div className="mt-auto flex items-center gap-2 pt-4">
          <Button variant="contained" size="small" startIcon={<DirectionsRounded />} href={p.directionsUrl} target="_blank" rel="noreferrer"
            sx={{ flex: list ? '0 0 auto' : 1 }}>
            Directions
          </Button>
          {p.address && (
            <Tooltip title="Copy name and address">
              <IconButton size="small" aria-label={`Copy address of ${p.name}`} onClick={() => void copyAddress()}
                sx={{ border: 1, borderColor: 'divider', borderRadius: '8px' }}>
                <ContentCopyRounded sx={{ fontSize: 16 }} />
              </IconButton>
            </Tooltip>
          )}
        </div>
      </div>
    </article>
  );
}

function PlaceSkeletons({ view }: { view: ViewKey }) {
  return (
    <ul className={view === 'grid' ? 'grid gap-5 sm:grid-cols-2 lg:grid-cols-3' : 'space-y-4'} aria-hidden>
      {Array.from({ length: 6 }, (_, i) => (
        <li key={i} className={`card flex overflow-hidden ${view === 'list' ? 'flex-col sm:flex-row' : 'flex-col'}`}>
          <Skeleton variant="rectangular" sx={{ aspectRatio: '4/3', height: 'auto', width: { xs: '100%', sm: view === 'list' ? 256 : '100%' }, flexShrink: 0 }} />
          <div className="flex-1 p-4"><Skeleton width="70%" height={24} /><Skeleton width="90%" /><Skeleton width="60%" /><Skeleton variant="rounded" height={32} sx={{ mt: 2 }} /></div>
        </li>
      ))}
    </ul>
  );
}
