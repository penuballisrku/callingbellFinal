import { useState, type ReactNode } from 'react';
import { Link as RouterLink } from 'react-router';
import {
  Box, Breadcrumbs, Button, Card, CardContent, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, Link, Skeleton, Stack, Tooltip, Typography,
} from '@mui/material';
import StarRounded from '@mui/icons-material/StarRounded';
import TrendingUpRounded from '@mui/icons-material/TrendingUpRounded';
import TrendingDownRounded from '@mui/icons-material/TrendingDownRounded';
import ErrorOutlineRounded from '@mui/icons-material/ErrorOutlineRounded';
import InboxRounded from '@mui/icons-material/InboxRounded';
import VerifiedRounded from '@mui/icons-material/VerifiedRounded';
import { useLookup } from '@/lib/hooks';
import { ago, initials, moneyShort, number } from '@/lib/format';
import type { Kpi } from '@/lib/types';

/* ---------- Image with graceful fallback (never shows a broken image icon) ---------- */
export function Img({ src, alt, className, fallbackText, rounded = 'rounded-lg', aspect, fit = 'cover', eager }: {
  src?: string | null; alt: string; className?: string; fallbackText?: string; rounded?: string; aspect?: string; fit?: 'cover' | 'contain'; eager?: boolean;
}) {
  const [failed, setFailed] = useState(false);
  const style = aspect ? { aspectRatio: aspect } : undefined;
  if (!src || failed) {
    return (
      <div role="img" aria-label={alt} style={style}
        className={`${className ?? ''} ${rounded} flex items-center justify-center bg-subtle text-muted font-semibold select-none`}>
        <span className="text-[clamp(12px,30%,28px)]">{fallbackText ? initials(fallbackText) : 'CB'}</span>
      </div>
    );
  }
  return (
    <img src={src} alt={alt} loading={eager ? 'eager' : 'lazy'} decoding="async" style={style} onError={() => setFailed(true)}
      className={`${className ?? ''} ${rounded} ${fit === 'cover' ? 'object-cover' : 'object-contain'} bg-subtle`} />
  );
}

