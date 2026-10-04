import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Checkbox, Chip, CircularProgress, Drawer, FormControlLabel, MenuItem, Pagination, Radio, RadioGroup, TextField,
} from '@mui/material';
import TuneRounded from '@mui/icons-material/TuneRounded';
import SearchRounded from '@mui/icons-material/SearchRounded';
import { api } from '@/lib/api';
import { useCategories, useCities, useCityAreas, useDebounced, useDocumentTitle, useLookup } from '@/lib/hooks';
import { CityAutocomplete } from '@/components/CityAutocomplete';
import { usePresence } from '@/lib/realtime';
import { number } from '@/lib/format';
import type { AppliedFilters, Banner, BusinessCard as Card, Pagination as Meta, Plan, SearchSuggestion } from '@/lib/types';
import { BusinessCard, BusinessCardSkeleton } from '@/components/BusinessCard';
import { EmptyState, ErrorState, Img, PageHeader } from '@/components/ui';
import { useVisitorDistrict } from '@/components/VisitorCountry';
import { useCity } from '@/stores/city';
import { SearchSuggest } from '@/components/SearchSuggest';
import { PlaceholderTicker } from '@/components/PlaceholderTicker';
import { ExternalResults, type ResultSource, sourcePanelId, sourceTabId, SourceTabs, useExternalSearch } from './ExternalResults';
import InfoOutlined from '@mui/icons-material/InfoOutlined';
import VerifiedRounded from '@mui/icons-material/VerifiedRounded';
import LocationOffRounded from '@mui/icons-material/LocationOffRounded';
import PlaceRounded from '@mui/icons-material/PlaceRounded';
import { useSnackbar } from 'notistack';
import { namesPlace, parsedSearchParams, parseSearch, placeLabel, type ParsedSearch } from '@/lib/searchParse';

interface SearchResponse { data: Card[]; pagination: Meta; applied: AppliedFilters }

const sorts = [
  { value: 'relevance', label: 'Relevance' },
  { value: 'rating', label: 'Highest rated' },
  { value: 'reviews', label: 'Most reviewed' },
  { value: 'distance', label: 'Nearest' },
  { value: 'price', label: 'Lowest price' },
  { value: 'newest', label: 'Newest' },
];

const flags = [
  { key: 'openNow', label: 'Open now' },
  { key: 'verifiedOnly', label: 'Verified only' },
  { key: 'onlineBooking', label: 'Instant booking' },
  { key: 'homeService', label: 'Home visits' },
  { key: 'video', label: 'Video consultation' },
] as const;

