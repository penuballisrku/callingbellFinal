import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Skeleton } from '@mui/material';
import SearchRounded from '@mui/icons-material/SearchRounded';
import ArrowForwardRounded from '@mui/icons-material/ArrowForwardRounded';
import BoltRounded from '@mui/icons-material/BoltRounded';
import FormatQuoteRounded from '@mui/icons-material/FormatQuoteRounded';
import PhoneIphoneRounded from '@mui/icons-material/PhoneIphoneRounded';
import { api } from '@/lib/api';
import { ago, compactNumber, money, number } from '@/lib/format';
import { useDocumentTitle } from '@/lib/hooks';
import { usePresence } from '@/lib/realtime';
import { useCity } from '@/stores/city';
import type { Banner, BusinessCard as Card, HomeData } from '@/lib/types';
import { BusinessCard, BusinessCardSkeleton } from '@/components/BusinessCard';
import { ErrorState, Img, SectionHeader, Stars } from '@/components/ui';
import { CitySelect } from '@/layouts/CustomerLayout';

const quickSearches = [
  { label: 'Electricians near me', to: '/search?sub=electrical&sort=distance' },
  { label: 'Available doctors', to: '/search?sub=doctors&availability=now' },
  { label: 'Online tutors', to: '/search?sub=private-tutors&video=true' },
  { label: 'Lawyers near me', to: '/search?sub=lawyers&sort=distance' },
  { label: 'AC repair nearby', to: '/search?sub=ac-repair&sort=distance' },
  { label: 'Salons open now', to: '/search?sub=beauty-salons&openNow=true' },
];

