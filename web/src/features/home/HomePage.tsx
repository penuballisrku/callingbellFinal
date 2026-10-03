import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Button, IconButton, Skeleton, useMediaQuery } from '@mui/material';
import SearchRounded from '@mui/icons-material/SearchRounded';
import ArrowForwardRounded from '@mui/icons-material/ArrowForwardRounded';
import EventAvailableRounded from '@mui/icons-material/EventAvailableRounded';
import StarRounded from '@mui/icons-material/StarRounded';
import AutoAwesomeRounded from '@mui/icons-material/AutoAwesomeRounded';
import ExpandMoreRounded from '@mui/icons-material/ExpandMoreRounded';
import ChevronLeftRounded from '@mui/icons-material/ChevronLeftRounded';
import ChevronRightRounded from '@mui/icons-material/ChevronRightRounded';
import ExpandLessRounded from '@mui/icons-material/ExpandLessRounded';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import PhoneIphoneRounded from '@mui/icons-material/PhoneIphoneRounded';
import { api } from '@/lib/api';
import { compactNumber, number, pluralize } from '@/lib/format';
import { useCategories, useCities, useDocumentTitle } from '@/lib/hooks';
import { useCity } from '@/stores/city';
import type { Banner, HomeData, MarketingPage, NearbyService, SearchSuggestion, NearbyServices, PopularService, RelatedCategory, SubCategory, TopPicks } from '@/lib/types';
import { ErrorState, Img, SectionHeader } from '@/components/ui';
import { CitySelect } from '@/layouts/CustomerLayout';
import { SearchSuggest, searchHref } from '@/components/SearchSuggest';
import { PlaceholderTicker } from '@/components/PlaceholderTicker';
import { ReviewsSection } from './ReviewsSection';

/** Orders featured sub-categories round-robin by parent category, so every category is represented before any repeats. */
function interleaveByCategory(subs: SubCategory[]): SubCategory[] {
  const groups = new Map<string, SubCategory[]>();
  subs.forEach((s) => groups.set(s.categorySlug, [...(groups.get(s.categorySlug) ?? []), s]));
  const out: SubCategory[] = [];
  for (let round = 0; out.length < subs.length; round++) groups.forEach((g) => { if (g[round]) out.push(g[round]); });
  return out;
}

export default function HomePage() {
  useDocumentTitle();
  const citySlug = useCity((s) => s.citySlug);
  const queryKey = ['home', citySlug];
  const { data, isLoading, isError, refetch } = useQuery({ queryKey, queryFn: () => api.get<HomeData>('/api/home', { city: citySlug }) });
  const { data: categoryTree } = useCategories();
  const popularSubs = useMemo(() => interleaveByCategory(data?.categories ?? []).slice(0, 18), [data]);

  if (isError) return <div className="container-page py-16"><ErrorState onRetry={() => refetch()} /></div>;

  return (
    <>
      <Hero data={data} />

      <div className="container-page space-y-14 py-12 md:space-y-16">
        <section aria-labelledby="cat-h">
          <SectionHeader title="Popular categories"
            subtitle={categoryTree ? `Trusted professionals across ${number(categoryTree.reduce((n, c) => n + c.subCategories.length, 0))} services in ${categoryTree.length} categories` : 'Trusted professionals for every local need'}
            action={<Link to="/categories" className="hidden items-center gap-1 text-sm font-semibold sm:inline-flex">All categories <ArrowForwardRounded sx={{ fontSize: 18 }} /></Link>} />
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6">
            {isLoading ? Array.from({ length: 12 }, (_, i) => <Skeleton key={i} variant="rounded" height={168} />) : popularSubs.map((c, i) => (
              // 12 cards (whole rows at 2/3/4 columns) below lg, 18 (three rows of six) on desktop.
              <Link key={c.id} to={`/search?sub=${c.slug}`} className={`group card overflow-hidden transition-colors hover:border-line-strong ${i >= 12 ? 'hidden lg:block' : ''}`}>
                <Img src={c.imageUrl} alt={c.altText ?? c.name} aspect="16/10" rounded="rounded-none" className="w-full" fallbackText={c.name} />
                <div className="flex items-center gap-2.5 p-3">
                  <Img src={c.iconUrl} alt="" className="h-8 w-8 shrink-0 p-1" rounded="rounded-lg" fit="contain" fallbackText={c.name} />
                  <div className="min-w-0">
                    <div className="truncate text-sm font-semibold group-hover:underline">{c.name}</div>
                    <div className="text-xs text-muted">{c.businessCount} {c.businessCount === 1 ? 'business' : 'businesses'}</div>
                  </div>
                </div>
              </Link>
            ))}
          </div>
          {categoryTree && (
            <nav aria-label="Browse by category" className="mt-5 flex flex-wrap gap-2">
              {categoryTree.map((c) => (
                <Link key={c.id} to={`/categories/${c.slug}`}
                  className="inline-flex items-center gap-1.5 rounded-full border border-line bg-surface py-1 pl-1.5 pr-3 text-[13px] font-medium text-ink-2 transition-colors hover:border-line-strong hover:text-ink">
                  <Img src={c.iconUrl} alt="" className="h-5 w-5 p-0.5" rounded="rounded-full" fit="contain" fallbackText={c.name} />
                  {c.name}
                </Link>
              ))}
              <Link to="/categories" className="inline-flex items-center gap-1 rounded-full px-3 py-1 text-[13px] font-semibold text-ink-2 hover:text-accent-ink">
                All categories <ArrowForwardRounded sx={{ fontSize: 16 }} />
              </Link>
            </nav>
          )}
        </section>

        <ServicesSection items={data?.popularServices} loading={isLoading} />

        <NearbyServicesSection />

        <TopPicksSection />

        {!!data?.promoBanners.length && <PromoCarousel banners={data.promoBanners} />}

        <ReviewsSection />


        <AppDownload />
      </div>
    </>
  );
}

