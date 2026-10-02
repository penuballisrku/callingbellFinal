import { Link } from 'react-router';
import { Skeleton } from '@mui/material';
import PlaceRounded from '@mui/icons-material/PlaceRounded';
import HomeWorkOutlined from '@mui/icons-material/HomeWorkOutlined';
import VideocamOutlined from '@mui/icons-material/VideocamOutlined';
import EventAvailableOutlined from '@mui/icons-material/EventAvailableOutlined';
import { AvailabilityBadge, Img, Rating, VerifiedMark } from './ui';
import { money } from '@/lib/format';
import type { BusinessCard as Card } from '@/lib/types';

export function BusinessCard({ b, layout = 'grid' }: { b: Card; layout?: 'grid' | 'row' }) {
  if (layout === 'row') return <BusinessRow b={b} />;
  return (
    <Link to={`/b/${b.slug}`}
      className="group card flex h-full flex-col overflow-hidden">
      <div className="relative">
        <Img src={b.coverImageUrl} alt={`${b.name} cover`} aspect="16/7" rounded="rounded-none" className="w-full" fallbackText={b.name} />
        {(b.isSponsored || b.isFeatured) && (
          <span className="absolute left-3 top-3 rounded-md bg-surface/95 px-2 py-0.5 text-[11px] font-semibold text-ink">
            {b.isSponsored ? 'Sponsored' : 'Featured'}
          </span>
        )}
        <div className="absolute -bottom-6 left-4 rounded-xl border-2 border-surface bg-surface">
          <Img src={b.logoUrl} alt={`${b.name} logo`} className="h-12 w-12" rounded="rounded-[10px]" fallbackText={b.name} />
        </div>
      </div>
      <div className="flex flex-1 flex-col px-4 pb-4 pt-8">
        <div className="flex items-center gap-1">
          <h3 className="truncate text-[15px] font-semibold group-hover:underline">{b.name}</h3>
          {b.isVerified && <VerifiedMark />}
        </div>
        <p className="mt-0.5 truncate text-[13px] text-muted">{b.subCategoryName ?? b.categoryName} · {b.area ? `${b.area}, ` : ''}{b.city}</p>
        <div className="mt-2.5 flex items-center justify-between gap-2">
          <Rating value={b.averageRating} count={b.reviewCount} />
          <AvailabilityBadge status={b.availabilityStatus} lastSeenOn={b.lastSeenOn} compact />
        </div>
        <div className="mt-auto flex items-center justify-between gap-2 pt-3 text-[13px]">
          <span className="text-muted">{b.startingPrice ? <>From <span className="font-semibold text-ink">{money(b.startingPrice)}</span></> : (b.isOpenNow ? 'Open now' : 'View services')}</span>
          <span className={`font-medium ${b.isOpenNow ? 'text-success' : 'text-muted'}`}>{b.isOpenNow ? 'Open' : 'Closed'}</span>
        </div>
      </div>
    </Link>
  );
}

function BusinessRow({ b }: { b: Card }) {
  return (
    <Link to={`/b/${b.slug}`} className="group card flex gap-4 p-4">
      <Img src={b.logoUrl} alt={`${b.name} logo`} className="h-16 w-16 shrink-0 sm:h-20 sm:w-20" rounded="rounded-xl" fallbackText={b.name} />
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <h3 className="truncate text-base font-semibold group-hover:underline">{b.name}</h3>
          {b.isVerified && <VerifiedMark />}
          {b.isSponsored && <span className="rounded bg-subtle px-1.5 py-0.5 text-[11px] font-semibold text-muted">Sponsored</span>}
          {!b.isSponsored && b.isFeatured && <span className="rounded bg-accent-soft px-1.5 py-0.5 text-[11px] font-semibold text-accent-ink">Featured</span>}
        </div>
        {b.tagline && <p className="truncate text-sm text-muted">{b.tagline}</p>}
        <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-[13px]">
          <Rating value={b.averageRating} count={b.reviewCount} />
          <span className="inline-flex items-center gap-0.5 text-muted"><PlaceRounded sx={{ fontSize: 16 }} />{b.area ? `${b.area}, ` : ''}{b.city}{b.distanceKm != null && ` · ${b.distanceKm.toFixed(1)} km`}</span>
          <AvailabilityBadge status={b.availabilityStatus} lastSeenOn={b.lastSeenOn} compact />
        </div>
        <div className="mt-2 flex flex-wrap items-center gap-2 text-xs text-muted">
          <span className="rounded-md border border-line px-2 py-0.5">{b.subCategoryName ?? b.categoryName}</span>
          {b.offersHomeService && <span className="inline-flex items-center gap-1"><HomeWorkOutlined sx={{ fontSize: 15 }} />Home visits</span>}
          {b.offersVideoConsultation && <span className="inline-flex items-center gap-1"><VideocamOutlined sx={{ fontSize: 15 }} />Video consult</span>}
          {b.acceptsOnlineBooking && <span className="inline-flex items-center gap-1"><EventAvailableOutlined sx={{ fontSize: 15 }} />Instant booking</span>}
          <span className={b.isOpenNow ? 'font-medium text-success' : ''}>{b.isOpenNow ? 'Open now' : 'Closed now'}</span>
        </div>
      </div>
      <div className="hidden shrink-0 flex-col items-end justify-between text-right sm:flex">
        {b.startingPrice ? <span className="text-sm text-muted">From <span className="block text-lg font-bold text-ink">{money(b.startingPrice)}</span></span> : <span />}
        {b.responseTimeMinutes ? <span className="text-xs text-muted">Replies in ~{b.responseTimeMinutes} min</span> : null}
      </div>
    </Link>
  );
}

export function BusinessCardSkeleton({ layout = 'grid' }: { layout?: 'grid' | 'row' }) {
  if (layout === 'row') return <Skeleton variant="rounded" height={124} />;
  return (
    <div className="card overflow-hidden">
      <Skeleton variant="rectangular" sx={{ aspectRatio: '16/7', height: 'auto' }} />
      <div className="space-y-2 p-4 pt-8"><Skeleton width="70%" /><Skeleton width="50%" /><Skeleton width="90%" /></div>
    </div>
  );
}
