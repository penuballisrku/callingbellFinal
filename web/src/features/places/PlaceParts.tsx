import { Children, type ReactNode } from 'react';
import { Link } from 'react-router';
import { Tooltip } from '@mui/material';
import CallRounded from '@mui/icons-material/CallRounded';
import WhatsApp from '@mui/icons-material/WhatsApp';
import DirectionsRounded from '@mui/icons-material/DirectionsRounded';
import MapOutlined from '@mui/icons-material/MapOutlined';
import NotificationsActiveRounded from '@mui/icons-material/NotificationsActiveRounded';
import { joinHref, telHref, whatsAppHref } from '@/lib/contact';
import type { PlaceDetails } from '@/lib/types';

/* ---------- Actions for a place that isn't on Calling Bell ---------- */

/**
 * Call, WhatsApp, Directions, View on Map and Join Calling Bell, as an even five-column bar on phones and a row of buttons from 640px.
 * Call and WhatsApp stay in place, disabled, when there's no number, so every place lines up the same way. With `compactMid`, between
 * 1024px and 1280px (where the actions share a row with the details) the plain actions show only their icon, named by the tooltip.
 */
export function PlaceActions({ place: p, compactMid, className = '' }: {
  place: { name: string; phone?: string | null; internationalPhone?: string | null; address?: string | null; directionsUrl: string; mapsUrl: string };
  compactMid?: boolean; className?: string;
}) {
  const phone = p.internationalPhone ?? p.phone;
  const whatsApp = whatsAppHref(phone, p.name);
  return (
    <div className={`relative z-[1] grid grid-cols-5 gap-1 sm:flex sm:flex-wrap sm:gap-2 ${className}`} role="group" aria-label={`Actions for ${p.name}`}>
      <RowAction href={phone ? telHref(phone) : undefined} icon={<CallRounded sx={{ fontSize: 18 }} />} label="Call" tone="primary"
        hint={phone ? `Call ${p.phone ?? phone}` : 'No phone number on Google Maps'} />
      <RowAction href={whatsApp ?? undefined} external icon={<WhatsApp sx={{ fontSize: 18, color: whatsApp ? '#25D366' : undefined }} />} label="WhatsApp"
        hint={whatsApp ? 'Chat on WhatsApp' : phone ? 'This number can’t receive WhatsApp' : 'No phone number on Google Maps'} compactMid={compactMid} />
      <RowAction href={p.directionsUrl} external icon={<DirectionsRounded sx={{ fontSize: 18 }} />} label="Directions" hint="Directions in Google Maps" compactMid={compactMid} />
      <RowAction href={p.mapsUrl} external icon={<MapOutlined sx={{ fontSize: 18 }} />} label="View on Map" short="Map" hint="Open in Google Maps" compactMid={compactMid} />
      <RowAction to={joinHref({ name: p.name, phone: p.phone, address: p.address })} icon={<NotificationsActiveRounded sx={{ fontSize: 18 }} />}
        label="Join Calling Bell" short="Join" tone="accent" hint={`Own ${p.name}? List it free on Calling Bell`} />
    </div>
  );
}

const actionTone = {
  primary: 'border-transparent bg-inverse text-on-inverse hover:opacity-90',
  accent: 'border-[color-mix(in_srgb,var(--cb-accent)_55%,transparent)] bg-accent-soft text-ink hover:bg-[color-mix(in_srgb,var(--cb-accent)_28%,transparent)]',
  default: 'border-line bg-surface text-ink-2 hover:bg-subtle hover:text-ink',
} as const;

/** One action as a link: icon over a short label on phones, icon beside the label from 640px. */
function RowAction({ href, to, external, icon, label, short, hint, tone = 'default', compactMid }: {
  href?: string; to?: string; external?: boolean; icon: ReactNode; label: string; short?: string; hint: string;
  tone?: keyof typeof actionTone; compactMid?: boolean;
}) {
  const content = (
    <>
      <span aria-hidden className="flex">{icon}</span>
      <span className={`max-w-full truncate ${compactMid ? 'lg:max-xl:sr-only' : ''}`}>
        {short ? <><span className="sm:hidden">{short}</span><span className="hidden sm:inline">{label}</span></> : label}
      </span>
    </>
  );
  const base = 'inline-flex min-w-0 flex-col items-center justify-center gap-1 rounded-lg border px-0 py-2 text-[10px] font-semibold leading-none tracking-tight '
    + 'transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--cb-accent)] '
    + 'sm:h-9 sm:flex-row sm:gap-1.5 sm:px-3 sm:py-0 sm:text-[13px] sm:tracking-normal lg:max-xl:px-2.5';
  const el = !href && !to ? (
    <span role="link" aria-disabled="true" aria-label={`${label}: ${hint}`} tabIndex={0}
      className={`${base} cursor-not-allowed border-line bg-surface text-faint`}>{content}</span>
  ) : to ? (
    <Link to={to} aria-label={label} className={`${base} ${actionTone[tone]}`}>{content}</Link>
  ) : (
    <a href={href} aria-label={label} className={`${base} ${actionTone[tone]}`} {...(external ? { target: '_blank', rel: 'noreferrer' } : {})}>{content}</a>
  );
  return <Tooltip title={hint}>{el}</Tooltip>;
}

/* ---------- Detail building blocks (the search detail panel and the place page) ---------- */

/** A titled block; left out entirely when none of its rows has a value. */
export function Section({ title, children }: { title: string; children: ReactNode }) {
  const items = Children.toArray(children);
  if (items.length === 0) return null;
  return (
    <section>
      <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">{title}</h4>
      <div className="space-y-2">{items}</div>
    </section>
  );
}

/** One label/value row; nothing at all when the value is missing. */
export function Field({ label, value }: { label: string; value?: ReactNode }) {
  if (value === null || value === undefined || value === '' || value === false) return null;
  return (
    <div className="grid grid-cols-[7.5rem_1fr] gap-3 text-sm">
      <span className="text-muted">{label}</span>
      <span className="min-w-0 break-words text-ink">{value}</span>
    </div>
  );
}

export function ExternalLink({ href, children }: { href: string; children: ReactNode }) {
  return <a href={href} target="_blank" rel="noreferrer nofollow" className="text-info hover:underline">{children}</a>;
}

export function SocialLinks({ links }: { links: { platform: string; url: string }[] }) {
  return (
    <span className="flex flex-wrap gap-x-3 gap-y-1">
      {links.map((l) => <ExternalLink key={l.url} href={l.url}>{l.platform}</ExternalLink>)}
    </span>
  );
}

export function PhotoCredit({ photo, className }: { photo: PlaceDetails['photos'][number]; className?: string }) {
  const author = photo.attributions[0];
  if (!author) return null;
  return (
    <figcaption className={`truncate text-[10px] leading-4 ${className ?? ''}`}>
      Photo: {author.uri ? <a href={author.uri} target="_blank" rel="noreferrer nofollow" className="hover:underline">{author.displayName}</a> : author.displayName}
    </figcaption>
  );
}

/** "example.com/menu" for a short link; just the site for a long one (search links, tracking parameters). */
export function prettyUrl(url: string): string {
  const short = url.replace(/^https?:\/\/(www\.)?/i, '').replace(/\/$/, '');
  if (short.length <= 40) return short;
  try { return new URL(url).hostname.replace(/^www\./i, ''); } catch { return `${short.slice(0, 40)}…`; }
}
export const coordinates = (lat?: number | null, lng?: number | null) => (lat != null && lng != null ? `${lat.toFixed(5)}, ${lng.toFixed(5)}` : null);