function Hero({ data }: { data?: HomeData }) {
  const navigate = useNavigate();
  const citySlug = useCity((s) => s.citySlug);
  const areaId = useCity((s) => s.areaId);
  const [q, setQ] = useState('');
  // Category / sub-category / service picked from the suggestions; searched (with the chosen location) when Search is pressed.
  const [picked, setPicked] = useState<SearchSuggestion | null>(null);
  const [index, setIndex] = useState(0);
  const banners = data?.heroBanners ?? [];
  useEffect(() => {
    if (banners.length < 2) return;
    const t = setInterval(() => setIndex((i) => (i + 1) % banners.length), 7000);
    return () => clearInterval(t);
  }, [banners.length]);
  const banner = banners[index % Math.max(1, banners.length)];
  const categoryNames = useMemo(() => data?.categories.map((c) => c.name) ?? [], [data]);
  // Quick-search chips are page content from the database (MarketingContent, PageKey 'Home', SectionKey 'QuickSearch').
  const { data: homeContent } = useQuery({ queryKey: ['content', 'Home'], queryFn: () => api.get<MarketingPage>('/api/content/pages/Home'), staleTime: 600_000 });
  const quickSearches = useMemo(() => homeContent?.blocks.filter((b) => b.section === 'QuickSearch' && b.linkUrl) ?? [], [homeContent]);

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    navigate(searchHref(q, picked, citySlug, areaId));
  };
  const pick = (s: SearchSuggestion) => {
    if (s.kind === 'Business') { navigate(`/b/${s.slug}`); return; }
    setQ(s.label);
    setPicked(s);
  };

  return (
    <section className="bg-navy text-white">
      <div className="container-page grid gap-10 py-12 md:py-16 lg:grid-cols-[1.15fr_1fr] lg:items-center">
        <div>
          <p className="text-sm font-semibold uppercase tracking-[0.12em] text-accent">Discover. Connect. Book. Grow.</p>
          <h1 className="mt-3 text-3xl font-bold leading-[1.15] tracking-tight sm:text-4xl lg:text-5xl">Find trusted local pros who are available right now.</h1>
          <p className="mt-4 max-w-xl text-base text-on-navy-muted md:text-lg">Compare verified businesses, see live availability, request quotes and book in minutes.</p>

          <form onSubmit={submit} className="mt-7 flex flex-col gap-2 rounded-xl bg-surface p-2 sm:flex-row sm:items-center" role="search">
            <div className="flex min-w-0 flex-1 items-center gap-2 px-2">
              <SearchRounded sx={{ color: 'var(--cb-faint)', flexShrink: 0 }} />
              <SearchSuggest value={q} onChange={setQ} citySlug={citySlug} onSelect={pick} ariaLabel="What are you looking for?"
                placeholder={categoryNames.length ? '' : 'Search for services, businesses…'}
                className="min-w-0 flex-1" panelClassName="-ml-9 mt-3 sm:min-w-[460px]"
                inputClassName="h-12 w-full truncate bg-transparent text-[15px] text-ink outline-none placeholder:text-faint">
                {!q && categoryNames.length > 0 && <PlaceholderTicker names={categoryNames} />}
              </SearchSuggest>
            </div>
            <div className="min-w-0 sm:w-56 sm:shrink-0"><CitySelect size="medium" fullWidth height={48} /></div>
            <Button type="submit" variant="contained" color="secondary" size="large" sx={{ height: 48, px: 3, flexShrink: 0 }}>Search</Button>
          </form>

          <div className="mt-4 flex min-h-[34px] flex-wrap gap-2">
            {quickSearches.map((s) => (
              <Link key={s.code} to={`${s.linkUrl}${citySlug ? `&city=${citySlug}` : ''}`}
                className="rounded-full border border-white/15 px-3 py-1.5 text-[13px] text-on-navy transition-colors hover:border-accent hover:text-white">{s.title}</Link>
            ))}
          </div>

          <dl className="mt-8 grid max-w-lg grid-cols-3 gap-4">
            {[
              ['Businesses', data?.stats.businesses], ['Reviews', data?.stats.reviews], ['Bookings completed', data?.stats.bookingsCompleted],
            ].map(([label, value]) => (
              <div key={label as string}>
                <dt className="text-xs text-on-navy-muted">{label}</dt>
                <dd className="mt-0.5 text-2xl font-bold">{value === undefined ? <Skeleton width={56} sx={{ bgcolor: 'rgba(255,255,255,.12)' }} /> : compactNumber(value as number)}</dd>
              </div>
            ))}
          </dl>
        </div>

        <div className="relative hidden overflow-hidden rounded-2xl border border-white/10 lg:block" style={{ aspectRatio: '4/3' }}>
          {banner ? (
            <Link to={banner.linkUrl ?? '/search'} className="group absolute inset-0 block">
              <Img src={banner.mobileImageUrl ?? banner.imageUrl} alt={banner.altText ?? banner.title} className="absolute inset-0 h-full w-full" rounded="rounded-none" eager />
              <div className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-[#0B1220] via-[#0B1220]/80 to-transparent p-6 pt-16">
                <h2 className="text-xl font-bold">{banner.title}</h2>
                {banner.subtitle && <p className="mt-1 text-sm text-on-navy-muted">{banner.subtitle}</p>}
                {banner.ctaText && <span className="mt-3 inline-flex items-center gap-1 text-sm font-semibold text-accent group-hover:underline">{banner.ctaText} <ArrowForwardRounded sx={{ fontSize: 18 }} /></span>}
              </div>
            </Link>
          ) : <Skeleton variant="rectangular" sx={{ position: 'absolute', inset: 0, height: '100%', bgcolor: 'rgba(255,255,255,.06)' }} />}
          {banners.length > 1 && (
            <div className="absolute right-4 top-4 flex gap-1.5">
              {banners.map((b, i) => (
                <button key={b.id} type="button" onClick={() => setIndex(i)} aria-label={`Show banner ${i + 1}`}
                  className={`h-1.5 rounded-full transition-all ${i === index % banners.length ? 'w-6 bg-accent' : 'w-1.5 bg-white/40'}`} />
              ))}
            </div>
          )}
        </div>
      </div>
    </section>
  );
}

