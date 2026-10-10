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
import CheckCircleRounded from '@mui/icons-material/CheckCircleRounded';
import HomeRounded from '@mui/icons-material/HomeRounded';
import ChatBubbleOutlineRounded from '@mui/icons-material/ChatBubbleOutlineRounded';
import NotificationsNoneRounded from '@mui/icons-material/NotificationsNoneRounded';
import SignalCellularAltRounded from '@mui/icons-material/SignalCellularAltRounded';
import WifiRounded from '@mui/icons-material/WifiRounded';
import BatteryFullRounded from '@mui/icons-material/BatteryFullRounded';
import TrendingUpRounded from '@mui/icons-material/TrendingUpRounded';
import PhoneInTalkOutlined from '@mui/icons-material/PhoneInTalkOutlined';
import StorefrontOutlined from '@mui/icons-material/StorefrontOutlined';
import NotificationsActiveRounded from '@mui/icons-material/NotificationsActiveRounded';
import ChatOutlined from '@mui/icons-material/ChatOutlined';
import { LogoMark } from '@/components/Logo';
import { api } from '@/lib/api';
import { compactNumber, money, number, pluralize } from '@/lib/format';
import { useCategories, useDocumentTitle, usePageCity, useSelectedAreaSlug } from '@/lib/hooks';
import { useCity } from '@/stores/city';
import type { Banner, BusinessCard, HomeData, MarketingPage, NearbyService, SearchSuggestion, NearbyServices, PopularService, RelatedCategory, SubCategory, TopPicks } from '@/lib/types';
import { ErrorState, Img, SectionHeader } from '@/components/ui';
import { CitySelect } from '@/components/LocationPicker';
import { useBrowsingCountryCode } from '@/components/VisitorCountry';
import { resolveSearchHref, SearchSuggest } from '@/components/SearchSuggest';
import { VoiceSearchButton } from '@/components/VoiceSearch';
import { useAssistant } from '@/features/assistant/store';
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
  // Waits for the visitor's detected city (first visit only) so the home content loads once, for their city.
  const { citySlug, ready } = usePageCity();
  const queryKey = ['home', citySlug];
  const { data, isPending: isLoading, isError, refetch } = useQuery({ queryKey, queryFn: () => api.get<HomeData>('/api/home', { city: citySlug }), enabled: ready });
  const { data: categoryTree } = useCategories();
  const popularSubs = useMemo(() => interleaveByCategory(data?.categories ?? []).slice(0, 18), [data]);

  if (isError) return <div className="container-page py-16"><ErrorState onRetry={() => refetch()} /></div>;

  return (
    <>
      <Hero data={data} />

      <div className="container-page space-y-14 py-12 md:space-y-16">
        <section aria-labelledby="cat-h">
          <SectionHeader id="cat-h" title="Popular categories"
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

        <PillarsSection />

        <ServicesSection items={data?.popularServices} loading={isLoading} />

        <NearbyServicesSection />

        <TopPicksSection />

        {!!data?.promoBanners.length && <PromoCarousel banners={data.promoBanners} />}

        <ReviewsSection />

        <JoinBand />

        <AppDownload data={data} />
      </div>
    </>
  );
}