/* ---------- Status badges driven by LookupValues ---------- */
export function StatusBadge({ type, code, size = 'sm' }: { type: string; code?: string | null; size?: 'sm' | 'md' }) {
  const lookups = useLookup(type);
  if (!code) return null;
  const item = lookups.find((l) => l.code === code);
  const color = item?.colorHex ?? 'var(--cb-faint)';
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-md font-semibold whitespace-nowrap ${size === 'sm' ? 'px-2 py-0.5 text-xs' : 'px-2.5 py-1 text-sm'}`}
      style={{ backgroundColor: `${color}1F`, color: 'var(--cb-ink)' }} title={item?.description ?? undefined}>
      <span className="h-1.5 w-1.5 rounded-full" style={{ backgroundColor: color }} aria-hidden />
      {item?.name ?? code}
    </span>
  );
}

const unavailable = new Set(['Offline', 'Busy']);
export function AvailabilityBadge({ status, lastSeenOn, compact }: { status: string; lastSeenOn?: string | null; compact?: boolean }) {
  const lookups = useLookup('AvailabilityStatus');
  const item = lookups.find((l) => l.code === status);
  const color = item?.colorHex ?? 'var(--cb-faint)';
  const live = !unavailable.has(status);
  const label = item?.name ?? status;
  return (
    <Tooltip title={status === 'Offline' && lastSeenOn ? `Last seen ${ago(lastSeenOn)}` : (item?.description ?? '')}>
      <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-ink whitespace-nowrap">
        <span className="relative flex h-2 w-2">
          {live && <span className="absolute inline-flex h-full w-full rounded-full opacity-60 motion-safe:animate-ping" style={{ backgroundColor: color }} />}
          <span className="relative inline-flex h-2 w-2 rounded-full" style={{ backgroundColor: color }} />
        </span>
        {compact && status === 'Offline' && lastSeenOn ? `Seen ${ago(lastSeenOn)}` : label}
      </span>
    </Tooltip>
  );
}

export function Rating({ value, count, size = 'sm' }: { value: number; count?: number; size?: 'sm' | 'md' }) {
  if (!count) return <span className="text-xs text-muted">New listing</span>;
  return (
    <span className={`inline-flex items-center gap-1 ${size === 'md' ? 'text-base' : 'text-sm'}`}>
      <span className="inline-flex items-center gap-0.5 rounded-md bg-[#0B7A4B] px-1.5 py-0.5 text-xs font-bold text-white">
        {value.toFixed(1)} <StarRounded sx={{ fontSize: 13 }} />
      </span>
      {count !== undefined && <span className="text-muted">({number(count)})</span>}
    </span>
  );
}

export function Stars({ value, size = 16 }: { value: number; size?: number }) {
  return (
    <span className="inline-flex" aria-label={`${value} out of 5 stars`}>
      {[1, 2, 3, 4, 5].map((i) => <StarRounded key={i} sx={{ fontSize: size, color: i <= Math.round(value) ? '#F4A62C' : 'var(--cb-line-strong)' }} />)}
    </span>
  );
}

export const VerifiedMark = () => (
  <Tooltip title="Documents verified by Calling Bell"><VerifiedRounded sx={{ fontSize: 18, color: '#2E90FA' }} aria-label="Verified" /></Tooltip>
);

/* ---------- Page scaffolding ---------- */
export function PageHeader({ title, subtitle, actions, crumbs }: {
  title: string; subtitle?: ReactNode; actions?: ReactNode; crumbs?: { label: string; to?: string }[];
}) {
  return (
    <Box className="mb-6">
      {crumbs && crumbs.length > 0 && (
        <Breadcrumbs sx={{ fontSize: 13, mb: 1 }}>
          {crumbs.map((c) => c.to
            ? <Link key={c.label} component={RouterLink} to={c.to} underline="hover" color="text.secondary">{c.label}</Link>
            : <Typography key={c.label} fontSize={13} color="text.primary">{c.label}</Typography>)}
        </Breadcrumbs>
      )}
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} justifyContent="space-between" alignItems={{ xs: 'flex-start', sm: 'center' }}>
        <div>
          <Typography variant="h4" component="h1">{title}</Typography>
          {subtitle && <Typography color="text.secondary" mt={0.5}>{subtitle}</Typography>}
        </div>
        {actions && <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>{actions}</Stack>}
      </Stack>
    </Box>
  );
}

export function SectionHeader({ title, subtitle, action }: { title: string; subtitle?: string; action?: ReactNode }) {
  return (
    <div className="mb-4 flex items-end justify-between gap-4">
      <div>
        <h2 className="text-xl font-bold tracking-tight md:text-2xl">{title}</h2>
        {subtitle && <p className="mt-1 text-sm text-muted md:text-base">{subtitle}</p>}
      </div>
      {action}
    </div>
  );
}

export function EmptyState({ title, message, action, icon }: { title: string; message?: string; action?: ReactNode; icon?: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center px-6 py-14 text-center">
      <div className="mb-3 flex h-12 w-12 items-center justify-center rounded-full bg-accent-soft text-accent-ink">{icon ?? <InboxRounded />}</div>
      <h3 className="text-base font-semibold">{title}</h3>
      {message && <p className="mt-1 max-w-md text-sm text-muted">{message}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}

export function ErrorState({ message, onRetry }: { message?: string; onRetry?: () => void }) {
  return (
    <EmptyState icon={<ErrorOutlineRounded />} title="We couldn't load this"
      message={message ?? 'Please check your connection and try again.'}
      action={onRetry && <Button variant="outlined" onClick={onRetry}>Try again</Button>} />
  );
}

export function ConfirmDialog({ open, title, message, confirmLabel = 'Confirm', destructive, loading, onConfirm, onClose, children }: {
  open: boolean; title: string; message?: string; confirmLabel?: string; destructive?: boolean; loading?: boolean;
  onConfirm: () => void; onClose: () => void; children?: ReactNode;
}) {
  return (
    <Dialog open={open} onClose={loading ? undefined : onClose} maxWidth="xs" fullWidth>
      <DialogTitle fontWeight={700}>{title}</DialogTitle>
      <DialogContent>
        {message && <DialogContentText>{message}</DialogContentText>}
        {children}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2.5 }}>
        <Button onClick={onClose} disabled={loading} color="inherit">Cancel</Button>
        <Button variant="contained" color={destructive ? 'error' : 'primary'} onClick={onConfirm} disabled={loading}>
          {loading ? 'Please wait…' : confirmLabel}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/* ---------- KPI tiles ---------- */
export function formatKpi(k: Pick<Kpi, 'value' | 'format'>) {
  switch (k.format) {
    case 'currency': return moneyShort(k.value);
    case 'percent': return `${k.value.toFixed(1)}%`;
    case 'rating': return k.value.toFixed(2);
    default: return number(k.value);
  }
}

export function KpiCard({ kpi, hint }: { kpi: Kpi; hint?: string }) {
  const change = kpi.changePercent;
  const up = (change ?? 0) >= 0;
  return (
    <Card>
      <CardContent sx={{ p: 2.25, '&:last-child': { pb: 2.25 } }}>
        <Typography variant="body2" color="text.secondary" fontWeight={500} noWrap>{kpi.label}</Typography>
        <Typography fontSize={26} fontWeight={700} letterSpacing="-0.02em" mt={0.5}>{formatKpi(kpi)}</Typography>
        <div className="mt-1 flex h-5 items-center gap-1 text-xs">
          {change !== null && change !== undefined ? (
            <>
              <span className={`inline-flex items-center gap-0.5 font-semibold ${up ? 'text-success' : 'text-danger'}`}>
                {up ? <TrendingUpRounded sx={{ fontSize: 16 }} /> : <TrendingDownRounded sx={{ fontSize: 16 }} />}
                {up ? '+' : ''}{change.toFixed(1)}%
              </span>
              <span className="hidden truncate text-muted sm:inline">{hint ?? 'vs previous 30 days'}</span>
            </>
          ) : <span className="text-muted">{hint ?? ''}</span>}
        </div>
      </CardContent>
    </Card>
  );
}

export function KpiSkeletons({ count = 4 }: { count?: number }) {
  return <>{Array.from({ length: count }, (_, i) => <Skeleton key={i} variant="rounded" height={112} />)}</>;
}

export function Panel({ title, subtitle, action, children, className, noPad }: {
  title?: string; subtitle?: string; action?: ReactNode; children: ReactNode; className?: string; noPad?: boolean;
}) {
  return (
    <section className={`card min-w-0 ${className ?? ''}`}>
      {title && (
        <div className="flex items-start justify-between gap-3 border-b border-line px-5 py-4">
          <div>
            <h3 className="text-[15px] font-semibold">{title}</h3>
            {subtitle && <p className="mt-0.5 text-xs text-muted">{subtitle}</p>}
          </div>
          {action}
        </div>
      )}
      <div className={noPad ? '' : 'p-5'}>{children}</div>
    </section>
  );
}