/** Services: category filter chips over service cards (image, category, rating, bookings, starting price); first 8 until expanded. */
function ServicesSection({ items, loading }: { items?: PopularService[]; loading: boolean }) {
  const [category, setCategory] = useState<string | null>(null);
  const [expanded, setExpanded] = useState(false);
  const categories = useMemo(() => {
    const seen = new Map<string, { slug: string; name: string; colorHex?: string | null }>();
    items?.forEach((s) => { if (!seen.has(s.categorySlug)) seen.set(s.categorySlug, { slug: s.categorySlug, name: s.categoryName, colorHex: s.colorHex }); });
    return [...seen.values()];
  }, [items]);
  const filtered = category ? (items ?? []).filter((s) => s.categorySlug === category) : items ?? [];
  const visible = category || expanded ? filtered : filtered.slice(0, 8);
  const chip = (active: boolean) =>
    `inline-flex shrink-0 items-center gap-1.5 rounded-full border px-3.5 py-1.5 text-[13px] font-medium transition-colors ${
      active ? 'border-ink bg-ink text-surface' : 'border-line bg-surface text-ink-2 hover:border-line-strong hover:text-ink'}`;

  if (!loading && !items?.length) return null;
  return (
    <section aria-labelledby="svc-h">
      <SectionHeader title="Services" subtitle="Book trusted local services at upfront prices" />
      {categories.length > 1 && (
        <div role="group" aria-label="Filter services by category" className="-mx-4 mb-6 flex gap-2 overflow-x-auto px-4 pb-1 [scrollbar-width:none] md:mx-0 md:flex-wrap md:px-0">
          <button type="button" aria-pressed={!category} onClick={() => setCategory(null)} className={chip(!category)}>All</button>
          {categories.map((c) => (
            <button key={c.slug} type="button" aria-pressed={category === c.slug} onClick={() => setCategory(category === c.slug ? null : c.slug)} className={chip(category === c.slug)}>
              <span aria-hidden className="h-2 w-2 rounded-full" style={{ background: c.colorHex ?? 'currentColor' }} />{c.name}
            </button>
          ))}
        </div>
      )}
      <div className="grid grid-cols-2 gap-3 sm:gap-4 md:grid-cols-3 lg:grid-cols-4 lg:gap-5">
        {loading
          ? Array.from({ length: 8 }, (_, i) => <Skeleton key={i} variant="rounded" height={300} sx={{ borderRadius: '18px' }} />)
          : visible.map((s) => <ServiceCard key={`${s.subCategorySlug}-${s.searchTerm}`} service={s} />)}
      </div>
      {!category && filtered.length > 8 && (
        <div className="mt-6 flex justify-center">
          <Button variant="outlined" onClick={() => setExpanded((v) => !v)} aria-expanded={expanded}>
            {expanded ? 'Show fewer services' : `View all ${filtered.length} services`}
          </Button>
        </div>
      )}
    </section>
  );
}