function Hero({ data }: { data?: HomeData }) {
  const navigate = useNavigate();
  const citySlug = useCity((s) => s.citySlug);
  const areaId = useCity((s) => s.areaId);
  const [q, setQ] = useState('');
  const openAssistant = useAssistant((s) => s.openAssistant);
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
    // Text naming a place ("lawyers in Nellore") selects that city/area first.
    void resolveSearchHref(q, picked, citySlug, areaId).then((href) => navigate(href));
  };
  const pick = (s: SearchSuggestion) => {
    if (s.kind === 'Business') { navigate(`/business/${s.slug}`); return; }
    setQ(s.label);
    setPicked(s);
  };

  return (
    <section className="relative overflow-hidden bg-navy text-white">
      <SignalRings className="-left-40 -top-40 h-[560px] w-[560px]" />
      <div aria-hidden className="pointer-events-none absolute -left-24 top-10 h-72 w-72 rounded-full bg-accent/15 blur-3xl" />
      <div className="container-page relative grid gap-10 py-12 md:py-16 lg:grid-cols-[1.15fr_1fr] lg:items-center">
        <div>
          <p className="text-sm font-semibold uppercase tracking-[0.12em] text-accent">Discover. Connect. Book. Grow.</p>
          <h1 className="mt-3 text-[34px] font-extrabold leading-[1.08] tracking-[-0.035em] sm:text-5xl lg:text-[56px]">
            Looking for <span className="text-accent">trusted</span> local services?
          </h1>
          <p className="mt-4 max-w-xl text-base text-on-navy-muted md:text-lg">Find pros who are available right now. Compare verified businesses, see live availability, request quotes and book in minutes.</p>

          <form onSubmit={submit} className="mt-7 flex flex-col gap-2 rounded-xl bg-surface p-2 sm:flex-row sm:items-center" role="search">
            <div className="flex min-w-0 flex-1 items-center gap-2 px-2">
              <SearchRounded sx={{ color: 'var(--cb-faint)', flexShrink: 0 }} />
              <SearchSuggest value={q} onChange={setQ} citySlug={citySlug} onSelect={pick} ariaLabel="What are you looking for?"
                placeholder={categoryNames.length ? '' : 'Search for services, businesses…'}
                className="min-w-0 flex-1" panelClassName="-ml-9 mt-3 sm:min-w-[460px]"
                inputClassName="h-12 w-full truncate bg-transparent text-[15px] text-ink outline-none placeholder:text-faint">
                {!q && categoryNames.length > 0 && <PlaceholderTicker names={categoryNames} />}
              </SearchSuggest>
              <VoiceSearchButton onText={(t) => setQ(t)} submit size="medium" />
            </div>
            <div className="min-w-0 sm:w-56 sm:shrink-0"><CitySelect size="medium" fullWidth height={48} /></div>
            <Button type="submit" variant="contained" color="secondary" size="large" sx={{ height: 48, px: 3, flexShrink: 0 }}>Search</Button>
          </form>
          <button type="button" onClick={() => openAssistant(q)}
            className="group mt-3 inline-flex items-center gap-1.5 text-left text-sm text-on-navy-muted transition-colors hover:text-white">
            <AutoAwesomeRounded sx={{ fontSize: 16 }} className="text-accent" />
            Not sure what to search for? <span className="font-semibold text-on-navy underline-offset-2 group-hover:underline">Describe it to our AI assistant</span>
          </button>

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
      <SectionHeader id="svc-h" title="Services" subtitle="Book trusted local services at upfront prices" />
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
  const { citySlug, areaId, ready } = usePageCity();
  const areaSlug = useSelectedAreaSlug(citySlug, areaId);
  const country = useBrowsingCountryCode();
  const { data, isPending: isLoading } = useQuery({
    // Once the place is known in full (an area's slug may arrive with its city's area list), so it is asked for once.
    enabled: ready && (!areaId || !!areaSlug),
    queryKey: ['nearby-services', citySlug, areaSlug, country],
    queryFn: () => api.get<NearbyServices>('/api/geo/nearby-services', { city: citySlug, area: areaSlug, country }),
    refetchInterval: (q) => (q.state.data?.aiPending ? 5000 : false),
    staleTime: 5 * 60_000,
  });

  if (!isLoading && !data?.items.length) return null;
  const place = data?.placeName ?? data?.cityName;
  return (
    <section aria-labelledby="near-h">
      <SectionHeader id="near-h" title={place ? `Popular near ${place}` : 'Popular near you'}
        subtitle={data?.catalog
          ? (data.aiRanked
            ? `No listings here yet - picked for ${place} and the season from services booked on Calling Bell`
            : 'No listings here yet - services people book most on Calling Bell')
          : data?.aiRanked
            ? 'Picked for your area and the season, from recent local bookings'
            : 'What people around you are booking right now'} />
      <div className="grid grid-cols-2 gap-3 sm:gap-4 md:grid-cols-3 lg:grid-cols-4 lg:gap-5">
        {isLoading
          ? Array.from({ length: 4 }, (_, i) => <Skeleton key={i} variant="rounded" height={300} sx={{ borderRadius: '18px' }} />)
          : data!.items.map((s) => <ServiceCard key={`${s.subCategorySlug}-${s.searchTerm}`} service={s} />)}
      </div>
      {!!data?.relatedCategories.length && (
        // An area: what is near it (the rest of the city follows below). A city: everything available in it.
        data.source === 'area' ? (
          <RelatedCategories id="related" items={data.relatedCategories} title={`Related categories near ${place}`}
            expandLabel={`Show all ${number(data.relatedCategories.length)} categories near ${place}`} citySlug={data.citySlug}
            note={data.catalog ? `Suggested for ${place}` : data.aiRanked ? 'Best matches first, from what people nearby book together' : `Within 7 km of ${place}`} />
        ) : (
          <RelatedCategories id="related" items={data.relatedCategories} title={`Categories in ${data.cityName ?? place ?? 'your city'}`}
            expandLabel={`Show all ${number(data.relatedCategories.length)} categories in ${data.cityName ?? place ?? 'your city'}`} citySlug={data.citySlug}
            note={data.catalog
              ? (data.aiRanked ? 'Best matches for this city first, then the full catalogue' : 'Full catalogue - most booked on Calling Bell first')
              : data.aiRanked ? 'Best matches first, then everything available in the city' : 'Everything available in the city'} />
        )
      )}
      {/* A selected area: the rest of the city's categories, so nothing available in the city is hidden. */}
      {!!data?.cityCategories?.length && (
        <RelatedCategories id="city-categories" items={data.cityCategories}
          title={data.relatedCategories.length ? `More categories in ${data.cityName}` : `Categories in ${data.cityName}`}
          expandLabel={`Show all ${number(data.cityCategories.length)} categories in ${data.cityName}`} citySlug={data.citySlug}
          note={data.catalog ? 'Full catalogue - most booked on Calling Bell first' : `Elsewhere in ${data.cityName}, distance from ${place}`} />
      )}
    </section>
  );
}

