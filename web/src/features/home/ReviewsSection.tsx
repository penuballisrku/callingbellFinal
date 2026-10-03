import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { IconButton, Skeleton, useMediaQuery } from '@mui/material';
import ChevronLeftRounded from '@mui/icons-material/ChevronLeftRounded';
import ChevronRightRounded from '@mui/icons-material/ChevronRightRounded';
import AutoAwesomeRounded from '@mui/icons-material/AutoAwesomeRounded';
import VerifiedRounded from '@mui/icons-material/VerifiedRounded';
import FormatQuoteRounded from '@mui/icons-material/FormatQuoteRounded';
import { api } from '@/lib/api';
import { useCities } from '@/lib/hooks';
import { ago, date, initials, number } from '@/lib/format';
import type { LocalReviews } from '@/lib/types';
import { useCity } from '@/stores/city';
import { Img, SectionHeader, Stars } from '@/components/ui';

const SEEN_KEY = 'cb-seen-reviews';
const REFRESH_MS = 120_000;

/** Review ids shown recently, so the next set avoids them (per browser; storage may be unavailable). */
function readSeen(): string[] {
  try { return JSON.parse(localStorage.getItem(SEEN_KEY) ?? '[]') as string[]; } catch { return []; }
}
function rememberSeen(ids: string[]) {
  try { localStorage.setItem(SEEN_KEY, JSON.stringify([...ids, ...readSeen().filter((x) => !ids.includes(x))].slice(0, 45))); } catch { /* ignore */ }
}

/**
 * Customer reviews: real published reviews of businesses near the visitor (IP area, chosen area, or city), a different set on each
 * visit and every 2 minutes, in an auto-advancing carousel. The summary above them is written by the local AI from those same
 * real reviews; until it is ready (or if AI is off) a summary computed from the data is shown.
 */