/**
 * Popular services near the visitor's IP location (or chosen area). The list renders straight from the database ranking;
 * a local AI model re-ranks it in the background, so poll while `aiPending` and swap in the AI order and reasons when ready.
 */
function NearbyServicesSection() {
  const citySlug = useCity((st) => st.citySlug);
  const areaId = useCity((st) => st.areaId);
  const { data: cities } = useCities();
  const areaSlug = useMemo(() => cities?.find((c) => c.slug === citySlug)?.areas.find((a) => a.id === areaId)?.slug ?? null, [cities, citySlug, areaId]);
  const { data, isLoading } = useQuery({
    queryKey: ['nearby-services', citySlug, areaSlug],
    queryFn: () => api.get<NearbyServices>('/api/geo/nearby-services', { city: citySlug, area: areaSlug }),
    refetchInterval: (q) => (q.state.data?.aiPending ? 5000 : false),
    staleTime: 5 * 60_000,
  });

  if (!isLoading && !data?.items.length) return null;
  const place = data?.placeName ?? data?.cityName;
  return (
    <section aria-labelledby="near-h">
      <SectionHeader title={place ? `Popular near ${place}` : 'Popular near you'}
        subtitle={data?.aiRanked
          ? 'Picked for your area and the season by our AI assistant, from recent local bookings'
          : 'What people around you are booking right now'}
        action={data?.aiPending ? (
          <span className="inline-flex items-center gap-1.5 rounded-full bg-accent-soft px-2.5 py-1 text-xs font-semibold text-accent-ink" role="status">
            <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-accent motion-reduce:animate-none" />Personalising with AI
          </span>
        ) : data?.aiRanked ? (
          <span className="inline-flex items-center gap-1 text-xs font-medium text-muted"><AutoAwesomeRounded sx={{ fontSize: 14 }} className="text-accent-ink" />AI picks</span>
        ) : undefined} />
      <div className="grid grid-cols-2 gap-3 sm:gap-4 md:grid-cols-3 lg:grid-cols-4 lg:gap-5">
        {isLoading
          ? Array.from({ length: 4 }, (_, i) => <Skeleton key={i} variant="rounded" height={300} sx={{ borderRadius: '18px' }} />)
          : data!.items.map((s) => <ServiceCard key={`${s.subCategorySlug}-${s.searchTerm}`} service={s} />)}
      </div>
      {!!data?.relatedCategories.length && (
        <RelatedCategories items={data.relatedCategories} place={place ?? 'you'} citySlug={data.citySlug} aiRanked={data.aiRanked} />
      )}
    </section>
  );
}

const RELATED_PREVIEW = 6;