const RELATED_PREVIEW = 6;

/**
 * A list of sub-categories as link cards: those near the visitor (the AI's related picks first, with reasons once ready), or for a
 * selected area also the rest of the city's. The first {@link RELATED_PREVIEW} show by default; "Show all" expands to the full list.
 */
function RelatedCategories({ id, items, title, expandLabel, note, citySlug }: {
  id: string; items: RelatedCategory[]; title: string; expandLabel: string; note: string; citySlug?: string | null;
}) {
  const [showAll, setShowAll] = useState(false);
  const visible = showAll ? items : items.slice(0, RELATED_PREVIEW);
  const hidden = items.length - RELATED_PREVIEW;
  return (
    <div className="mt-8" role="region" aria-labelledby={`${id}-h`}>
      <div className="mb-3 flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <h3 id={`${id}-h`} className="text-base font-semibold tracking-[-0.01em] md:text-lg">
          {title}
          <span className="ml-2 text-sm font-normal text-muted">{number(items.length)}</span>
        </h3>
        <span className="text-xs text-muted">{note}</span>
      </div>
      <ul id={`${id}-list`} className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
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
                  {c.categoryName}{c.businessCount > 0 && ` · ${pluralize(c.businessCount, 'business', 'businesses')}`}
                  {c.nearestKm != null && ` · ${c.nearestKm < 1 ? 'under 1 km' : `${c.nearestKm.toFixed(1)} km`}`}
                </span>
                {c.reason && <span className="mt-1.5 line-clamp-2 text-xs leading-5 text-ink-2">{c.reason}</span>}
              </span>
            </Link>
          </li>
        ))}
      </ul>
      {hidden > 0 && (
        <div className="mt-5 flex flex-wrap items-center justify-center gap-3">
          <Button variant="outlined" onClick={() => setShowAll((v) => !v)} aria-expanded={showAll} aria-controls={`${id}-list`}
            endIcon={showAll ? <ExpandLessRounded /> : <ExpandMoreRounded />}>
            {showAll ? 'Show fewer categories' : expandLabel}
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
        {near?.reason && <p className="mt-2 line-clamp-2 text-xs leading-5 text-muted">{near.reason}</p>}
      </div>
    </Link>
  );
}