export default function SearchPage() {
  const [params, setParams] = useSearchParams();
  const navigate = useNavigate();
  const [text, setText] = useState(params.get('q') ?? '');
  const debounced = useDebounced(text);
  const [drawer, setDrawer] = useState(false);
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();

  // Location: the city/area chosen in the header dropdown applies to every search that doesn't name its own, and any change
  // made here (filters, chips) is reflected back in the dropdown, so the two always agree.
  const storeCity = useCity((s) => s.citySlug);
  const storeArea = useCity((s) => s.areaId);
  const setLocation = useCity((s) => s.setCity);
  const city = params.get('city') ?? storeCity;
  const areaId = params.get('area') ?? (city && city === storeCity ? storeArea : null);
  // Runs when the URL changes only (store changes made here are followed by a URL change), reading the store's latest values.
  useEffect(() => {
    // A place typed in the search that isn't a listed city ("in Nellore") stands in for the dropdown's location.
    if (params.get('place')) return;
    const { citySlug: selCity, areaId: selArea } = useCity.getState();
    const urlCity = params.get('city');
    const urlArea = params.get('area');
    if (!urlCity || (urlCity === selCity && !urlArea && selArea)) {
      if (!selCity) return;
      const next = new URLSearchParams(params);
      next.set('city', selCity);
      if (selArea) next.set('area', selArea);
      setParams(next, { replace: true });
    } else if (urlCity !== selCity || (urlArea && urlArea !== selArea)) {
      setLocation(urlCity, urlArea);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params]);

  // Keep debounced text in the URL (search debounce). Text naming a place ("lawyers in Nellore") waits for Enter, so a half-typed
  // place isn't looked up.
  useEffect(() => {
    if ((params.get('q') ?? '') === debounced || namesPlace(debounced)) return;
    update({ q: debounced || null });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debounced]);

  /** "lawyers in Madhapur": search "lawyers" (or its sub-category) and select Madhapur in the location dropdown. */
  const applyParsed = (p: ParsedSearch) => {
    setText(p.subCategorySlug || p.categorySlug ? '' : p.query);
    const { citySlug: selCity, areaId: selArea } = useCity.getState();
    if (p.place) {
      setLocation(p.place.citySlug, p.place.areaId ?? null);
      if (p.place.citySlug !== selCity || (p.place.areaId ?? null) !== selArea)
        enqueueSnackbar(`Location set to ${placeLabel(p.place)}`, { variant: 'info' });
    }
    setParams(parsedSearchParams(p, params, { citySlug: selCity, areaId: selArea }), { replace: true });
  };
  const [parsing, setParsing] = useState(false);
  /** Text whose parse failed: it is then searched as typed instead of waiting. */
  const [parseFailed, setParseFailed] = useState<string | null>(null);
  const parseAndApply = async (value: string) => {
    setParsing(true);
    try { applyParsed(await parseSearch(value, useCity.getState().citySlug)); }
    catch { setParseFailed(value.trim()); update({ q: value.trim() || null }); }
    finally { setParsing(false); }
  };
  const submitText = () => {
    if (namesPlace(text)) void parseAndApply(text);
    else update({ q: text.trim() || null });
  };
  // Links and bookmarks such as /search?q=lawyers+in+Nellore are parsed the same way.
  const urlQ = params.get('q') ?? '';
  useEffect(() => {
    if (namesPlace(urlQ)) void parseAndApply(urlQ);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [urlQ]);

  const update = (patch: Record<string, string | null>, resetPage = true) => {
    const next = new URLSearchParams(params);
    Object.entries(patch).forEach(([k, v]) => (v === null || v === '' ? next.delete(k) : next.set(k, v)));
    if (resetPage) next.delete('page');
    // A new search (what or where) starts on its default tab again.
    if (['q', 'category', 'sub', 'city', 'area', 'place'].some((k) => k in patch)) next.delete('tab');
    if ('city' in patch || 'area' in patch) {
      const nextCity = 'city' in patch ? patch.city || null : city;
      const nextArea = 'area' in patch ? patch.area || null : 'city' in patch ? null : areaId;
      setLocation(nextCity, nextArea);
      if (!nextArea) next.delete('area');
      next.delete('place'); // a listed city/area replaces a place typed in the search
    }
    setParams(next, { replace: true });
  };

  /** Clears the search and filters but keeps the selected location. */
  const clearAll = () => {
    setText('');
    if (params.get('place')) { setParams(new URLSearchParams(), { replace: true }); return; } // the URL sync adds the dropdown's location
    const next = new URLSearchParams();
    if (city) next.set('city', city);
    if (areaId) next.set('area', areaId);
    setParams(next, { replace: true });
  };

  // On this page suggestions refine the current results instead of navigating away (businesses still open their profile).
  const applySuggestion = (s: SearchSuggestion) => {
    if (s.kind === 'Business') { navigate(`/b/${s.slug}`); return; }
    const q = s.kind === 'Service' ? s.label : '';
    setText(q);
    if (s.kind === 'Category') update({ category: s.slug, sub: null, q: null });
    else update({ category: null, sub: s.subCategorySlug ?? s.slug, q: q || null });
  };

  const query = {
    q: params.get('q'), category: params.get('category'), sub: params.get('sub'), city, areaId,
    minRating: params.get('minRating'), availability: params.get('availability'), openNow: params.get('openNow') === 'true',
    verifiedOnly: params.get('verifiedOnly') === 'true', homeService: params.get('homeService') === 'true',
    videoConsultation: params.get('video') === 'true', onlineBooking: params.get('onlineBooking') === 'true',
    plan: params.get('plan'), sort: params.get('sort') ?? 'relevance', page: Number(params.get('page') ?? 1), pageSize: 12,
  };
  const queryKey = ['search', query];
  /** A place typed in the search that isn't a listed city or area ("lawyers in Nellore"): no registered businesses there. */
  const place = params.get('place')?.trim() || null;
  /** The URL's text still names a place ("lawyers in Nellore"): it is being parsed, so nothing is searched until it is applied. */
  const parsePending = namesPlace(query.q ?? '') && query.q?.trim() !== parseFailed;

  const dbQuery = useQuery({
    queryKey,
    queryFn: async () => {
      const env = await api.envelope<Card[]>('/api/businesses', query);
      return env as unknown as SearchResponse;
    },
    placeholderData: keepPreviousData,
    enabled: !place && !parsePending,
  });
  const { isLoading, isFetching, isError, refetch } = dbQuery;
  const data = place ? undefined : dbQuery.data;
  // Beyond the platform: Google Maps businesses, then AI-recommended real places, near the selected area (or the typed place),
  // always after registered ones.
  const external = useExternalSearch(place
    ? { q: query.q, category: query.category, sub: query.sub, place }
    : parsePending ? {}
    : { q: query.q, category: query.category, sub: query.sub, city: query.city, areaId: query.areaId });
  const searchesExternal = !!(query.q?.trim() || query.category || query.sub);
  // Results tab (?tab=google|ai). Without a choice: Calling Bell first, or Google Maps (else AI) when there are no registered businesses.
  const tabParam = params.get('tab');
  const dbEmpty = !!place || (!!data && data.pagination.totalCount === 0);
  const googleUsable = external.data ? !['off', 'skipped'].includes(external.data.google.status) : true;
  const tab: ResultSource = !searchesExternal ? 'db'
    : tabParam === 'google' || tabParam === 'ai' || tabParam === 'db' ? tabParam
      : dbEmpty ? (googleUsable ? 'google' : 'ai') : 'db';
  const setTab = (next: ResultSource) => {
    const p = new URLSearchParams(params);
    p.set('tab', next);
    setParams(p, { replace: true });
  };
  const { data: categoryTree } = useCategories();
  // Rotating "e.g. …" placeholder: categories alternating with the most-listed sub-categories, from the catalogue.
  const tickerNames = useMemo(() => {
    const cats = (categoryTree ?? []).map((c) => c.name);
    const subs = (categoryTree ?? []).flatMap((c) => c.subCategories)
      .sort((a, b) => b.businessCount - a.businessCount).map((s) => s.name);
    return Array.from({ length: Math.max(cats.length, subs.length) }, (_, i) => [subs[i], cats[i]]).flat().filter((n): n is string => !!n);
  }, [categoryTree]);
  const placeWhat = place
    ? categoryTree?.flatMap((c) => c.subCategories).find((s) => s.slug === query.sub)?.name
      ?? categoryTree?.find((c) => c.slug === query.category)?.name ?? (query.q ? `“${query.q}”` : 'Businesses')
    : null;
  const { data: banners } = useQuery({ queryKey: ['banners', 'SearchTop'], queryFn: () => api.get<Banner[]>('/api/banners', { placement: 'SearchTop' }), staleTime: 600_000 });
  // Promote the banner for the sub-category being searched; otherwise one picked per visit, so every campaign gets seen.
  const [bannerSeed] = useState(() => Math.floor(Math.random() * 1000));
  const banner = useMemo(() => {
    if (!banners?.length) return undefined;
    return banners.find((b) => query.sub && b.linkUrl?.includes(`sub=${query.sub}`)) ?? banners[bannerSeed % banners.length];
  }, [banners, query.sub, bannerSeed]);

  const applied = data?.applied;
  const title = useMemo(() => {
    if (place) return `${placeWhat} in ${external.data?.placeName && external.data.origin === 'place' ? external.data.placeName : place}`;
    const what = applied?.subName ?? applied?.categoryName ?? (applied?.q ? `“${applied.q}”` : 'Local businesses');
    const where = [applied?.areaName, applied?.cityName].filter(Boolean).join(', ');
    return `${what}${where ? ` in ${where}` : ''}`;
  }, [applied, place, placeWhat, external.data]);
  useDocumentTitle(title);

  usePresence(data?.data.map((b) => b.id) ?? [], (e) => {
    queryClient.setQueryData<SearchResponse>(queryKey, (old) => old && {
      ...old, data: old.data.map((b) => (b.id === e.businessId ? { ...b, availabilityStatus: e.status, lastSeenOn: e.lastSeenOn } : b)),
    });
  });

  // Area first, then the rest of the city: split this page where the area's results end.
  const areaMatches = applied?.areaName ? applied.areaMatches ?? null : null;
  const items = data?.data ?? [];
  const split = areaMatches == null ? null : Math.max(0, Math.min(items.length, areaMatches - (query.page - 1) * query.pageSize));
  const groups = split == null
    ? [{ key: 'all', label: null as string | null, items }]
    : [
        { key: 'area', label: `In ${applied!.areaName}`, items: items.slice(0, split) },
        { key: 'city', label: `More in ${applied!.cityName ?? 'the city'}, nearest to ${applied!.areaName}`, items: items.slice(split) },
      ].filter((g) => g.items.length > 0);
  const areaNotice = areaMatches === 0 && items.length > 0 && query.page === 1
    ? `No registered businesses in ${applied!.areaName} yet. Showing the nearest in ${applied!.cityName ?? 'the city'}.`
    : null;

  const activeChips = place ? [
    placeWhat && (query.sub || query.category) && { key: query.sub ? 'sub' : 'category', label: placeWhat },
    { key: 'place', label: place },
  ].filter(Boolean) as { key: string; label: string }[] : [
    applied?.subName && { key: 'sub', label: applied.subName },
    !applied?.subName && applied?.categoryName && { key: 'category', label: applied.categoryName },
    applied?.cityName && { key: 'city', label: applied.cityName },
    applied?.areaName && { key: 'area', label: applied.areaName },
    query.minRating && { key: 'minRating', label: `${query.minRating}★ & above` },
    query.availability && { key: 'availability', label: query.availability === 'now' ? 'Available now' : 'Availability' },
    ...flags.filter((f) => params.get(f.key) === 'true').map((f) => ({ key: f.key, label: f.label })),
    query.plan && { key: 'plan', label: `${query.plan} plan` },
  ].filter(Boolean) as { key: string; label: string }[];

  const filters = <Filters params={params} update={update} />;
  const filtersNote = tab !== 'db' && (
    <p className="mb-3 flex items-start gap-1.5 rounded-lg bg-subtle px-3 py-2 text-xs text-muted">
      <InfoOutlined sx={{ fontSize: 14, mt: '1px' }} />Category and location apply to every tab; the other filters apply to Calling Bell businesses.
    </p>
  );

  return (
    <div className="container-page py-8">
      <PageHeader title={title} crumbs={[{ label: 'Home', to: '/' }, { label: 'Search' }]}
        subtitle={place ? 'Google Maps businesses and AI recommendations · not a Calling Bell city yet'
          : data ? `${number(data.pagination.totalCount)} ${data.pagination.totalCount === 1 ? 'business' : 'businesses'} found` : 'Searching…'} />

      <div className="grid gap-6 lg:grid-cols-[264px_1fr]">
        <aside className="hidden lg:block">
          <div className="card sticky top-20 max-h-[calc(100vh-6rem)] overflow-y-auto p-4">{filtersNote}{filters}</div>
        </aside>

        <div className="min-w-0">
          <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center">
            <form role="search" className="relative flex-1" onSubmit={(e) => { e.preventDefault(); submitText(); }}>
              <SearchRounded sx={{ position: 'absolute', left: 10, top: 9, fontSize: 20, color: 'var(--cb-faint)', zIndex: 1, pointerEvents: 'none' }} />
              <SearchSuggest value={text} onChange={setText} citySlug={query.city} onSelect={applySuggestion}
                placeholder={tickerNames.length ? '' : 'Search a service and place, e.g. lawyers in Madhapur'} ariaLabel="Search"
                inputClassName="h-[40px] w-full rounded-lg border border-line bg-surface pl-9 pr-9 text-sm outline-none focus:border-line-strong">
                {!text && <PlaceholderTicker names={tickerNames} prefix="e.g." className="pl-9 pr-9 text-sm" />}
              </SearchSuggest>
              {parsing && <CircularProgress size={16} sx={{ position: 'absolute', right: 12, top: 12 }} aria-label="Finding the place" />}
            </form>
            <div className="flex gap-2">
              <Button className="lg:!hidden" variant="outlined" startIcon={<TuneRounded />} onClick={() => setDrawer(true)}>Filters{activeChips.length ? ` (${activeChips.length})` : ''}</Button>
              {/* Sorting applies to Calling Bell results; Google Maps and AI results are listed nearest first. */}
              {tab === 'db' && (
                <TextField select size="small" value={query.sort} onChange={(e) => update({ sort: e.target.value === 'relevance' ? null : e.target.value })}
                  sx={{ minWidth: 170 }} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Sort by' } }}>
                  {sorts.map((s) => <MenuItem key={s.value} value={s.value}>{s.label}</MenuItem>)}
                </TextField>
              )}
            </div>
          </div>

          {activeChips.length > 0 && (
            <div className="mb-4 flex flex-wrap items-center gap-2">
              {activeChips.map((c) => <Chip key={c.key} label={c.label} onDelete={() => update({ [c.key]: null, ...(c.key === 'city' ? { area: null } : {}) })} variant="outlined" sx={{ bgcolor: 'background.paper' }} />)}
              <Button size="small" onClick={clearAll}>Clear all</Button>
            </div>
          )}

          {searchesExternal && (
            <SourceTabs value={tab} onChange={setTab} dbCount={place ? 0 : data?.pagination.totalCount} dbLoading={!place && (isLoading || parsePending)}
              ext={external.data} loading={external.isLoading} />
          )}

          {tab !== 'db' ? (
            <div role="tabpanel" id={sourcePanelId(tab)} aria-labelledby={sourceTabId(tab)}>
              <ExternalResults only={tab} ext={external.data} loading={external.isLoading} isError={external.isError} />
            </div>
          ) : (
          <div role={searchesExternal ? 'tabpanel' : undefined} id={sourcePanelId('db')} aria-labelledby={searchesExternal ? sourceTabId('db') : undefined}>
          {banner && query.page === 1 && (
            <Link to={banner.linkUrl ?? '/search'} className="card mb-4 flex items-center gap-4 overflow-hidden p-0 hover:border-line-strong">
              <Img src={banner.mobileImageUrl ?? banner.imageUrl} alt={banner.altText ?? ''} className="h-20 w-20 shrink-0 sm:h-24 sm:w-24" rounded="rounded-none" />
              <div className="min-w-0 py-3 pr-4">
                <span className="text-[11px] font-semibold uppercase tracking-wide text-muted">Promoted</span>
                <div className="truncate font-semibold">{banner.title}</div>
                <div className="line-clamp-2 text-sm text-muted">{banner.subtitle}</div>
              </div>
            </Link>
          )}

          {place ? (
            <UnlistedPlaceNotice place={external.data?.origin === 'place' && external.data.placeName ? external.data.placeName : place}
              notFound={external.data?.origin === 'place-not-found'} onUseDropdown={() => update({ place: null })} />
          ) : isError ? <div className="card"><ErrorState onRetry={() => refetch()} /></div> : (
            <div className={`transition-opacity ${isFetching && !isLoading ? 'opacity-60' : ''}`} aria-busy={isFetching}>
              {isLoading || parsePending || !data ? (
                <div className="space-y-3">{Array.from({ length: 6 }, (_, i) => <BusinessCardSkeleton key={i} layout="row" />)}</div>
              ) : data!.data.length === 0 && searchesExternal ? (
                <div role="status" className="card flex flex-col gap-3 border-accent/40 bg-accent-soft/40 p-4 text-sm sm:flex-row sm:items-start">
                  <InfoOutlined fontSize="small" className="mt-0.5 shrink-0 text-accent-ink" />
                  <div className="min-w-0 flex-1">
                    <p className="font-medium text-ink">No registered businesses found in our database for {applied?.areaName ?? applied?.cityName ?? 'this area'}.</p>
                    <p className="mt-0.5 text-muted">See what Google Maps and our AI assistant found nearby. Filters such as rating and availability apply to registered businesses only.</p>
                  </div>
                  <div className="flex shrink-0 flex-wrap gap-2">
                    <Button size="small" variant="contained" onClick={() => setTab('google')}>Google Maps</Button>
                    <Button size="small" variant="outlined" onClick={() => setTab('ai')}>AI recommended</Button>
                  </div>
                </div>
              ) : data!.data.length === 0 ? (
                <div className="card"><EmptyState title="No businesses match these filters" message="Try removing a filter, searching a nearby city or browsing all categories."
                  action={<Button variant="contained" onClick={clearAll}>Reset filters</Button>} /></div>
              ) : (
                <div className="space-y-6">
                  {searchesExternal && (
                    <p className="flex items-center gap-1.5 text-sm text-muted">
                      <VerifiedRounded sx={{ fontSize: 16 }} className="text-success" />
                      Verified businesses registered on Calling Bell: enquire, book and review them here.
                    </p>
                  )}
                  {areaNotice && (
                    <p role="status" className="flex items-start gap-2 rounded-xl border border-line bg-surface px-4 py-3 text-sm text-muted">
                      <InfoOutlined fontSize="small" className="mt-0.5 shrink-0 text-accent-ink" />{areaNotice}
                    </p>
                  )}
                  {groups.map((g) => (
                    <section key={g.key} aria-label={g.label ?? 'Results'}>
                      {g.label && <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-muted">{g.label}</h2>}
                      {/* One business per row. */}
                      <ul className="space-y-3">{g.items.map((b) => <li key={b.id}><BusinessCard b={b} layout="row" /></li>)}</ul>
                    </section>
                  ))}
                </div>
              )}
            </div>
          )}

          {data && data.pagination.totalPages > 1 && (
            <div className="mt-8 flex justify-center">
              <Pagination count={data.pagination.totalPages} page={query.page} shape="rounded"
                onChange={(_, p) => { update({ page: p === 1 ? null : String(p) }, false); window.scrollTo({ top: 0, behavior: 'smooth' }); }} />
            </div>
          )}
          </div>
          )}
        </div>
      </div>

      <Drawer anchor="bottom" open={drawer} onClose={() => setDrawer(false)} slotProps={{ paper: { sx: { maxHeight: '85vh', borderTopLeftRadius: 16, borderTopRightRadius: 16 } } }}>
        <div className="overflow-y-auto p-5">{filtersNote}{filters}</div>
        <div className="border-t border-line p-4"><Button fullWidth variant="contained" size="large" onClick={() => setDrawer(false)}>Show {data ? number(data.pagination.totalCount) : ''} results</Button></div>
      </Drawer>
    </div>
  );
}

/** Shown instead of registered businesses when the search names a place that isn't a Calling Bell city or area ("lawyers in Nellore"). */
function UnlistedPlaceNotice({ place, notFound, onUseDropdown }: { place: string; notFound: boolean; onUseDropdown: () => void }) {
  const selected = useCity((s) => s.citySlug);
  return (
    <div role="status" className="card flex flex-col gap-3 border-accent/40 bg-accent-soft/40 p-4 text-sm sm:flex-row sm:items-start">
      <span aria-hidden className="mt-0.5 shrink-0 text-accent-ink">{notFound ? <LocationOffRounded fontSize="small" /> : <InfoOutlined fontSize="small" />}</span>
      <div className="min-w-0 flex-1">
        <p className="font-medium text-ink">
          {notFound
            ? `We couldn’t find a place called “${place}”.`
            : `${place} isn’t a Calling Bell city yet, so there are no registered businesses there.`}
        </p>
        <p className="mt-0.5 text-muted">
          {notFound
            ? 'Check the spelling, or pick a city or area from the location menu.'
            : `Showing Google Maps businesses and AI recommendations in ${place} below.`}
        </p>
      </div>
      <Button size="small" variant="outlined" onClick={onUseDropdown} sx={{ flexShrink: 0, alignSelf: { xs: 'flex-start', sm: 'center' } }}>
        {selected ? 'Search my selected location' : 'Choose a listed city'}
      </Button>
    </div>
  );
}

function Filters({ params, update }: { params: URLSearchParams; update: (p: Record<string, string | null>) => void }) {
  const { data: categories } = useCategories();
  const { data: cities } = useCities();
  const { data: plans } = useQuery({ queryKey: ['plans'], queryFn: () => api.get<Plan[]>('/api/plans'), staleTime: 600_000 });
  const availability = useLookup('AvailabilityStatus').filter((a) => a.code !== 'Offline' && a.code !== 'Busy');

  const categorySlug = params.get('category') ?? categories?.find((c) => c.subCategories.some((s) => s.slug === params.get('sub')))?.slug ?? '';
  const city = cities?.find((c) => c.slug === params.get('city'));
  const typedPlace = params.get('place')?.trim() || null;
  // With no city chosen, offer the areas of the visitor's IP-detected district.
  const { data: district } = useVisitorDistrict();
  const areaCity = city ?? cities?.find((c) => c.slug === district?.citySlug);
  // Every area of the city (the area-discovery agent fills cities that have none yet).
  const areaList = useCityAreas(areaCity?.slug);
  const areaParam = (params.get('area') ?? '').toLowerCase();

  return (
    <div className="space-y-5">
      <FilterGroup title="Category">
        <TextField select value={categories ? categorySlug : ''} onChange={(e) => update({ category: e.target.value || null, sub: null })} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Category' } }}>
          <MenuItem value="">All categories</MenuItem>
          {categories?.map((c) => <MenuItem key={c.slug} value={c.slug}>{c.name} ({c.businessCount})</MenuItem>)}
        </TextField>
        {categorySlug && (
          <TextField select value={categories ? params.get('sub') ?? '' : ''} onChange={(e) => update({ sub: e.target.value || null })} sx={{ mt: 1 }} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Sub-category' } }}>
            <MenuItem value="">All in category</MenuItem>
            {categories?.find((c) => c.slug === categorySlug)?.subCategories.map((s) => <MenuItem key={s.slug} value={s.slug}>{s.name} ({s.businessCount})</MenuItem>)}
          </TextField>
        )}
      </FilterGroup>

      <FilterGroup title="Location">
        {/* A place typed in the search that isn't a listed city ("in Nellore"): shown here; picking a listed city replaces it. */}
        {typedPlace && (
          <div className="mb-2 flex items-center gap-1.5 rounded-lg border border-line bg-subtle px-3 py-2 text-sm">
            <PlaceRounded sx={{ fontSize: 16 }} className="text-muted" />
            <span className="min-w-0 flex-1 truncate font-medium">{typedPlace}</span>
            <span className="shrink-0 text-xs text-muted">from your search</span>
          </div>
        )}
        <CityAutocomplete value={typedPlace ? null : params.get('city')} onChange={(slug) => update({ city: slug, area: null })}
          placeholder={typedPlace ? 'Choose a listed city' : 'All cities'} ariaLabel="City" />
        {areaCity && !typedPlace && (
          <TextField select value={city && areaList.areas.some((a) => a.id === areaParam) ? areaParam : ''}
            sx={{ mt: 1, minWidth: 0, '& .MuiSelect-select': { overflow: 'hidden', textOverflow: 'ellipsis' } }}
            onChange={(e) => update(city ? { area: e.target.value || null } : { city: areaCity.slug, area: e.target.value || null })}
            slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Area' } }}
            helperText={areaList.discovering ? `Finding areas in ${areaCity.name}…` : undefined}>
            <MenuItem value="">{city ? `All areas in ${city.name}` : `Areas near you · ${areaCity.name}`}</MenuItem>
            {areaList.areas.map((a) => <MenuItem key={a.id} value={a.id}>{a.name} · {a.pincode}</MenuItem>)}
          </TextField>
        )}
      </FilterGroup>

      <FilterGroup title="Availability">
        <TextField select value={availability.length ? params.get('availability') ?? '' : ''} onChange={(e) => update({ availability: e.target.value || null })} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Availability' } }}>
          <MenuItem value="">Any</MenuItem>
          <MenuItem value="now">Available now (any mode)</MenuItem>
          {availability.map((a) => <MenuItem key={a.code} value={a.code}>{a.name}</MenuItem>)}
        </TextField>
      </FilterGroup>

      <FilterGroup title="Rating">
        <RadioGroup value={params.get('minRating') ?? ''} onChange={(e) => update({ minRating: e.target.value || null })}>
          {[['', 'Any rating'], ['4.5', '4.5★ & above'], ['4', '4★ & above'], ['3.5', '3.5★ & above']].map(([v, l]) => (
            <FormControlLabel key={v} value={v} control={<Radio size="small" />} label={<span className="text-sm">{l}</span>} />
          ))}
        </RadioGroup>
      </FilterGroup>

      <FilterGroup title="Options">
        <div className="flex flex-col">
          {flags.map((f) => (
            <FormControlLabel key={f.key} label={<span className="text-sm">{f.label}</span>}
              control={<Checkbox size="small" checked={params.get(f.key) === 'true'} onChange={(e) => update({ [f.key]: e.target.checked ? 'true' : null })} />} />
          ))}
        </div>
      </FilterGroup>

      <FilterGroup title="Subscription plan">
        <TextField select value={plans ? params.get('plan') ?? '' : ''} onChange={(e) => update({ plan: e.target.value || null })} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Subscription plan' } }}>
          <MenuItem value="">Any plan</MenuItem>
          {plans?.map((p) => <MenuItem key={p.code} value={p.code}>{p.name}</MenuItem>)}
        </TextField>
      </FilterGroup>
    </div>
  );
}

function FilterGroup({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <fieldset className="min-w-0">
      <legend className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">{title}</legend>
      {children}
    </fieldset>
  );
}