/**
 * Every sub-category available near the visitor: the AI's related picks first (with reasons once ready), then the rest in database order.
 * The first {@link RELATED_PREVIEW} show by default; "Show all" expands to the full list.
 */
function RelatedCategories({ items, place, citySlug, aiRanked }: { items: RelatedCategory[]; place: string; citySlug?: string | null; aiRanked: boolean }) {
  const [showAll, setShowAll] = useState(false);
  const visible = showAll ? items : items.slice(0, RELATED_PREVIEW);
  const hidden = items.length - RELATED_PREVIEW;
  return (
    <div className="mt-8" role="region" aria-labelledby="related-h">
      <div className="mb-3 flex items-baseline justify-between gap-3">
        <h3 id="related-h" className="text-base font-semibold tracking-[-0.01em] md:text-lg">
          {showAll ? `All categories near ${place}` : `Related categories near ${place}`}
          <span className="ml-2 text-sm font-normal text-muted">{number(items.length)}</span>
        </h3>
        <span className="hidden text-xs text-muted sm:block">{aiRanked ? 'AI picks first, from what people nearby book together' : 'Available around you'}</span>
      </div>
      <ul id="related-list" className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {visible.map((c) => (
          <li key={c.slug}>
            <Link to={`/search?sub=${c.slug}${citySlug ? `&city=${citySlug}` : ''}`}
              className="group flex h-full items-start gap-3 rounded-2xl border border-line bg-surface p-3 shadow-[var(--cb-shadow-xs)] outline-offset-2 transition-[box-shadow,border-color] duration-200 hover:border-line-strong hover:shadow-[var(--cb-shadow-md)] focus-visible:outline-2 focus-visible:outline-accent md:p-4">
              <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl" style={{ background: `${c.colorHex ?? '#667085'}14` }}>
                <Img src={c.iconUrl} alt="" className="h-6 w-6" rounded="rounded" fit="contain" fallbackText={c.name} />
              </span>
              <span className="min-w-0 flex-1">
                <span className="flex items-center justify-between gap-2">
                  <span className="truncate text-sm font-semibold text-ink group-hover:underline">{c.name}</span>
                  <ArrowForwardRounded sx={{ fontSize: 16 }} className="shrink-0 text-faint transition-colors group-hover:text-accent-ink" />
                </span>
                <span className="mt-0.5 block truncate text-xs text-muted">
                  {c.categoryName} · {pluralize(c.businessCount, 'business', 'businesses')}
                  {c.nearestKm != null && ` · ${c.nearestKm < 1 ? 'under 1 km' : `${c.nearestKm.toFixed(1)} km`}`}
                </span>
                {c.reason && (
                  <span className="mt-1.5 flex gap-1 text-xs leading-5 text-ink-2">
                    <AutoAwesomeRounded sx={{ fontSize: 13, mt: '3px', flexShrink: 0 }} className="text-accent-ink" />
                    <span className="line-clamp-2">{c.reason}</span>
                  </span>
                )}
              </span>
            </Link>
          </li>
        ))}
      </ul>
      {hidden > 0 && (
        <div className="mt-5 flex flex-wrap items-center justify-center gap-3">
          <Button variant="outlined" onClick={() => setShowAll((v) => !v)} aria-expanded={showAll} aria-controls="related-list"
            endIcon={showAll ? <ExpandLessRounded /> : <ExpandMoreRounded />}>
            {showAll ? 'Show fewer categories' : `Show all ${number(items.length)} categories near ${place}`}
          </Button>
          <Link to="/categories" className="text-sm font-semibold text-ink-2 hover:text-accent-ink">Browse every category</Link>
        </div>
      )}
    </div>
  );
}