export default function HomePage() {
  useDocumentTitle();
  const citySlug = useCity((s) => s.citySlug);
  const queryClient = useQueryClient();
  const queryKey = ['home', citySlug];
  const { data, isLoading, isError, refetch } = useQuery({ queryKey, queryFn: () => api.get<HomeData>('/api/home', { city: citySlug }) });

  // Live availability for every business on the page.
  const ids = useMemo(() => {
    if (!data) return [];
    return [...new Set([...data.nearby, ...data.onlineNow, ...data.featured, ...data.sponsored, ...data.topRated].map((b) => b.id))];
  }, [data]);
  usePresence(ids, (e) => {
    queryClient.setQueryData<HomeData>(queryKey, (old) => {
      if (!old) return old;
      const patch = (list: Card[]) => list.map((b) => (b.id === e.businessId ? { ...b, availabilityStatus: e.status, lastSeenOn: e.lastSeenOn } : b));
      return { ...old, nearby: patch(old.nearby), onlineNow: patch(old.onlineNow), featured: patch(old.featured), sponsored: patch(old.sponsored), topRated: patch(old.topRated) };
    });
  });

  if (isError) return <div className="container-page py-16"><ErrorState onRetry={() => refetch()} /></div>;
  const cityLabel = data?.cityName ?? 'your city';

  return (
    <>
      <Hero data={data} />

      <div className="container-page space-y-14 py-12 md:space-y-16">
        <section aria-labelledby="cat-h">
          <SectionHeader title="Popular categories" subtitle="Trusted professionals across 40+ services"
            action={<Link to="/categories" className="hidden items-center gap-1 text-sm font-semibold sm:inline-flex">All categories <ArrowForwardRounded sx={{ fontSize: 18 }} /></Link>} />
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6">
            {isLoading ? Array.from({ length: 12 }, (_, i) => <Skeleton key={i} variant="rounded" height={168} />) : data!.categories.map((c) => (
              <Link key={c.id} to={`/search?sub=${c.slug}`} className="group card overflow-hidden transition-colors hover:border-line-strong">
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
        </section>

        <section aria-labelledby="svc-h">
          <SectionHeader title="Popular services" subtitle="Most booked on Calling Bell in the last 90 days" />
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {isLoading ? Array.from({ length: 8 }, (_, i) => <Skeleton key={i} variant="rounded" height={84} />) : data!.popularServices.map((s) => (
              <Link key={`${s.name}-${s.subCategorySlug}`} to={`/search?sub=${s.subCategorySlug}&q=${encodeURIComponent(s.name)}`}
                className="card flex items-center gap-3 p-3 transition-colors hover:border-line-strong">
                <Img src={s.imageUrl} alt="" className="h-14 w-14 shrink-0" fallbackText={s.name} />
                <div className="min-w-0">
                  <div className="truncate text-sm font-semibold">{s.name}</div>
                  <div className="truncate text-xs text-muted">{s.subCategoryName} · {s.providerCount} providers</div>
                  <div className="mt-0.5 text-xs">From <span className="font-semibold">{money(s.startingPrice)}</span> · {number(s.bookingCount)} booked</div>
                </div>
              </Link>
            ))}
          </div>
        </section>

        <BusinessRail title={`Top picks in ${cityLabel}`} subtitle="Highly rated businesses near you" items={data?.nearby} loading={isLoading}
          link={`/search?${data?.citySlug ? `city=${data.citySlug}&` : ''}sort=rating`} />

        <BusinessRail title="Online right now" subtitle="Available to call, chat or book this minute" items={data?.onlineNow} loading={isLoading}
          link={`/search?availability=now${data?.citySlug ? `&city=${data.citySlug}` : ''}`}
          badge={<span className="ml-2 inline-flex items-center gap-1 rounded-full bg-success-soft px-2.5 py-1 align-middle text-xs font-semibold text-success"><BoltRounded sx={{ fontSize: 14 }} />{data ? number(data.stats.onlineNow) : '-'} live</span>} />

        <BusinessRail title="Featured businesses" subtitle="Verified, top-performing businesses across India" items={data?.featured} loading={isLoading} />

        {data?.promoBanners[0] && <PromoBanner banner={data.promoBanners[0]} />}

        {(isLoading || (data?.sponsored.length ?? 0) > 0) && (
          <BusinessRail title="Sponsored" subtitle="Promoted by businesses on Calling Bell" items={data?.sponsored} loading={isLoading} />
        )}

        <section aria-labelledby="rev-h">
          <SectionHeader title="What customers are saying" subtitle="Recent reviews from verified customers" />
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
            {isLoading ? Array.from({ length: 6 }, (_, i) => <Skeleton key={i} variant="rounded" height={190} />) : data!.recentReviews.map((r) => (
              <article key={r.id} className="card flex flex-col p-5">
                <div className="flex items-center justify-between"><Stars value={r.rating} /><span className="text-xs text-muted">{ago(r.createdOn)}</span></div>
                <FormatQuoteRounded sx={{ color: '#F4A62C', mt: 1 }} />
                {r.title && <h3 className="text-sm font-semibold">{r.title}</h3>}
                <p className="mt-1 line-clamp-3 text-sm text-ink-2">{r.comment}</p>
                <div className="mt-auto flex items-center gap-3 border-t border-line pt-4">
                  <Img src={r.businessLogoUrl} alt="" className="h-9 w-9" fallbackText={r.businessName} />
                  <div className="min-w-0 text-xs">
                    <div className="font-semibold">{r.customerName}</div>
                    <Link to={`/b/${r.businessSlug}`} className="block truncate text-muted hover:underline">on {r.businessName}, {r.city}</Link>
                  </div>
                </div>
              </article>
            ))}
          </div>
        </section>

        <BusinessRail title="Top rated" subtitle={`Highest rated with at least 8 reviews${data?.cityName ? ` in ${data.cityName}` : ''}`} items={data?.topRated} loading={isLoading} />

        <AppDownload />
      </div>
    </>
  );
}

function Hero({ data }: { data?: HomeData }) {
  const navigate = useNavigate();
  const citySlug = useCity((s) => s.citySlug);
  const [q, setQ] = useState('');
  const [index, setIndex] = useState(0);
  const banners = data?.heroBanners ?? [];
  useEffect(() => {
    if (banners.length < 2) return;
    const t = setInterval(() => setIndex((i) => (i + 1) % banners.length), 7000);
    return () => clearInterval(t);
  }, [banners.length]);
  const banner = banners[index % Math.max(1, banners.length)];

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    navigate(`/search?q=${encodeURIComponent(q)}${citySlug ? `&city=${citySlug}` : ''}`);
  };

  return (
    <section className="bg-navy text-white">
      <div className="container-page grid gap-10 py-12 md:py-16 lg:grid-cols-[1.15fr_1fr] lg:items-center">
        <div>
          <p className="text-sm font-semibold uppercase tracking-[0.12em] text-accent">Discover. Connect. Book. Grow.</p>
          <h1 className="mt-3 text-3xl font-bold leading-[1.15] tracking-tight sm:text-4xl lg:text-5xl">Find trusted local pros who are available right now.</h1>
          <p className="mt-4 max-w-xl text-base text-on-navy-muted md:text-lg">Compare verified businesses, see live availability, request quotes and book in minutes.</p>

          <form onSubmit={submit} className="mt-7 flex flex-col gap-2 rounded-xl bg-surface p-2 sm:flex-row sm:items-center" role="search">
            <div className="flex flex-1 items-center gap-2 px-2">
              <SearchRounded sx={{ color: 'var(--cb-faint)' }} />
              <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Try “electricians near me” or “salons open now”" aria-label="What are you looking for?"
                className="h-11 w-full bg-transparent text-[15px] text-ink outline-none placeholder:text-faint" />
            </div>
            <div className="sm:w-44"><CitySelect size="medium" /></div>
            <Button type="submit" variant="contained" color="secondary" size="large" sx={{ height: 48, px: 3 }}>Search</Button>
          </form>

          <div className="mt-4 flex flex-wrap gap-2">
            {quickSearches.map((s) => (
              <Link key={s.label} to={`${s.to}${citySlug ? `&city=${citySlug}` : ''}`}
                className="rounded-full border border-white/15 px-3 py-1.5 text-[13px] text-on-navy transition-colors hover:border-accent hover:text-white">{s.label}</Link>
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

function BusinessRail({ title, subtitle, items, loading, link, badge }: {
  title: string; subtitle?: string; items?: Card[]; loading: boolean; link?: string; badge?: React.ReactNode;
}) {
  if (!loading && !items?.length) return null;
  return (
    <section>
      <SectionHeader title={title} subtitle={subtitle}
        action={link && <Link to={link} className="hidden items-center gap-1 whitespace-nowrap text-sm font-semibold sm:inline-flex">View all <ArrowForwardRounded sx={{ fontSize: 18 }} /></Link>} />
      {badge && <div className="-mt-2 mb-4">{badge}</div>}
      <div className="scroll-row" style={{ gridAutoColumns: 'minmax(260px, 1fr)' }}>
        {loading ? Array.from({ length: 4 }, (_, i) => <BusinessCardSkeleton key={i} />) : items!.map((b) => <BusinessCard key={b.id} b={b} />)}
      </div>
    </section>
  );
}

function PromoBanner({ banner }: { banner: Banner }) {
  return (
    <Link to={banner.linkUrl ?? '/'} className="group relative block overflow-hidden rounded-2xl bg-navy text-white">
      <picture>
        <source media="(max-width: 767px)" srcSet={banner.mobileImageUrl ?? banner.imageUrl} />
        <Img src={banner.desktopImageUrl ?? banner.imageUrl} alt={banner.altText ?? banner.title} className="h-full min-h-[220px] w-full md:min-h-[240px]" rounded="rounded-none" eager />
      </picture>
      <div className="absolute inset-0 flex flex-col justify-center bg-gradient-to-r from-[#0B1220] via-[#0B1220]/85 to-transparent p-6 md:p-10">
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
