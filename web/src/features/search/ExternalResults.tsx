import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button, Skeleton, Tooltip } from '@mui/material';
import AutoAwesomeRounded from '@mui/icons-material/AutoAwesomeRounded';
import MapRounded from '@mui/icons-material/MapRounded';
import VerifiedRounded from '@mui/icons-material/VerifiedRounded';
import DirectionsRounded from '@mui/icons-material/DirectionsRounded';
import CallRounded from '@mui/icons-material/CallRounded';
import LanguageRounded from '@mui/icons-material/LanguageRounded';
import ScheduleRounded from '@mui/icons-material/ScheduleRounded';
import PlaceRounded from '@mui/icons-material/PlaceRounded';
import InfoOutlined from '@mui/icons-material/InfoOutlined';
import { api } from '@/lib/api';
import { initials, number } from '@/lib/format';
import type { ExternalPlace, ExternalSearch, ExternalTier } from '@/lib/types';
import { Rating } from '@/components/ui';

const PAGE = 8;

export interface ExternalSearchParams { q?: string | null; category?: string | null; sub?: string | null; city?: string | null; areaId?: string | null }

/**
 * Results beyond the platform for the current search: AI-recommended real places from OpenStreetMap, then Google Maps businesses.
 * Polls while the local AI is still picking places.
 */
export function useExternalSearch(p: ExternalSearchParams) {
  const enabled = !!(p.q?.trim() || p.category || p.sub);
  return useQuery({
    queryKey: ['external-search', p.q ?? '', p.category ?? '', p.sub ?? '', p.city ?? '', p.areaId ?? ''],
    queryFn: () => api.get<ExternalSearch>('/api/geo/external-search', { q: p.q, category: p.category, sub: p.sub, city: p.city, area: p.areaId }),
    enabled,
    staleTime: 300_000,
    retry: 1,
    refetchInterval: (q) => (q.state.data?.ai.aiStatus === 'pending' && q.state.dataUpdateCount < 40 ? 8_000 : false),
  });
}

/** Result count per source, in priority order. */
export function SourceSummary({ dbCount, ext, loading }: { dbCount?: number; ext?: ExternalSearch; loading: boolean }) {
  const tierText = (t?: ExternalTier) =>
    !t ? (loading ? 'Searching…' : '–')
      : t.status === 'off' ? 'Not enabled'
        : t.status === 'unavailable' ? 'Unavailable'
          : t.status === 'skipped' ? '–'
            : number(t.total);
  const pills = [
    { key: 'db', icon: <VerifiedRounded sx={{ fontSize: 16 }} className="text-success" />, label: 'Verified businesses', value: dbCount == null ? '…' : number(dbCount) },
    { key: 'ai', icon: <AutoAwesomeRounded sx={{ fontSize: 16 }} className="text-accent-ink" />, label: 'AI recommended', value: tierText(ext?.ai) },
    { key: 'google', icon: <MapRounded sx={{ fontSize: 16 }} className="text-info" />, label: 'Google Maps', value: tierText(ext?.google) },
  ];
  return (
    <ul className="mb-4 flex flex-wrap gap-2" aria-label="Results by source">
      {pills.map((p) => (
        <li key={p.key} className="inline-flex items-center gap-1.5 rounded-full border border-line bg-surface px-3 py-1 text-[13px]">
          {p.icon}<span className="text-muted">{p.label}</span><span className="font-semibold tabular">{p.value}</span>
        </li>
      ))}
    </ul>
  );
}