function ServiceCard({ service: s }: { service: PopularService | NearbyService }) {
  const near = 'reason' in s || 'nearestKm' in s ? (s as NearbyService) : null;
  const color = s.colorHex ?? '#667085';
  return (
    <Link to={`/search?sub=${s.subCategorySlug}&q=${encodeURIComponent(s.searchTerm)}`}
      className="group relative flex flex-col overflow-hidden rounded-[18px] border border-line bg-surface shadow-[var(--cb-shadow-sm)] outline-offset-2 transition-[transform,box-shadow,border-color] duration-200 ease-out hover:-translate-y-1 hover:border-line-strong hover:shadow-[var(--cb-shadow-lg)] focus-visible:outline-2 focus-visible:outline-accent motion-reduce:transition-none motion-reduce:hover:translate-y-0">
      {/* Category-coloured gradient accent along the top edge */}
      <span aria-hidden className="absolute inset-x-0 top-0 z-10 h-[3px]" style={{ background: `linear-gradient(90deg, ${color}, ${color}33)` }} />
      <div className="relative">
        <div className="overflow-hidden">
          <Img src={s.imageUrl} alt={s.altText ?? s.name} aspect="16/10" rounded="rounded-none" fallbackText={s.name}
            className="w-full transition-transform duration-300 ease-out group-hover:scale-[1.04] motion-reduce:transition-none motion-reduce:group-hover:scale-100" />
        </div>
        <span aria-hidden className="pointer-events-none absolute inset-x-0 bottom-0 h-1/2" style={{ background: `linear-gradient(to top, ${color}1f, transparent)` }} />
        <span className="absolute -bottom-5 left-3 grid h-11 w-11 place-items-center rounded-xl bg-surface shadow-[var(--cb-shadow-md)] ring-1 ring-line md:left-4">
          <Img src={s.iconUrl} alt="" className="h-7 w-7" rounded="rounded-md" fit="contain" fallbackText={s.subCategoryName} />
        </span>
        {near?.nearestKm != null && (
          <span className="absolute right-2.5 top-2.5 inline-flex items-center gap-0.5 rounded-full bg-surface/90 px-2 py-0.5 text-[11px] font-semibold text-ink-2 shadow-[var(--cb-shadow-xs)] backdrop-blur">
            <PlaceOutlined sx={{ fontSize: 13 }} />{near.nearestKm < 1 ? 'Under 1 km' : `${near.nearestKm.toFixed(1)} km`}
          </span>
        )}
      </div>

      <div className="flex flex-1 flex-col px-3 pb-3 pt-7 md:px-4 md:pb-4">
        <div className="truncate text-xs font-medium text-muted">{s.categoryName} · {s.subCategoryName}</div>
        <h3 className="mt-1 line-clamp-2 text-[15px] font-semibold leading-snug tracking-[-0.01em] text-ink md:text-base">{s.name}</h3>

        <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-ink-2">
          {s.reviewCount > 0 && (
            <span className="inline-flex items-center gap-1" aria-label={`Rated ${s.rating.toFixed(1)} out of 5 from ${s.reviewCount} reviews`}>
              <StarRounded sx={{ fontSize: 16, color: '#F4A62C' }} />
              <span className="font-semibold tabular">{s.rating.toFixed(1)}</span>
              <span className="text-muted">({compactNumber(s.reviewCount)})</span>
            </span>
          )}
          {s.bookingCount > 0 && (
            <span className="inline-flex items-center gap-1">
              <EventAvailableRounded sx={{ fontSize: 15 }} className="text-muted" />
              <span><span className="font-semibold tabular">{compactNumber(s.bookingCount)}</span> booked</span>
            </span>
          )}
        </div>
        {near?.reason && (
          <p className="mt-2 flex gap-1.5 text-xs leading-5 text-muted">
            <AutoAwesomeRounded sx={{ fontSize: 14, mt: '3px', flexShrink: 0 }} className="text-accent-ink" />
            <span className="line-clamp-2">{near.reason}</span>
          </p>
        )}
      </div>
    </Link>
  );
}

/**
 * "Top picks in {city}": categories and sub-categories for the city detected from the visitor's IP address. Database-ranked picks show
 * immediately; AI additions for that city (marked with a sparkle, with a reason) are appended once ready. The selected city is only a
 * fallback for when the IP can't be located.
 */