export function ReviewsSection() {
  const citySlug = useCity((s) => s.citySlug);
  const areaId = useCity((s) => s.areaId);
  const { data: cities } = useCities();
  const areaSlug = useMemo(() => cities?.find((c) => c.slug === citySlug)?.areas.find((a) => a.id === areaId)?.slug ?? null, [cities, citySlug, areaId]);

  // A new random set on load and every REFRESH_MS, without user interaction.
  const [round, setRound] = useState(0);
  useEffect(() => { const t = setInterval(() => setRound((r) => r + 1), REFRESH_MS); return () => clearInterval(t); }, []);
  const fetchReviews = () => api.get<LocalReviews>('/api/geo/reviews', { city: citySlug, area: areaSlug, exclude: readSeen().join(',') || null });
  const { data, isLoading, isError } = useQuery({
    queryKey: ['local-reviews', citySlug, areaSlug, round], queryFn: fetchReviews,
    staleTime: Infinity, gcTime: 0, placeholderData: keepPreviousData,
  });
  useEffect(() => { if (data?.reviews.length) rememberSeen(data.reviews.map((r) => r.id)); }, [data]);
  // While the AI summary is being written, check back for it without reshuffling the cards on screen.
  const { data: later } = useQuery({
    queryKey: ['local-reviews-summary', citySlug, areaSlug], queryFn: fetchReviews,
    enabled: !!data?.aiPending, refetchInterval: (q) => (q.state.data?.aiPending === false ? false : 15_000),
  });
  const aiSummary = data?.aiSummary ?? later?.aiSummary ?? null;

  // Carousel: 1 / 2 / 3 cards per view; auto-advances, pauses on hover or keyboard focus, never auto-moves for reduced motion.
  const md = useMediaQuery('(min-width: 768px)');
  const lg = useMediaQuery('(min-width: 1024px)');
  const reduceMotion = useMediaQuery('(prefers-reduced-motion: reduce)');
  const perView = lg ? 3 : md ? 2 : 1;
  const reviews = data?.reviews ?? [];
  const pages = Math.max(1, Math.ceil(reviews.length / perView));
  const [page, setPage] = useState(0);
  const [paused, setPaused] = useState(false);
  useEffect(() => { setPage(0); }, [data, perView]);
  useEffect(() => {
    if (pages < 2 || paused || reduceMotion) return;
    const t = setInterval(() => setPage((p) => (p + 1) % pages), 6000);
    return () => clearInterval(t);
  }, [pages, paused, reduceMotion]);
  const current = Math.min(page, pages - 1);

  if (isError && !data) return null;
  const place = data?.scope === 'area' ? data.placeName : data?.scope === 'city' ? data.cityName : null;

  return (
    <section aria-labelledby="reviews-h" aria-roledescription="carousel"
      onMouseEnter={() => setPaused(true)} onMouseLeave={() => setPaused(false)} onFocus={() => setPaused(true)} onBlur={() => setPaused(false)}>
      <SectionHeader title={place ? `What customers in ${place} are saying` : 'What customers are saying'}
        subtitle="Real reviews from customers of local businesses, refreshed every few minutes"
        action={pages > 1 ? (
          <div className="flex gap-1">
            <IconButton size="small" aria-label="Previous reviews" onClick={() => setPage((p) => (p - 1 + pages) % pages)} sx={{ border: '1px solid var(--cb-line)' }}><ChevronLeftRounded fontSize="small" /></IconButton>
            <IconButton size="small" aria-label="Next reviews" onClick={() => setPage((p) => (p + 1) % pages)} sx={{ border: '1px solid var(--cb-line)' }}><ChevronRightRounded fontSize="small" /></IconButton>
          </div>
        ) : undefined} />

      {/* Summary: AI-written from the real reviews when ready, otherwise computed from them. */}
      <div className="mb-5 flex flex-col gap-3 rounded-2xl border border-line bg-surface p-4 sm:flex-row sm:items-center md:p-5">
        <div className="flex shrink-0 items-center gap-3 sm:border-r sm:border-line sm:pr-5">
          <span className="text-3xl font-bold tabular">{data ? data.averageRating.toFixed(1) : '–'}</span>
          <span>
            <Stars value={data?.averageRating ?? 0} size={15} />
            <span className="block text-xs text-muted">{data ? `${number(data.reviewCount)} recent reviews` : 'Loading reviews…'}</span>
          </span>
        </div>
        <p className="min-w-0 flex-1 text-sm leading-6 text-ink-2" aria-live="polite">
          {aiSummary ? (
            <>
              <AutoAwesomeRounded sx={{ fontSize: 15, mr: 0.75, mt: '-2px' }} className="text-accent-ink" />
              {aiSummary}
              <span className="ml-1.5 whitespace-nowrap text-[11px] text-faint">AI summary of real reviews</span>
            </>
          ) : data ? (
            <>Customers{place ? ` in ${place}` : ''} rate local businesses <strong className="font-semibold text-ink">{data.averageRating.toFixed(1)} out of 5</strong> on
              average across {number(data.reviewCount)} recent reviews.{data.aiPending && <span className="ml-1.5 text-[11px] text-faint">Summarising with AI…</span>}</>
          ) : <Skeleton width="80%" />}
        </p>
      </div>

      <div className="-mx-2 overflow-hidden" aria-live="off">
        {isLoading && !data ? (
          <div className="grid grid-cols-1 gap-4 px-2 md:grid-cols-2 lg:grid-cols-3">
            {Array.from({ length: perView }, (_, i) => <Skeleton key={i} variant="rounded" height={236} sx={{ borderRadius: '16px' }} />)}
          </div>
        ) : (
          <ul className="flex transition-transform duration-500 ease-out motion-reduce:transition-none"
            style={{ transform: `translateX(-${current * 100}%)` }}>
            {reviews.map((r, i) => {
              const onPage = Math.floor(i / perView) === current;
              return (
                <li key={r.id} className="shrink-0 px-2" style={{ width: `${100 / perView}%` }} aria-hidden={!onPage} inert={!onPage}>
                  <article className="flex h-full flex-col rounded-2xl border border-line bg-surface p-5 shadow-[var(--cb-shadow-xs)]">
                    <div className="flex items-center justify-between gap-2">
                      <Stars value={r.rating} size={15} />
                      <time dateTime={r.createdOn} title={date(r.createdOn)} className="text-xs text-muted">{ago(r.createdOn)}</time>
                    </div>
                    <FormatQuoteRounded sx={{ color: '#F4A62C', mt: 1.5, fontSize: 22 }} />
                    {r.title && <h3 className="text-sm font-semibold">{r.title}</h3>}
                    <p className="mt-1 line-clamp-4 text-sm leading-6 text-ink-2">{r.comment}</p>
                    <div className="mt-auto flex items-center gap-3 border-t border-line pt-4">
                      <span aria-hidden className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-accent-soft text-xs font-bold text-accent-ink">{initials(r.customerName)}</span>
                      <div className="min-w-0 flex-1 text-xs">
                        <div className="flex items-center gap-1 font-semibold text-ink">
                          {r.customerName}
                          {r.isVerifiedVisit && <VerifiedRounded sx={{ fontSize: 14 }} className="text-success" titleAccess="Verified visit" />}
                        </div>
                        <Link to={`/b/${r.businessSlug}`} className="block truncate text-muted hover:underline">
                          on {r.businessName}{r.area ? `, ${r.area}` : ''}
                        </Link>
                      </div>
                      <Img src={r.businessLogoUrl} alt="" className="h-8 w-8 shrink-0" fallbackText={r.businessName} />
                    </div>
                  </article>
                </li>
              );
            })}
          </ul>
        )}
      </div>

      {pages > 1 && (
        <div className="mt-4 flex justify-center gap-1.5">
          {Array.from({ length: pages }, (_, i) => (
            <button key={i} type="button" aria-label={`Show reviews ${i + 1} of ${pages}`} aria-current={i === current}
              onClick={() => setPage(i)} className={`h-2 rounded-full transition-all duration-300 ${i === current ? 'w-6 bg-accent' : 'w-2 bg-line-strong hover:bg-muted'}`} />
          ))}
        </div>
      )}
    </section>
  );
}