/** "AI Recommended Businesses" and "Google Maps Businesses" sections, shown after the registered businesses. */
export function ExternalResults({ ext, loading, isError }: { ext?: ExternalSearch; loading: boolean; isError: boolean }) {
  if (loading) {
    return (
      <section className="mt-8" aria-busy="true">
        <Skeleton width={260} height={28} />
        <div className="mt-3 grid gap-3 md:grid-cols-2">{Array.from({ length: 4 }, (_, i) => <Skeleton key={i} variant="rounded" height={150} />)}</div>
      </section>
    );
  }
  if (isError || !ext) return null;
  const near = ext.placeName ? ` near ${ext.placeName}` : '';
  return (
    <>
      {ext.ai.status !== 'skipped' && (
        <TierSection tier={ext.ai} source="ai" title="AI Recommended Businesses"
          subtitle={ext.ai.aiStatus === 'ranked'
            ? `Real places${near} from OpenStreetMap, picked for “${ext.query}” by our AI assistant. Not registered on Calling Bell.`
            : ext.ai.aiStatus === 'pending'
              ? `Real places${near} from OpenStreetMap, nearest first. Our AI assistant is picking the best matches…`
              : `Real places${near} from OpenStreetMap, nearest first. Not registered on Calling Bell.`}
          attribution={<>Map data © <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer" className="underline">OpenStreetMap contributors</a></>} />
      )}
      {ext.google.status !== 'skipped' && ext.google.status !== 'off' && (
        <TierSection tier={ext.google} source="google" title="Google Maps Businesses"
          subtitle={`Businesses${near} listed on Google Maps. Not registered on Calling Bell.`}
          attribution={<>Results from Google Maps</>} />
      )}
    </>
  );
}

function TierSection({ tier, source, title, subtitle, attribution }: {
  tier: ExternalTier; source: 'ai' | 'google'; title: string; subtitle: string; attribution: React.ReactNode;
}) {
  const [shown, setShown] = useState(PAGE);
  const Icon = source === 'ai' ? AutoAwesomeRounded : MapRounded;
  return (
    <section className="mt-8" aria-labelledby={`ext-${source}`}>
      <div className="mb-3 flex items-start gap-3">
        <span aria-hidden className={`grid h-9 w-9 shrink-0 place-items-center rounded-xl ${source === 'ai' ? 'bg-accent-soft text-accent-ink' : 'bg-subtle text-info'}`}>
          <Icon sx={{ fontSize: 20 }} />
        </span>
        <div className="min-w-0">
          <h2 id={`ext-${source}`} className="text-lg font-semibold">
            {title} <span className="ml-1 text-sm font-medium text-muted tabular">{tier.status === 'ready' ? number(tier.total) : ''}</span>
          </h2>
          <p className="text-sm text-muted">{subtitle}</p>
        </div>
      </div>

      {tier.status === 'unavailable' ? (
        <div className="card flex items-center gap-2 p-4 text-sm text-muted"><InfoOutlined fontSize="small" />This source isn’t responding right now. Please try again in a few minutes.</div>
      ) : tier.items.length === 0 ? (
        <div className="card p-4 text-sm text-muted">No matching places found nearby.</div>
      ) : (
        <>
          <ul className="grid gap-3 md:grid-cols-2">
            {tier.items.slice(0, shown).map((p) => <li key={p.id} className="min-w-0"><PlaceCard p={p} source={source} /></li>)}
          </ul>
          {tier.items.length > shown && (
            <div className="mt-4 flex justify-center">
              <Button variant="outlined" onClick={() => setShown((n) => n + PAGE)}>Show more ({number(tier.items.length - shown)})</Button>
            </div>
          )}
        </>
      )}
      <p className="mt-3 text-xs text-faint">
        {attribution}{tier.duplicates > 0 && ` · ${number(tier.duplicates)} already listed above ${tier.duplicates === 1 ? 'was' : 'were'} left out`}
      </p>
    </section>
  );
}

function PlaceCard({ p, source }: { p: ExternalPlace; source: 'ai' | 'google' }) {
  const hours = p.openingHours?.replace(/;\s*/g, ' · ');
  return (
    <article className="card flex h-full gap-3 p-4">
      <span aria-hidden className="grid h-12 w-12 shrink-0 place-items-center rounded-xl bg-subtle text-sm font-bold text-muted">{initials(p.name)}</span>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <h3 className="min-w-0 truncate text-[15px] font-semibold">{p.name}</h3>
          <span className={`inline-flex shrink-0 items-center gap-0.5 rounded px-1.5 py-0.5 text-[11px] font-semibold ${source === 'ai' ? 'bg-accent-soft text-accent-ink' : 'bg-subtle text-info'}`}>
            {source === 'ai' ? <><AutoAwesomeRounded sx={{ fontSize: 12 }} />AI Recommended</> : <><MapRounded sx={{ fontSize: 12 }} />Google Maps</>}
          </span>
        </div>
        <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-[13px] text-muted">
          {p.kind && <span>{p.kind}</span>}
          <span className="inline-flex items-center gap-0.5"><PlaceRounded sx={{ fontSize: 15 }} />{p.distanceKm.toFixed(1)} km away</span>
          {p.rating != null && <Rating value={p.rating} count={p.ratingCount ?? undefined} />}
        </div>
        {p.address && <p className="mt-1 line-clamp-2 text-[13px] text-ink-2">{p.address}</p>}
        {hours && (
          <Tooltip title={hours}>
            <p className="mt-1 inline-flex max-w-full items-center gap-1 text-xs text-muted"><ScheduleRounded sx={{ fontSize: 14 }} /><span className="truncate">{hours}</span></p>
          </Tooltip>
        )}
        <div className="mt-3 flex flex-wrap gap-2">
          <Button size="small" variant="contained" startIcon={<DirectionsRounded />} href={p.directionsUrl} target="_blank" rel="noreferrer">Directions</Button>
          {p.phone && <Button size="small" variant="outlined" startIcon={<CallRounded />} href={`tel:${p.phone.replace(/[^\d+]/g, '')}`}>Call</Button>}
          {p.website && <Button size="small" variant="outlined" startIcon={<LanguageRounded />} href={p.website} target="_blank" rel="noreferrer nofollow">Website</Button>}
          {p.sourceUrl && (
            <Button size="small" href={p.sourceUrl} target="_blank" rel="noreferrer">{source === 'ai' ? 'View on map' : 'Open in Google Maps'}</Button>
          )}
        </div>
      </div>
    </article>
  );
}