function TopPicksSection() {
  const citySlug = useCity((st) => st.citySlug);
  const { data: picks, isLoading } = useQuery({
    queryKey: ['top-picks', citySlug],
    queryFn: () => api.get<TopPicks>('/api/geo/top-picks', { fallbackCity: citySlug }),
    refetchInterval: (q) => (q.state.data?.aiPending ? 5000 : false),
    staleTime: 10 * 60_000,
  });

  if (!isLoading && !picks?.categories.length) return null;
  const more = (picks?.totalInCity ?? 0) - (picks?.categories.filter((c) => c.source === 'database').length ?? 0);
  return (
    <section aria-labelledby="top-picks-h">
      <SectionHeader title={picks?.cityName ? `Top picks in ${picks.cityName}` : 'Top picks'}
        subtitle={picks?.locationSource === 'ip'
          ? `Based on your IP address location${picks.placeName && picks.placeName !== picks.cityName ? ` (${picks.placeName})` : ''} · most booked here first`
          : 'Most booked categories in the selected city'}
        action={picks?.aiPending ? (
          <span role="status" className="inline-flex items-center gap-1.5 rounded-full bg-accent-soft px-2.5 py-1 text-xs font-semibold text-accent-ink">
            <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-accent motion-reduce:animate-none" />Finding more for {picks.cityName}
          </span>
        ) : (
          <Link to="/categories" className="hidden items-center gap-1 whitespace-nowrap text-sm font-semibold sm:inline-flex">All categories <ArrowForwardRounded sx={{ fontSize: 18 }} /></Link>
        )} />

      <ul className="grid grid-cols-2 gap-3 md:grid-cols-3 lg:grid-cols-4">
        {isLoading ? Array.from({ length: 8 }, (_, i) => <li key={i}><Skeleton variant="rounded" height={96} sx={{ borderRadius: '16px' }} /></li>) : picks!.categories.map((c) => (
          <li key={c.slug}>
            <Link to={`/search?sub=${c.slug}${picks!.citySlug ? `&city=${picks!.citySlug}` : ''}`}
              className="group flex h-full items-start gap-3 rounded-2xl border border-line bg-surface p-3 shadow-[var(--cb-shadow-xs)] outline-offset-2 transition-[box-shadow,border-color] duration-200 hover:border-line-strong hover:shadow-[var(--cb-shadow-md)] focus-visible:outline-2 focus-visible:outline-accent md:p-4">
              <span className="relative grid h-11 w-11 shrink-0 place-items-center rounded-xl" style={{ background: `${c.colorHex ?? '#667085'}14` }}>
                <Img src={c.iconUrl} alt="" className="h-6 w-6" rounded="rounded" fit="contain" fallbackText={c.name} />
                {c.source === 'ai' && (
                  <span title="Suggested by AI for this city" className="absolute -right-1.5 -top-1.5 grid h-5 w-5 place-items-center rounded-full bg-surface shadow-[var(--cb-shadow-sm)] ring-1 ring-line">
                    <AutoAwesomeRounded sx={{ fontSize: 12 }} className="text-accent-ink" />
                  </span>
                )}
              </span>
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-semibold text-ink group-hover:underline">{c.name}</span>
                <span className="block truncate text-xs text-muted">{c.categoryName}</span>
                <span className="mt-1 flex flex-wrap items-center gap-x-2 text-xs text-ink-2">
                  {c.businessCount > 0 && <span>{pluralize(c.businessCount, 'business', 'businesses')}</span>}
                  {c.rating > 0 && <span className="inline-flex items-center gap-0.5"><StarRounded sx={{ fontSize: 13, color: '#F4A62C' }} />{c.rating.toFixed(1)}</span>}
                </span>
                {c.reason && <span className="mt-1.5 hidden text-xs leading-5 text-muted sm:line-clamp-2">{c.reason}</span>}
              </span>
            </Link>
          </li>
        ))}
      </ul>
      {more > 0 && (
        <div className="mt-4 text-center">
          <Link to="/categories" className="text-sm font-semibold text-ink-2 hover:text-accent-ink">+{more} more categories in {picks?.cityName}</Link>
        </div>
      )}
    </section>
  );
}

/**
 * Mid-page promotions from the database (HomeMid banners). Slides are stacked in one grid cell so the carousel keeps the tallest
 * slide's height (no layout shift); auto-advances every 8s, pauses on hover or keyboard focus, and never auto-rotates for reduced motion.
 */