/**
 * "Top picks in {city}": categories and sub-categories for the city detected from the visitor's IP address. Database-ranked picks show
 * immediately; further picks for that city (with a reason) are appended once ready. The selected city is only a fallback for when
 * the IP can't be located.
 */
function TopPicksSection() {
  const { citySlug, ready } = usePageCity();
  const country = useBrowsingCountryCode();
  const { data: picks, isPending: isLoading } = useQuery({
    enabled: ready,
    queryKey: ['top-picks', citySlug, country],
    // The chosen (or detected) city decides; with none, the visitor's IP location does (when it is in the country being browsed).
    queryFn: () => api.get<TopPicks>('/api/geo/top-picks', { city: citySlug, fallbackCity: citySlug, country }),
    refetchInterval: (q) => (q.state.data?.aiPending ? 5000 : false),
    staleTime: 10 * 60_000,
  });

  if (!isLoading && !picks?.categories.length) return null;
  const more = (picks?.totalInCity ?? 0) - (picks?.categories.filter((c) => c.source === 'database').length ?? 0);
  return (
    <section aria-labelledby="top-picks-h">
      <SectionHeader id="top-picks-h" title={picks?.cityName ? `Top picks in ${picks.cityName}` : 'Top picks'}
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
              <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl" style={{ background: `${c.colorHex ?? '#667085'}14` }}>
                <Img src={c.iconUrl} alt="" className="h-6 w-6" rounded="rounded" fit="contain" fallbackText={c.name} />
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

/** The "signal" arcs from the Calling Bell mark, radiating behind navy sections. Decorative. */
function SignalRings({ className }: { className: string }) {
  return (
    <svg aria-hidden className={`pointer-events-none absolute text-accent ${className}`} viewBox="0 0 560 560" fill="none">
      {[80, 140, 200, 260].map((r, i) => <circle key={r} cx="280" cy="280" r={r} stroke="currentColor" strokeWidth="1.5" opacity={0.22 - i * 0.045} />)}
    </svg>
  );
}

const PILLARS = [
  { word: 'Discover', icon: <SearchRounded />, text: 'Find trusted services near you, with live availability.', to: '/search', cta: 'Search services' },
  { word: 'Connect', icon: <PhoneInTalkOutlined />, text: 'Call, chat or send an enquiry to a business directly.', to: '/nearby', cta: 'Explore nearby' },
  { word: 'Book', icon: <EventAvailableRounded />, text: 'Schedule a visit or a consultation in a few taps.', to: '/categories', cta: 'Browse categories' },
  { word: 'Grow', icon: <TrendingUpRounded />, text: 'Local businesses get discovered by customers nearby.', to: '/list-your-business', cta: 'List your business' },
];

/** How Calling Bell works, in the four steps of the tagline. The order is the customer's journey, so the steps are numbered. */
function PillarsSection() {
  return (
    <section aria-labelledby="how-h">
      <SectionHeader id="how-h" title="One platform for every local service" subtitle="Discover. Connect. Book. Grow." />
      <ol className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4 lg:gap-4">
        {PILLARS.map((p, i) => (
          <li key={p.word}>
            <Link to={p.to}
              className="group flex h-full flex-col rounded-2xl border border-line bg-surface p-5 shadow-[var(--cb-shadow-xs)] outline-offset-2 transition-[box-shadow,border-color] duration-200 hover:border-line-strong hover:shadow-[var(--cb-shadow-md)] focus-visible:outline-2 focus-visible:outline-accent">
              <span className="flex items-center justify-between">
                <span className="grid h-11 w-11 place-items-center rounded-xl bg-navy text-accent [&_svg]:text-[22px]">{p.icon}</span>
                <span className="tabular text-xs font-semibold text-faint">0{i + 1}</span>
              </span>
              <span className="mt-4 text-[28px] font-extrabold leading-none tracking-[-0.04em] text-ink">{p.word}<span className="text-accent">.</span></span>
              <span className="mt-2 flex-1 text-sm text-muted">{p.text}</span>
              <span className="mt-4 inline-flex items-center gap-1 text-sm font-semibold text-ink-2 group-hover:text-accent-ink">
                {p.cta} <ArrowForwardRounded sx={{ fontSize: 16 }} className="transition-transform group-hover:translate-x-0.5" />
              </span>
            </Link>
          </li>
        ))}
      </ol>
    </section>
  );
}

/** Invitation for local businesses, in the navy and orange of the brand. */
function JoinBand() {
  return (
    <section aria-labelledby="join-h" className="relative overflow-hidden rounded-2xl bg-navy text-white ring-1 ring-inset ring-white/8">
      <SignalRings className="-right-48 -top-48 h-[600px] w-[600px]" />
      <div className="relative grid items-center gap-10 p-6 md:p-10 lg:grid-cols-[1.2fr_1fr]">
        <div>
          <span className="inline-flex items-center gap-1.5 rounded-full border border-accent/40 bg-accent/10 px-3 py-1 text-xs font-semibold text-accent">
            <StorefrontOutlined sx={{ fontSize: 16 }} />For local businesses
          </span>
          <h2 id="join-h" className="mt-4 text-3xl font-extrabold leading-[1.1] tracking-[-0.03em] md:text-[40px]">
            Get your business <span className="text-accent">discovered</span>
          </h2>
          <p className="mt-3 max-w-lg text-base text-on-navy-muted md:text-lg">Join Calling Bell and reach customers searching near you. Receive enquiries and bookings in one place.</p>
          <div className="mt-7 flex flex-wrap gap-3">
            <Button variant="contained" color="secondary" size="large" component={Link} to="/list-your-business" endIcon={<ArrowForwardRounded />}>Join Calling Bell Today</Button>
            <Button size="large" component={Link} to="/pricing"
              sx={{ color: 'var(--cb-on-navy)', '&:hover': { bgcolor: 'rgba(255,255,255,.06)' } }}>See plans</Button>
          </div>
        </div>

        {/* A sample listing, as the reel shows it. Decorative. */}
        <div aria-hidden className="pointer-events-none relative mx-auto w-full max-w-sm select-none pb-10">
          <div className="overflow-hidden rounded-2xl bg-surface text-ink shadow-[0_24px_48px_-16px_rgba(0,0,0,0.5)]">
            <div className="h-16 bg-gradient-to-r from-[#0B1220] via-[#1D2A44] to-[#F4A62C]/70" />
            <div className="-mt-8 px-5 pb-5">
              <span className="grid h-16 w-16 place-items-center rounded-2xl border-4 border-surface bg-accent text-on-accent"><StorefrontOutlined fontSize="large" /></span>
              <div className="mt-3 text-lg font-bold tracking-tight">Your Business</div>
              <div className="mt-0.5 inline-flex items-center gap-1 text-sm font-semibold text-accent-ink"><CheckCircleRounded sx={{ fontSize: 16 }} />Listed on Calling Bell</div>
              <div className="mt-4 grid grid-cols-3 gap-2 text-xs font-semibold">
                <span className="flex items-center justify-center gap-1 rounded-full border border-line py-2"><PhoneInTalkOutlined sx={{ fontSize: 15 }} />Call</span>
                <span className="flex items-center justify-center gap-1 rounded-full border border-line py-2"><ChatOutlined sx={{ fontSize: 15 }} />Enquire</span>
                <span className="flex items-center justify-center gap-1 rounded-full bg-accent py-2 text-on-accent"><EventAvailableRounded sx={{ fontSize: 15 }} />Book</span>
              </div>
            </div>
          </div>
          <div className="absolute -right-2 bottom-0 flex items-center gap-3 rounded-xl border border-accent/40 bg-[#121B2E] px-3.5 py-3 shadow-[0_16px_32px_-8px_rgba(0,0,0,0.6)] sm:-right-6">
            <span className="grid h-9 w-9 place-items-center rounded-lg bg-accent text-on-accent"><NotificationsActiveRounded sx={{ fontSize: 20 }} /></span>
            <span>
              <span className="block text-sm font-bold text-white">New enquiry</span>
              <span className="block text-xs text-on-navy-muted">A customer nearby wants to connect</span>
            </span>
          </div>
        </div>
      </div>
    </section>
  );
}

function AppDownload({ data }: { data?: HomeData }) {
  return (
    <section className="card grid items-center gap-8 overflow-hidden p-6 md:grid-cols-[1.3fr_1fr] md:p-10">
      <div>
        <span className="inline-flex items-center gap-1 rounded-full bg-accent-soft px-3 py-1 text-xs font-semibold text-accent-ink"><PhoneIphoneRounded sx={{ fontSize: 16 }} />Coming soon to Android & iOS</span>
        <h2 className="mt-3 text-2xl font-bold tracking-tight md:text-3xl">Calling Bell in your pocket</h2>
        <p className="mt-2 max-w-lg text-muted">Get instant alerts when your booking is confirmed, chat with businesses and see who's available nearby - wherever you are.</p>
        <ul className="mt-4 grid max-w-lg gap-2 text-sm text-ink-2 sm:grid-cols-2">
          {['Live availability of businesses near you', 'Book and reschedule in a few taps', 'Chat and call businesses directly', 'Booking and offer alerts'].map((t) => (
            <li key={t} className="flex items-start gap-2"><CheckCircleRounded sx={{ fontSize: 18 }} className="mt-px shrink-0 text-success" />{t}</li>
          ))}
        </ul>
        <div className="mt-6 flex flex-wrap gap-3">
          {['Get it on Google Play', 'Download on the App Store'].map((s) => (
            <span key={s} className="rounded-lg bg-inverse px-4 py-2.5 text-sm font-semibold text-on-inverse">{s}</span>
          ))}
        </div>
      </div>
      <div className="flex justify-center">
        <AppPreview data={data} />
      </div>
    </section>
  );
}

/**
 * A phone showing the Calling Bell app's home screen, filled with live data: the visitor's city, its popular services and its top
 * rated businesses (from the API, like the rest of the page). Decorative, so hidden from screen readers.
 */
function AppPreview({ data }: { data?: HomeData }) {
  const { citySlug, ready } = usePageCity();
  const { data: businesses } = useQuery({
    queryKey: ['app-preview', citySlug],
    queryFn: () => api.get<BusinessCard[]>('/api/businesses', { city: citySlug, sort: 'rating', page: 1, pageSize: 2 }),
    enabled: ready,
    staleTime: 10 * 60_000,
  });
  const services = data?.categories.slice(0, 4) ?? [];
  const tabs = [
    { icon: <HomeRounded sx={{ fontSize: 18 }} />, label: 'Home', active: true },
    { icon: <SearchRounded sx={{ fontSize: 18 }} />, label: 'Search' },
    { icon: <EventAvailableRounded sx={{ fontSize: 18 }} />, label: 'Bookings' },
    { icon: <ChatBubbleOutlineRounded sx={{ fontSize: 18 }} />, label: 'Chats' },
  ];
  return (
    <div aria-hidden className="pointer-events-none relative select-none">
      {/* Soft glow behind the phone. */}
      <div className="absolute inset-x-6 bottom-4 top-10 rounded-full bg-accent/20 blur-3xl" />
      <div className="relative w-[248px] rounded-[40px] bg-[#0B1220] p-[9px] shadow-[0_24px_48px_-20px_rgba(11,18,32,0.55)] ring-1 ring-black/10">
        <div className="relative flex h-[500px] flex-col overflow-hidden rounded-[32px] bg-canvas">
          {/* Camera notch and status bar. */}
          <div className="absolute left-1/2 top-2 h-5 w-20 -translate-x-1/2 rounded-full bg-[#0B1220]" />
          <div className="flex items-center justify-between bg-navy px-5 pb-1 pt-2.5 text-[10px] font-semibold text-white">
            <span>9:41</span>
            <span className="flex items-center gap-1"><SignalCellularAltRounded sx={{ fontSize: 12 }} /><WifiRounded sx={{ fontSize: 12 }} /><BatteryFullRounded sx={{ fontSize: 12 }} /></span>
          </div>

          {/* App header: brand, location and search. */}
          <div className="bg-navy px-3.5 pb-3.5 pt-2 text-white">
            <div className="flex items-center justify-between">
              <span className="flex items-center gap-1.5">
                <LogoMark size={22} tone="onDark" />
                <span className="text-[13px] font-bold tracking-[-0.02em]">Calling Bell</span>
              </span>
              <NotificationsNoneRounded sx={{ fontSize: 18 }} />
            </div>
            <p className="mt-2 flex items-center gap-0.5 text-[10px] text-on-navy-muted">
              <PlaceOutlined sx={{ fontSize: 12 }} />{data?.cityName ?? 'Near you'}
            </p>
            <div className="mt-1.5 flex items-center gap-1.5 rounded-lg bg-white px-2.5 py-2 text-[10px] text-[#667085]">
              <SearchRounded sx={{ fontSize: 14 }} />
              <span className="truncate">Search {services.slice(0, 2).map((c) => c.name.toLowerCase()).join(', ') || 'services'}…</span>
            </div>
          </div>

          <div className="flex-1 space-y-3 overflow-hidden px-3 pt-3">
            {/* Popular services in the city. */}
            <div className="grid grid-cols-4 gap-1.5">
              {(services.length ? services : Array.from({ length: 4 }, () => null)).map((c, i) => (
                <div key={c?.id ?? i} className="flex flex-col items-center gap-1">
                  {c ? <Img src={c.iconUrl ?? c.imageUrl} alt="" fallbackText={c.name} className="h-10 w-10" rounded="rounded-xl" />
                    : <Skeleton variant="rounded" width={40} height={40} sx={{ borderRadius: '12px' }} />}
                  <span className="w-full truncate text-center text-[8.5px] font-medium text-ink-2">{c?.name ?? ''}</span>
                </div>
              ))}
            </div>

            {/* Top rated businesses in the city. */}
            <div>
              <p className="mb-1.5 text-[11px] font-bold text-ink">Top rated near you</p>
              <div className="space-y-2">
                {(businesses ?? [null, null]).slice(0, 2).map((b, i) => b ? (
                  <div key={b.id} className="flex gap-2 rounded-xl border border-line bg-surface p-2">
                    <Img src={b.logoUrl} alt="" fallbackText={b.name} className="h-10 w-10 shrink-0" rounded="rounded-lg" />
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-[11px] font-semibold text-ink">{b.name}</p>
                      <p className="truncate text-[9px] text-muted">{b.subCategoryName ?? b.categoryName} · {b.area ?? b.city}</p>
                      <div className="mt-1 flex items-center justify-between gap-1">
                        <span className="flex min-w-0 items-center gap-0.5 truncate text-[9px] font-semibold text-ink">
                          <StarRounded sx={{ fontSize: 11, color: '#F4A62C' }} />{b.averageRating.toFixed(1)}
                          {b.startingPrice ? <span className="font-normal text-muted"> · from {money(b.startingPrice)}</span> : null}
                        </span>
                        <span className="shrink-0 rounded-md bg-accent px-2 py-0.5 text-[9px] font-bold text-on-accent">Book</span>
                      </div>
                    </div>
                  </div>
                ) : <Skeleton key={i} variant="rounded" height={58} sx={{ borderRadius: '12px' }} />)}
              </div>
            </div>
          </div>

          {/* Bottom tab bar. */}
          <div className="grid grid-cols-4 border-t border-line bg-surface px-2 pb-3 pt-1.5">
            {tabs.map((t) => (
              <span key={t.label} className={`flex flex-col items-center gap-0.5 text-[8.5px] font-medium ${t.active ? 'text-accent-ink' : 'text-faint'}`}>
                {t.icon}{t.label}
              </span>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
