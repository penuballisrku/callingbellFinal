import { lazy, Suspense, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router';
import { Button, Skeleton } from '@mui/material';
import CallRounded from '@mui/icons-material/CallRounded';
import WhatsApp from '@mui/icons-material/WhatsApp';
import ChatBubbleOutlineRounded from '@mui/icons-material/ChatBubbleOutlineRounded';
import DirectionsRounded from '@mui/icons-material/DirectionsRounded';
import RequestQuoteOutlined from '@mui/icons-material/RequestQuoteOutlined';
import ScheduleRounded from '@mui/icons-material/ScheduleRounded';
import PlaceRounded from '@mui/icons-material/PlaceRounded';
import HomeWorkOutlined from '@mui/icons-material/HomeWorkOutlined';
import VideocamOutlined from '@mui/icons-material/VideocamOutlined';
import EventAvailableOutlined from '@mui/icons-material/EventAvailableOutlined';
import { AvailabilityBadge, CallingBellBadge, Img, Rating, VerifiedMark } from './ui';
import { money } from '@/lib/format';
import { fullAddress, nearLandmark } from '@/lib/address';
import type { BusinessCard as Card } from '@/lib/types';
import { prefetchBusiness } from '@/lib/queryCache';
import { directionsHref, telHref, whatsAppHref, whatsAppSx } from '@/lib/contact';
import { useAuth } from '@/stores/auth';

/** The chat panel loads only when a visitor opens it from a result. */
const ChatDrawer = lazy(() => import('@/features/chat/Chat').then((m) => ({ default: m.ChatDrawer })));

/** Starts loading the business page when the visitor points at, focuses or touches a card, so the click opens it instantly. */
const prefetchProps = (slug: string) => {
  const prefetch = () => prefetchBusiness(slug);
  return { onMouseEnter: prefetch, onFocus: prefetch, onTouchStart: prefetch };
};

/** `compact` is a dense row for narrow panels such as the AI search assistant. */
/** `onSelect` (row layout): a plain click opens the business in a detail panel instead of navigating; Ctrl/Cmd/middle-click still opens the page. */
export function BusinessCard({ b, layout = 'grid', onOpen, onSelect }: { b: Card; layout?: 'grid' | 'row' | 'compact'; onOpen?: () => void; onSelect?: (b: Card) => void }) {
  if (layout === 'row') return <BusinessRow b={b} onSelect={onSelect} />;
  if (layout === 'compact') return <BusinessCompact b={b} onOpen={onOpen} />;
  return (
    <Link to={`/business/${b.slug}`} {...prefetchProps(b.slug)}
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
        <p className="mt-0.5 truncate text-[13px] text-muted">{b.subCategoryName ?? b.categoryName}</p>
        <CardAddress b={b} className="mt-1 text-[13px]" />
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

function BusinessCompact({ b, onOpen }: { b: Card; onOpen?: () => void }) {
  return (
    <Link to={`/business/${b.slug}`} {...prefetchProps(b.slug)} onClick={onOpen} className="group flex gap-3 rounded-xl border border-line bg-surface p-3 transition-colors hover:border-line-strong">
      <Img src={b.logoUrl} alt={`${b.name} logo`} className="h-12 w-12 shrink-0" rounded="rounded-lg" fallbackText={b.name} />
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-1.5">
          <h3 className="truncate text-sm font-semibold group-hover:underline">{b.name}</h3>
          {b.isSponsored && <span className="shrink-0 rounded bg-subtle px-1.5 py-0.5 text-[10px] font-semibold text-muted">Sponsored</span>}
        </div>
        <div className="mt-1"><CallingBellBadge verified={b.isVerified} compact /></div>
        <div className="mt-1 flex flex-wrap items-center gap-x-2.5 gap-y-1 text-xs">
          <Rating value={b.averageRating} count={b.reviewCount} />
          <AvailabilityBadge status={b.availabilityStatus} lastSeenOn={b.lastSeenOn} compact />
        </div>
        <CardAddress b={b} className="mt-1 text-xs" />
        {b.startingPrice ? <p className="mt-0.5 text-xs text-muted">From {money(b.startingPrice)}</p> : null}
      </div>
    </Link>
  );
}

/** A search result: the business (opens it), then buttons to call, message, chat, book and get directions. No prices. */
function BusinessRow({ b, onSelect }: { b: Card; onSelect?: (b: Card) => void }) {
  return (
    <article className="card overflow-hidden">
      <Link to={`/business/${b.slug}`} {...prefetchProps(b.slug)} className="group flex gap-4 p-4 pb-3"
        onClick={onSelect ? (e) => { if (e.button === 0 && !e.metaKey && !e.ctrlKey && !e.shiftKey && !e.altKey) { e.preventDefault(); onSelect(b); } } : undefined}>
        <Img src={b.logoUrl} alt={`${b.name} logo`} className="h-16 w-16 shrink-0 sm:h-20 sm:w-20" rounded="rounded-xl" fallbackText={b.name} />
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <h3 className="truncate text-base font-semibold group-hover:underline">{b.name}</h3>
            <CallingBellBadge verified={b.isVerified} />
            {b.isSponsored && <span className="rounded bg-subtle px-1.5 py-0.5 text-[11px] font-semibold text-muted">Sponsored</span>}
            {!b.isSponsored && b.isFeatured && <span className="rounded bg-accent-soft px-1.5 py-0.5 text-[11px] font-semibold text-accent-ink">Featured</span>}
          </div>
          {b.tagline && <p className="truncate text-sm text-muted">{b.tagline}</p>}
          <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-[13px]">
            <Rating value={b.averageRating} count={b.reviewCount} />
            <AvailabilityBadge status={b.availabilityStatus} lastSeenOn={b.lastSeenOn} compact />
          </div>
          <CardAddress b={b} className="mt-1.5 text-[13px]" />
          <div className="mt-2 flex flex-wrap items-center gap-2 text-xs text-muted">
            <span className="rounded-md border border-line px-2 py-0.5">{b.subCategoryName ?? b.categoryName}</span>
            {b.offersHomeService && <span className="inline-flex items-center gap-1"><HomeWorkOutlined sx={{ fontSize: 15 }} />Home visits</span>}
            {b.offersVideoConsultation && <span className="inline-flex items-center gap-1"><VideocamOutlined sx={{ fontSize: 15 }} />Video consult</span>}
            {b.acceptsOnlineBooking && <span className="inline-flex items-center gap-1"><EventAvailableOutlined sx={{ fontSize: 15 }} />Instant booking</span>}
            <span className={b.isOpenNow ? 'font-medium text-success' : ''}>{b.isOpenNow ? 'Open now' : 'Closed now'}</span>
            {b.responseTimeMinutes ? <span className="inline-flex items-center gap-1"><ScheduleRounded sx={{ fontSize: 15 }} />Replies in ~{b.responseTimeMinutes} min</span> : null}
          </div>
        </div>
      </Link>
      <ResultActions b={b} />
    </article>
  );
}

/**
 * Call and WhatsApp (when the business lists a number), chat (signed in), book or get a quote (on the business page, with the form
 * open) and directions.
 */
function ResultActions({ b }: { b: Card }) {
  const signedIn = useAuth((s) => !!s.user);
  const navigate = useNavigate();
  const location = useLocation();
  const [chat, setChat] = useState(false);
  const whatsApp = whatsAppHref(b.whatsAppNumber ?? b.phoneNumber, b.name);
  const directions = directionsHref(b.latitude, b.longitude, [b.name, fullAddress(b)].filter(Boolean).join(', '));
  const openChat = () => (signedIn ? setChat(true) : navigate(`/login?returnUrl=${encodeURIComponent(location.pathname + location.search)}`));
  return (
    <div className="flex flex-wrap items-center gap-2 border-t border-line px-4 py-3">
      {b.phoneNumber && <Button size="small" variant="contained" disableElevation startIcon={<CallRounded />} href={telHref(b.phoneNumber)} aria-label={`Call ${b.name}`}>Call</Button>}
      {whatsApp && <Button size="small" variant="outlined" startIcon={<WhatsApp />} href={whatsApp} target="_blank" rel="noreferrer" sx={whatsAppSx} aria-label={`WhatsApp ${b.name}`}>WhatsApp</Button>}
      <Button size="small" variant="outlined" startIcon={<ChatBubbleOutlineRounded />} onClick={openChat} aria-label={`Chat with ${b.name}`}>Chat</Button>
      <Button size="small" variant="outlined" component={Link} to={`/business/${b.slug}?action=${b.acceptsOnlineBooking ? 'book' : 'quote'}`}
        startIcon={b.acceptsOnlineBooking ? <EventAvailableOutlined /> : <RequestQuoteOutlined />}>
        {b.acceptsOnlineBooking ? 'Book now' : 'Get a quote'}
      </Button>
      {directions && <Button size="small" startIcon={<DirectionsRounded />} href={directions} target="_blank" rel="noreferrer" aria-label={`Directions to ${b.name}`}>Directions</Button>}
      {chat && <Suspense fallback={null}><ChatDrawer businessId={b.id} open onClose={() => setChat(false)} /></Suspense>}
    </div>
  );
}

/** The full address (street, area, city, pincode), the landmark and the distance, wrapping onto more lines when needed. */
function CardAddress({ b, className }: { b: Card; className?: string }) {
  const near = nearLandmark(b.landmark);
  return (
    <address className={`flex items-start gap-1 not-italic text-muted ${className ?? ''}`}>
      <PlaceRounded sx={{ fontSize: 15, mt: '2px', flexShrink: 0 }} aria-hidden />
      <span className="min-w-0">
        {fullAddress(b)}
        {near && <span className="text-faint"> · {near}</span>}
        {b.distanceKm != null && <span className="whitespace-nowrap"> · {b.distanceKm.toFixed(1)} km</span>}
      </span>
    </address>
  );
}

export function BusinessCardSkeleton({ layout = 'grid' }: { layout?: 'grid' | 'row' }) {
  if (layout === 'row') return <Skeleton variant="rounded" height={212} />;
  return (
    <div className="card overflow-hidden">
      <Skeleton variant="rectangular" sx={{ aspectRatio: '16/7', height: 'auto' }} />
      <div className="space-y-2 p-4 pt-8"><Skeleton width="70%" /><Skeleton width="50%" /><Skeleton width="90%" /></div>
    </div>
  );
}
