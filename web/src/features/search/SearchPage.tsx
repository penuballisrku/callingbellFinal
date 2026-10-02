import { useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Checkbox, Chip, Drawer, FormControlLabel, MenuItem, Pagination, Radio, RadioGroup, TextField, ToggleButton, ToggleButtonGroup,
} from '@mui/material';
import TuneRounded from '@mui/icons-material/TuneRounded';
import SearchRounded from '@mui/icons-material/SearchRounded';
import ViewListRounded from '@mui/icons-material/ViewListRounded';
import GridViewRounded from '@mui/icons-material/GridViewRounded';
import { api } from '@/lib/api';
import { useCities, useDebounced, useDocumentTitle, useLookup } from '@/lib/hooks';
import { usePresence } from '@/lib/realtime';
import { number } from '@/lib/format';
import type { AppliedFilters, Banner, BusinessCard as Card, Category, Pagination as Meta, Plan } from '@/lib/types';
import { BusinessCard, BusinessCardSkeleton } from '@/components/BusinessCard';
import { EmptyState, ErrorState, Img, PageHeader } from '@/components/ui';

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
  const [text, setText] = useState(params.get('q') ?? '');
  const debounced = useDebounced(text);
  const [drawer, setDrawer] = useState(false);
  const [layout, setLayout] = useState<'row' | 'grid'>('row');
  const queryClient = useQueryClient();

  // Keep debounced text in the URL (search debounce).
  useEffect(() => {
    if ((params.get('q') ?? '') === debounced) return;
    update({ q: debounced || null });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debounced]);

  const update = (patch: Record<string, string | null>, resetPage = true) => {
    const next = new URLSearchParams(params);
    Object.entries(patch).forEach(([k, v]) => (v === null || v === '' ? next.delete(k) : next.set(k, v)));
    if (resetPage) next.delete('page');
    setParams(next, { replace: true });
  };

  const query = {
    q: params.get('q'), category: params.get('category'), sub: params.get('sub'), city: params.get('city'), areaId: params.get('area'),
    minRating: params.get('minRating'), availability: params.get('availability'), openNow: params.get('openNow') === 'true',
    verifiedOnly: params.get('verifiedOnly') === 'true', homeService: params.get('homeService') === 'true',
    videoConsultation: params.get('video') === 'true', onlineBooking: params.get('onlineBooking') === 'true',
    plan: params.get('plan'), sort: params.get('sort') ?? 'relevance', page: Number(params.get('page') ?? 1), pageSize: 12,
  };
  const queryKey = ['search', query];

  const { data, isLoading, isFetching, isError, refetch } = useQuery({
    queryKey,
    queryFn: async () => {
      const env = await api.envelope<Card[]>('/api/businesses', query);
      return env as unknown as SearchResponse;
    },
    placeholderData: keepPreviousData,
  });
  const { data: banners } = useQuery({ queryKey: ['banners', 'SearchTop'], queryFn: () => api.get<Banner[]>('/api/banners', { placement: 'SearchTop' }), staleTime: 600_000 });

  const applied = data?.applied;
  const title = useMemo(() => {
    const what = applied?.subName ?? applied?.categoryName ?? (applied?.q ? `“${applied.q}”` : 'Local businesses');
    return `${what}${applied?.cityName ? ` in ${applied.cityName}` : ''}`;
  }, [applied]);
  useDocumentTitle(title);

  usePresence(data?.data.map((b) => b.id) ?? [], (e) => {
    queryClient.setQueryData<SearchResponse>(queryKey, (old) => old && {
      ...old, data: old.data.map((b) => (b.id === e.businessId ? { ...b, availabilityStatus: e.status, lastSeenOn: e.lastSeenOn } : b)),
    });
  });

  const activeChips = [
    applied?.subName && { key: 'sub', label: applied.subName },
    !applied?.subName && applied?.categoryName && { key: 'category', label: applied.categoryName },
    applied?.cityName && { key: 'city', label: applied.cityName },
    query.minRating && { key: 'minRating', label: `${query.minRating}★ & above` },
    query.availability && { key: 'availability', label: query.availability === 'now' ? 'Available now' : 'Availability' },
    ...flags.filter((f) => params.get(f.key) === 'true').map((f) => ({ key: f.key, label: f.label })),
    query.plan && { key: 'plan', label: `${query.plan} plan` },
  ].filter(Boolean) as { key: string; label: string }[];

  const filters = <Filters params={params} update={update} />;

  return (
    <div className="container-page py-8">
      <PageHeader title={title} crumbs={[{ label: 'Home', to: '/' }, { label: 'Search' }]}
        subtitle={data ? `${number(data.pagination.totalCount)} ${data.pagination.totalCount === 1 ? 'business' : 'businesses'} found` : 'Searching…'} />

      <div className="grid gap-6 lg:grid-cols-[264px_1fr]">
        <aside className="hidden lg:block">
          <div className="card sticky top-20 max-h-[calc(100vh-6rem)] overflow-y-auto p-4">{filters}</div>
        </aside>

        <div className="min-w-0">
          <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center">
            <div className="relative flex-1">
              <SearchRounded sx={{ position: 'absolute', left: 10, top: 9, fontSize: 20, color: 'var(--cb-faint)' }} />
              <input value={text} onChange={(e) => setText(e.target.value)} placeholder="Search by name, service or locality" aria-label="Search"
                className="h-[40px] w-full rounded-lg border border-line bg-surface pl-9 pr-3 text-sm outline-none focus:border-line-strong" />
            </div>
            <div className="flex gap-2">
              <Button className="lg:!hidden" variant="outlined" startIcon={<TuneRounded />} onClick={() => setDrawer(true)}>Filters{activeChips.length ? ` (${activeChips.length})` : ''}</Button>
              <TextField select size="small" value={query.sort} onChange={(e) => update({ sort: e.target.value === 'relevance' ? null : e.target.value })}
                sx={{ minWidth: 170 }} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Sort by' } }}>
                {sorts.map((s) => <MenuItem key={s.value} value={s.value}>{s.label}</MenuItem>)}
              </TextField>
              <ToggleButtonGroup size="small" exclusive value={layout} onChange={(_, v) => v && setLayout(v)} className="hidden sm:flex" aria-label="Layout">
                <ToggleButton value="row" aria-label="List view"><ViewListRounded fontSize="small" /></ToggleButton>
                <ToggleButton value="grid" aria-label="Grid view"><GridViewRounded fontSize="small" /></ToggleButton>
              </ToggleButtonGroup>
            </div>
          </div>

          {activeChips.length > 0 && (
            <div className="mb-4 flex flex-wrap items-center gap-2">
              {activeChips.map((c) => <Chip key={c.key} label={c.label} onDelete={() => update({ [c.key]: null, ...(c.key === 'city' ? { area: null } : {}) })} variant="outlined" sx={{ bgcolor: '#fff' }} />)}
              <Button size="small" onClick={() => { setText(''); setParams(new URLSearchParams(), { replace: true }); }}>Clear all</Button>
            </div>
          )}

          {banners?.[0] && query.page === 1 && (
            <Link to={banners[0].linkUrl ?? '/search'} className="card mb-4 flex items-center gap-4 overflow-hidden p-0 hover:border-line-strong">
              <Img src={banners[0].mobileImageUrl ?? banners[0].imageUrl} alt={banners[0].altText ?? ''} className="h-20 w-20 shrink-0 sm:h-24 sm:w-24" rounded="rounded-none" />
              <div className="min-w-0 py-3 pr-4">
                <span className="text-[11px] font-semibold uppercase tracking-wide text-muted">Promoted</span>
                <div className="truncate font-semibold">{banners[0].title}</div>
                <div className="line-clamp-2 text-sm text-muted">{banners[0].subtitle}</div>
              </div>
            </Link>
          )}

          {isError ? <div className="card"><ErrorState onRetry={() => refetch()} /></div> : (
            <div className={`transition-opacity ${isFetching && !isLoading ? 'opacity-60' : ''}`} aria-busy={isFetching}>
              {isLoading ? (
                <div className="space-y-3">{Array.from({ length: 6 }, (_, i) => <BusinessCardSkeleton key={i} layout="row" />)}</div>
              ) : data!.data.length === 0 ? (
                <div className="card"><EmptyState title="No businesses match these filters" message="Try removing a filter, searching a nearby city or browsing all categories."
                  action={<Button variant="contained" onClick={() => { setText(''); setParams(new URLSearchParams(), { replace: true }); }}>Reset filters</Button>} /></div>
              ) : layout === 'row' ? (
                <div className="space-y-3">{data!.data.map((b) => <BusinessCard key={b.id} b={b} layout="row" />)}</div>
              ) : (
                <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">{data!.data.map((b) => <BusinessCard key={b.id} b={b} />)}</div>
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
      </div>

      <Drawer anchor="bottom" open={drawer} onClose={() => setDrawer(false)} slotProps={{ paper: { sx: { maxHeight: '85vh', borderTopLeftRadius: 16, borderTopRightRadius: 16 } } }}>
        <div className="overflow-y-auto p-5">{filters}</div>
        <div className="border-t border-line p-4"><Button fullWidth variant="contained" size="large" onClick={() => setDrawer(false)}>Show {data ? number(data.pagination.totalCount) : ''} results</Button></div>
      </Drawer>
    </div>
  );
}

function Filters({ params, update }: { params: URLSearchParams; update: (p: Record<string, string | null>) => void }) {
  const { data: categories } = useQuery({ queryKey: ['categories'], queryFn: () => api.get<Category[]>('/api/categories'), staleTime: 600_000 });
  const { data: cities } = useCities();
  const { data: plans } = useQuery({ queryKey: ['plans'], queryFn: () => api.get<Plan[]>('/api/plans'), staleTime: 600_000 });
  const availability = useLookup('AvailabilityStatus').filter((a) => a.code !== 'Offline' && a.code !== 'Busy');

  const categorySlug = params.get('category') ?? categories?.find((c) => c.subCategories.some((s) => s.slug === params.get('sub')))?.slug ?? '';
  const city = cities?.find((c) => c.slug === params.get('city'));

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
        <TextField select value={cities ? params.get('city') ?? '' : ''} onChange={(e) => update({ city: e.target.value || null, area: null })} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'City' } }}>
          <MenuItem value="">All cities</MenuItem>
          {cities?.map((c) => <MenuItem key={c.slug} value={c.slug}>{c.name}</MenuItem>)}
        </TextField>
        {city && (
          <TextField select value={params.get('area') ?? ''} onChange={(e) => update({ area: e.target.value || null })} sx={{ mt: 1 }} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Area' } }}>
            <MenuItem value="">All areas in {city.name}</MenuItem>
            {city.areas.map((a) => <MenuItem key={a.id} value={a.id}>{a.name} · {a.pincode}</MenuItem>)}
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
    <fieldset>
      <legend className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">{title}</legend>
      {children}
    </fieldset>
  );
}