function PromoCarousel({ banners }: { banners: Banner[] }) {
  const [index, setIndex] = useState(0);
  const [paused, setPaused] = useState(false);
  const reduceMotion = useMediaQuery('(prefers-reduced-motion: reduce)');
  useEffect(() => {
    if (banners.length < 2 || paused || reduceMotion) return;
    const t = setInterval(() => setIndex((i) => (i + 1) % banners.length), 8000);
    return () => clearInterval(t);
  }, [banners.length, paused, reduceMotion]);
  const current = index % banners.length;
  const go = (step: number) => setIndex((i) => (i + step + banners.length) % banners.length);

  return (
    <section aria-roledescription="carousel" aria-label="Promotions"
      onMouseEnter={() => setPaused(true)} onMouseLeave={() => setPaused(false)} onFocus={() => setPaused(true)} onBlur={() => setPaused(false)}>
      <div className="grid">
        {banners.map((b, i) => (
          <div key={b.id} role="group" aria-roledescription="slide" aria-label={`${i + 1} of ${banners.length}`} aria-hidden={i !== current}
            inert={i !== current} className={`[grid-area:1/1] transition-opacity duration-500 motion-reduce:transition-none ${i === current ? 'opacity-100' : 'pointer-events-none opacity-0'}`}>
            <PromoBanner banner={b} />
          </div>
        ))}
      </div>
      {banners.length > 1 && (
        <div className="mt-3 flex items-center justify-center gap-2">
          <IconButton size="small" aria-label="Previous promotion" onClick={() => go(-1)}><ChevronLeftRounded /></IconButton>
          <div className="flex items-center gap-1.5">
            {banners.map((b, i) => (
              <button key={b.id} type="button" aria-label={`Show promotion ${i + 1}: ${b.title}`} aria-current={i === current} onClick={() => setIndex(i)}
                className={`h-2 rounded-full transition-all duration-300 ${i === current ? 'w-6 bg-accent' : 'w-2 bg-line-strong hover:bg-muted'}`} />
            ))}
          </div>
          <IconButton size="small" aria-label="Next promotion" onClick={() => go(1)}><ChevronRightRounded /></IconButton>
        </div>
      )}
    </section>
  );
}

function PromoBanner({ banner }: { banner: Banner }) {
  return (
    <Link to={banner.linkUrl ?? '/'} className="group relative block h-full overflow-hidden rounded-2xl bg-navy text-white">
      {/* Artwork sits behind the text; the text block sets the height so long copy is never clipped. */}
      <picture className="absolute inset-0">
        <source media="(max-width: 767px)" srcSet={banner.mobileImageUrl ?? banner.imageUrl} />
        <Img src={banner.desktopImageUrl ?? banner.imageUrl} alt={banner.altText ?? banner.title} className="h-full w-full" rounded="rounded-none" eager />
      </picture>
      {/* Phones: a near-solid wash so artwork never sits behind the headline. Wider screens: fade to reveal the artwork on the right. */}
      <div className="relative flex h-full min-h-[220px] flex-col justify-center bg-gradient-to-t from-[#0B1220] via-[#0B1220]/90 to-[#0B1220]/70 p-6 md:min-h-[240px] md:bg-gradient-to-r md:from-[#0B1220] md:via-[#0B1220]/85 md:to-transparent md:p-10">
        <h2 className="max-w-lg text-2xl font-bold md:text-3xl">{banner.title}</h2>
        {banner.subtitle && <p className="mt-2 max-w-lg text-sm text-on-navy-muted md:text-base">{banner.subtitle}</p>}
        {banner.ctaText && <span className="mt-5 inline-flex w-fit items-center gap-1 rounded-lg bg-accent px-4 py-2.5 text-sm font-semibold text-on-accent">{banner.ctaText} <ArrowForwardRounded sx={{ fontSize: 18 }} /></span>}
      </div>
    </Link>
  );
}

function AppDownload() {
  return (
    <section className="card grid items-center gap-8 overflow-hidden p-6 md:grid-cols-[1.4fr_1fr] md:p-10">
      <div>
        <span className="inline-flex items-center gap-1 rounded-full bg-accent-soft px-3 py-1 text-xs font-semibold text-accent-ink"><PhoneIphoneRounded sx={{ fontSize: 16 }} />Coming soon to Android & iOS</span>
        <h2 className="mt-3 text-2xl font-bold tracking-tight md:text-3xl">Calling Bell in your pocket</h2>
        <p className="mt-2 max-w-lg text-muted">Get instant alerts when your booking is confirmed, chat with businesses and see who's available nearby - wherever you are.</p>
        <div className="mt-5 flex flex-wrap gap-3">
          {['Get it on Google Play', 'Download on the App Store'].map((s) => (
            <span key={s} className="rounded-lg bg-inverse px-4 py-2.5 text-sm font-semibold text-on-inverse">{s}</span>
          ))}
        </div>
      </div>
      <div className="hidden justify-center md:flex" aria-hidden>
        <div className="h-56 w-32 rounded-[28px] border-[6px] border-inverse bg-canvas p-2">
          <div className="h-3 w-12 rounded-full bg-line" />
          <div className="mt-3 space-y-2">{[0, 1, 2, 3].map((i) => <div key={i} className="h-9 rounded-lg bg-surface shadow-sm" />)}</div>
        </div>
      </div>
    </section>
  );
}
