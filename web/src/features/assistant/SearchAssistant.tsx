import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router';
import { useShallow } from 'zustand/react/shallow';
import { Alert, Button, Chip, CircularProgress, Drawer, IconButton, LinearProgress, Skeleton, Tooltip } from '@mui/material';
import MyLocationRounded from '@mui/icons-material/MyLocationRounded';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import AutoAwesomeRounded from '@mui/icons-material/AutoAwesomeRounded';
import CloseRounded from '@mui/icons-material/CloseRounded';
import SendRounded from '@mui/icons-material/SendRounded';
import RestartAltRounded from '@mui/icons-material/RestartAltRounded';
import ArrowForwardRounded from '@mui/icons-material/ArrowForwardRounded';
import MapOutlined from '@mui/icons-material/MapOutlined';
import { api, errorMessage } from '@/lib/api';
import type { AssistantFilters, AssistantReply, AssistantStarters, SearchSuggestion } from '@/lib/types';
import { useDeviceLocation } from '@/lib/deviceLocation';
import { SearchSuggest, selectSuggestedPlace } from '@/components/SearchSuggest';
import { CitySelect } from '@/components/LocationPicker';
import { BusinessCard } from '@/components/BusinessCard';
import { useCity } from '@/stores/city';
import { CompactPlace, useExternalSearch } from '@/features/search/ExternalResults';
import { useAssistant, type AssistantRequest, type Turn } from './store';
import { VoiceSearchButton } from '@/components/VoiceSearch';
import { GoogleMapsDataCredit } from '@/components/GoogleMapsAttribution';

/** While the local AI is still interpreting a request, the answer is refreshed this often, this many times. */
const POLL_MS = 5000;
const MAX_POLLS = 36;

/** The search page URL for the assistant's filters (the same parameters the search page's own filters use). */
export function searchHref(f: AssistantFilters) {
  const p = new URLSearchParams();
  const set = (k: string, v: string | number | null | undefined | boolean) => { if (v !== null && v !== undefined && v !== '' && v !== false) p.set(k, String(v)); };
  set('q', f.q); set('category', f.category); set('sub', f.sub); set('city', f.city); set('area', f.areaId); set('minRating', f.minRating);
  set('availability', f.availability); set('openNow', f.openNow); set('verifiedOnly', f.verifiedOnly); set('homeService', f.homeService);
  set('video', f.videoConsultation); set('onlineBooking', f.onlineBooking); if (f.sort && f.sort !== 'relevance') set('sort', f.sort);
  return `/search?${p.toString()}`;
}

/** The filters without the one a chip stood for. */
function withoutChip(f: AssistantFilters, key: keyof AssistantFilters): AssistantFilters {
  switch (key) {
    case 'openNow': case 'verifiedOnly': case 'homeService': case 'videoConsultation': case 'onlineBooking': return { ...f, [key]: false };
    case 'city': return { ...f, city: null, areaId: null };
    case 'sort': return { ...f, sort: null, minRating: f.sort === 'rating' ? null : f.minRating };
    default: return { ...f, [key]: null };
  }
}

/** Accent-tinted look that sets the AI entry point apart from the neutral header controls. */
const askAiTint = {
  bgcolor: 'var(--cb-accent-soft)', color: 'var(--cb-accent-ink)',
  borderColor: 'color-mix(in srgb, var(--cb-accent) 45%, transparent)',
  '&:hover': { bgcolor: 'var(--cb-accent-soft)', borderColor: 'var(--cb-accent)' },
};

/**
 * "Ask AI" button for headers and toolbars. Animated (index.css, .cb-ai-*): a gentle glow and sparkle twinkle every few seconds, a tilt
 * on hover and focus, a press, and a turning sparkle while the assistant is working on an answer; still for prefers-reduced-motion.
 */
export function AskAiButton({ compact }: { compact?: boolean }) {
  const openAssistant = useAssistant((s) => s.openAssistant);
  const busy = useAssistant((s) => s.turns.some((t) => t.loading));
  const label = busy ? 'Open AI search assistant (searching…)' : 'Open AI search assistant';
  if (compact) {
    return (
      <Tooltip title="AI search assistant">
        <IconButton onClick={() => openAssistant()} aria-label={label} aria-busy={busy} data-busy={busy} className="cb-ai-btn"
          sx={{ ...askAiTint, height: 40, width: 40, border: '1px solid' }}>
          <span className="cb-ai-spark"><AutoAwesomeRounded sx={{ fontSize: 19 }} /></span>
        </IconButton>
      </Tooltip>
    );
  }
  return (
    <button type="button" onClick={() => openAssistant()} aria-label={label} aria-busy={busy} data-busy={busy}
      className="cb-ai-btn inline-flex h-10 items-center gap-2 whitespace-nowrap rounded-full border border-[color-mix(in_srgb,var(--cb-accent)_45%,transparent)] bg-accent-soft py-1 pl-1 pr-3.5 text-sm font-semibold text-accent-ink hover:border-accent focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-[var(--cb-ring)]">
      <span className="grid h-7 w-7 place-items-center rounded-full bg-accent text-on-accent">
        <span className="cb-ai-spark"><AutoAwesomeRounded sx={{ fontSize: 16 }} /></span>
      </span>
      Ask AI
    </button>
  );
}

const placeSource: Record<string, string> = {
  device: 'from your device', ip: 'approximate, from your internet connection', user: 'chosen by you',
};

/**
 * Where the assistant searches, and how that was found. When the assistant opens, the device location is used straight away if the
 * browser already allows it; otherwise the visitor can share it with one click (the browser asks), with a note on why. Until then, or
 * if they decline, the existing location (their choice, else the IP's) is used, and it can always be changed here.
 */
function AssistantLocation({ open }: { open: boolean }) {
  const { citySlug, source } = useCity(useShallow((s) => ({ citySlug: s.citySlug, source: s.source })));
  const device = useDeviceLocation({ auto: open });
  const canAsk = (device.status === 'prompt' || device.status === 'unavailable') && source !== 'device';
  const note = device.status === 'locating' ? 'Finding your location…'
    : device.status === 'denied' && source !== 'user' ? 'Location access is off, so this is approximate. Change it any time.'
      : device.status === 'unavailable' && source !== 'device' ? "We couldn't get your device's location (location services may be off), so this is approximate."
        : device.status === 'outside' ? "You seem to be outside the cities we list. Choose where to search."
          : canAsk && source === 'ip' ? 'Approximate, from your internet connection. Share your location to find businesses in your area; it only picks your area and isn’t stored.'
            : citySlug && source && placeSource[source] ? `Location ${placeSource[source]}.` : 'Choose where to search.';

  return (
    <div className="cb-ai-enter border-b border-line bg-surface px-4 py-2.5">
      <div className="flex items-center gap-2">
        <div className="min-w-0 flex-1"><CitySelect fullWidth /></div>
        {device.status === 'locating' ? <CircularProgress size={18} aria-label="Finding your location" sx={{ flexShrink: 0, mx: 1 }} />
          : canAsk && (
            <Tooltip title="Use your device's location for results near you" describeChild>
              <Button size="small" variant="outlined" onClick={() => void device.locate()} startIcon={<MyLocationRounded sx={{ fontSize: 16 }} />}
                sx={{ flexShrink: 0, minHeight: 36, whiteSpace: 'nowrap' }}>
                Near me
              </Button>
            </Tooltip>
          )}
      </div>
      <p className="mt-1 flex items-start gap-1 text-[11px] leading-4 text-faint" role="status">
        <PlaceOutlined sx={{ fontSize: 13, mt: '1px' }} aria-hidden />{note}
      </p>
    </div>
  );
}

/** The AI search assistant: a side panel (full screen on phones) where visitors describe what they need in their own words. */
export function SearchAssistant() {
  const { open, turns, close, reset, add, update, takeQueued } = useAssistant();
  const navigate = useNavigate();
  const citySlug = useCity((s) => s.citySlug);
  const areaId = useCity((s) => s.areaId);
  const countryCode = useCity((s) => s.countryCode);
  const [text, setText] = useState('');
  const listRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const busy = turns.some((t) => t.loading);
  // The filters of the latest answer: follow-up messages refine them.
  const context = [...turns].reverse().find((t) => t.reply)?.reply?.filters ?? null;

  const run = async (turnId: number, request: AssistantRequest, polls = 0) => {
    try {
      const { data } = await api.post<AssistantReply>('/api/search/assistant', request);
      update(turnId, { reply: data, loading: false, error: undefined, polls });
    } catch (e) {
      // A failed refresh keeps the answer already shown.
      update(turnId, polls > 0 ? { polls: MAX_POLLS } : { error: errorMessage(e), loading: false });
    }
  };

  const ask = (message: string | null, filters: AssistantFilters | null, label?: string) => {
    const shown = message ?? label;
    if (shown) add({ role: 'user', text: shown });
    const request: AssistantRequest = { message, context: filters, city: citySlug, areaId };
    const id = add({ role: 'assistant', request, loading: true });
    void run(id, request);
  };

  const send = (value = text) => {
    const message = value.trim();
    if (!message || busy) return;
    setText('');
    ask(message, context);
  };

  /**
   * A suggestion from the box: a business opens; a place becomes where to search (the box is then ready for what); anything else is
   * asked as the next message, so the assistant answers it with live results as if typed. Choosing never also submits the form.
   */
  const pickSuggestion = (s: SearchSuggestion) => {
    if (s.kind === 'Business') { close(); navigate(`/business/${s.slug}`); return; }
    if (s.kind === 'Location') { selectSuggestedPlace(s); setText(''); inputRef.current?.focus(); return; }
    // "AC Repair in Kukatpally" names its place, which the assistant reads from the message.
    send(s.kind === 'Popular' ? s.query ?? s.label : s.label);
  };

  // A prompt handed over when opening (e.g. from the home page) is sent right away.
  useEffect(() => {
    if (!open) return;
    const queued = takeQueued();
    if (queued) ask(queued, context);
    const t = setTimeout(() => inputRef.current?.focus(), 150);
    return () => clearTimeout(t);
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  // Refresh an answer while the AI is still working out what the request means.
  const pending = turns.find((t) => t.reply?.aiPending && (t.polls ?? 0) < MAX_POLLS && t.request);
  useEffect(() => {
    if (!pending) return;
    const t = setTimeout(() => void run(pending.id, pending.request!, (pending.polls ?? 0) + 1), POLL_MS);
    return () => clearTimeout(t);
  }, [pending?.id, pending?.polls]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    listRef.current?.scrollTo({ top: listRef.current.scrollHeight, behavior: 'smooth' });
  }, [turns.length, turns[turns.length - 1]?.loading]);

  return (
    <Drawer anchor="right" open={open} onClose={close} aria-labelledby="assistant-title"
      slotProps={{ paper: { sx: { width: { xs: '100%', sm: 440 }, display: 'flex', flexDirection: 'column', bgcolor: 'var(--cb-canvas)' } } }}>
      <header className="flex items-center gap-3 border-b border-line bg-surface px-4 py-3">
        <span className="cb-ai-enter grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-accent-soft">
          <span className={busy ? 'cb-ai-spinning inline-grid' : 'inline-grid'}><AutoAwesomeRounded className="text-accent-ink" sx={{ fontSize: 20 }} /></span>
        </span>
        <div className="min-w-0 flex-1">
          <h2 id="assistant-title" className="text-[15px] font-semibold leading-tight">AI search assistant</h2>
          <p className="truncate text-xs text-muted">Describe what you need in your own words</p>
        </div>
        {turns.length > 0 && (
          <Tooltip title="New conversation"><IconButton onClick={reset} disabled={busy} aria-label="Start a new conversation"><RestartAltRounded /></IconButton></Tooltip>
        )}
        <IconButton onClick={close} aria-label="Close assistant" edge="end"><CloseRounded /></IconButton>
      </header>
      <AssistantLocation open={open} />

      <div ref={listRef} className="flex-1 space-y-4 overflow-y-auto px-4 py-4" aria-live="polite">
        {turns.length === 0 ? <Welcome onPick={(p) => send(p)} /> : turns.map((t) => (
          t.role === 'user'
            ? <div key={t.id} className="flex justify-end"><p className="max-w-[85%] rounded-2xl rounded-br-md bg-navy px-3.5 py-2 text-sm text-white">{t.text}</p></div>
            : <AnswerView key={t.id} turn={t} onClose={close} disabled={busy}
                onSuggest={(s) => (s === 'Start over' ? reset() : send(s))}
                onRemoveChip={(key, label) => t.reply && ask(null, withoutChip(t.reply.filters, key), `Remove “${label}”`)}
                onRetry={() => { if (t.request) { update(t.id, { loading: true, error: undefined }); void run(t.id, t.request); } }} />
        ))}
      </div>

      <form className="border-t border-line bg-surface p-3" onSubmit={(e) => { e.preventDefault(); send(); }}>
        <div className="flex items-end gap-2">
          {/* Suggestions open upwards (the box is at the bottom of the panel). Enter on a highlighted suggestion picks it; otherwise it sends. */}
          <SearchSuggest value={text} onChange={(v) => setText(v.slice(0, 300))} citySlug={citySlug} areaId={areaId} country={countryCode}
            onSelect={pickSuggestion} placement="above" clearable maxLength={300} inputRef={inputRef} className="min-w-0 flex-1"
            placeholder="e.g. AC not cooling, need someone today" ariaLabel="Message the AI search assistant"
            inputClassName="h-10 w-full rounded-lg border border-line bg-surface pl-3 pr-9 text-sm text-ink outline-none transition-colors placeholder:text-faint focus:border-accent" />
          <VoiceSearchButton onText={(t) => setText(t.slice(0, 300))} submit size="medium" label="Speak your request" />
          <IconButton type="submit" disabled={!text.trim() || busy} aria-label="Send" color="primary"
            sx={{ bgcolor: 'var(--cb-navy)', color: '#fff', '&:hover': { bgcolor: 'var(--cb-navy)' }, '&.Mui-disabled': { bgcolor: 'var(--cb-subtle)' }, height: 40, width: 40 }}>
            <SendRounded fontSize="small" />
          </IconButton>
        </div>
        <p className="mt-1.5 text-[11px] text-faint">CallInBell businesses first, then Google Maps, then OpenStreetMap. AI suggestions can be imperfect.</p>
      </form>
    </Drawer>
  );
}

/** Places an answer shows (the server returns up to this many CallInBell businesses). */
const ANSWER_SIZE = 5;

/**
 * Tops an answer up to {@link ANSWER_SIZE} places, in priority order: CallInBell businesses (shown above), then Google Maps, then
 * OpenStreetMap, the free open source map, which is only searched when Google Maps is off, fails or finds too few. The server leaves out
 * places a higher source already listed.
 */
function NearbyPlaces({ filters, need, listed, onClose }: { filters: AssistantFilters; need: number; listed: number; onClose: () => void }) {
  const { data, isLoading, isError } = useExternalSearch({ q: filters.q, category: filters.category, sub: filters.sub, city: filters.city, areaId: filters.areaId },
    { aiWhenGoogleBelow: need });
  const google = (data?.google.items ?? []).map((p) => ({ p, source: 'google' as const }));
  const osm = (data?.ai.items ?? []).map((p) => ({ p, source: 'ai' as const }));
  const places = [...google, ...osm].slice(0, need);
  const available = google.length + osm.length;
  const stillSearching = google.length < need && (!!data?.ai.searching || data?.ai.aiStatus === 'pending');
  const fromGoogle = google.length > 0;
  const fromOsm = places.some((x) => x.source === 'ai');

  // Below CallInBell's own results, nothing is shown when the map has nothing to add.
  if (listed > 0 && !isLoading && places.length === 0 && !stillSearching) return null;

  return (
    <div className="space-y-2">
      {listed > 0 && <p className="flex items-center gap-1.5 pt-1 text-xs font-semibold uppercase tracking-wide text-muted"><MapOutlined sx={{ fontSize: 15 }} />More places nearby</p>}
      {isLoading ? (
        <>
          {listed === 0 && <p className="flex items-center gap-2 text-xs text-muted" role="status"><MapOutlined sx={{ fontSize: 16 }} />Looking for places nearby…</p>}
          {[0, 1].map((i) => <Skeleton key={i} variant="rounded" height={92} />)}
        </>
      ) : places.length === 0 ? (
        <p className="text-xs text-muted">
          {isError ? "The map search isn't responding right now." : stillSearching ? 'Still searching the map for places nearby…' : 'No places were found on the map nearby either.'}
        </p>
      ) : (
        <>
          <ul className="space-y-2">
            {places.map(({ p, source }) => (
              <li key={`${source}-${p.id}`}>
                <CompactPlace p={p} source={source} join={{ hint: filters.sub ?? filters.category ?? filters.q, onOpen: onClose }} />
              </li>
            ))}
          </ul>
          <p className="flex flex-wrap items-center gap-x-1 text-[11px] text-faint">
            <span>Not listed on CallInBell.</span>
            {fromGoogle && <GoogleMapsDataCredit />}
            {fromOsm && <span>Map data © <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer" className="underline">OpenStreetMap contributors</a></span>}
          </p>
        </>
      )}
      {!isLoading && (
        <Button component={Link} to={searchHref(filters)} onClick={onClose} size="small" startIcon={<MapOutlined />}>
          {available > places.length ? `See all ${available} places nearby` : 'Open the full search'}
        </Button>
      )}
    </div>
  );
}

/** Intro and example prompts, built from the services actually listed in the visitor's city. */
function Welcome({ onPick }: { onPick: (prompt: string) => void }) {
  const citySlug = useCity((s) => s.citySlug);
  const { data, isLoading } = useQuery({
    queryKey: ['assistant-starters', citySlug ?? ''],
    queryFn: () => api.get<AssistantStarters>('/api/search/assistant/starters', { city: citySlug }),
    staleTime: 600_000,
  });

  return (
    <div className="pt-2">
      <p className="text-sm text-ink">Hi! Tell me what you need, or describe the problem, and I'll find local businesses that fit.</p>
      <p className="mt-1 text-sm text-muted">You can mention where, when, your budget, or whether they should visit your home, then refine the results as we go.</p>
      {data?.cityName && !data.cityHasListings && (
        <Alert severity="info" variant="outlined" sx={{ mt: 2 }}>
          No businesses are listed in {data.cityName} yet. You can still search other cities, or ask me and I'll point you to places nearby on the map.
        </Alert>
      )}
      <p className="mt-5 text-xs font-semibold uppercase tracking-wide text-muted">Try asking</p>
      <div className="mt-2 flex flex-col gap-2">
        {isLoading
          ? [0, 1, 2].map((i) => <Skeleton key={i} variant="rounded" height={40} />)
          : (data?.prompts ?? []).map((e) => (
            <button key={e} type="button" onClick={() => onPick(e)}
              className="rounded-xl border border-line bg-surface px-3.5 py-2.5 text-left text-sm transition-colors hover:border-line-strong">
              {e}
            </button>
          ))}
      </div>
    </div>
  );
}

function AnswerView({ turn, disabled, onSuggest, onRemoveChip, onRetry, onClose }: {
  turn: Turn; disabled: boolean; onSuggest: (s: string) => void; onRemoveChip: (key: keyof AssistantFilters, label: string) => void;
  onRetry: () => void; onClose: () => void;
}) {
  const r = turn.reply;
  if (turn.loading && !r) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted" role="status">
        <span className="cb-ai-spinning inline-grid"><AutoAwesomeRounded className="text-accent-ink" sx={{ fontSize: 18 }} /></span> Searching…
      </div>
    );
  }
  if (turn.error || !r) {
    return <Alert severity="error" action={<Button color="inherit" size="small" onClick={onRetry}>Retry</Button>}>{turn.error ?? 'Something went wrong.'}</Alert>;
  }
  const stillThinking = r.aiPending && (turn.polls ?? 0) < MAX_POLLS;

  return (
    <div className="space-y-3">
      <div className="flex gap-2">
        <AutoAwesomeRounded className="mt-0.5 shrink-0 text-accent-ink" sx={{ fontSize: 18 }} aria-hidden />
        <p className="whitespace-pre-line text-sm leading-relaxed text-ink">{r.message}</p>
      </div>
      {stillThinking && (
        <div className="space-y-1">
          <LinearProgress sx={{ borderRadius: 1, height: 3 }} aria-label="AI is working on your request" />
          <p className="text-[11px] text-faint">The AI is working on a fuller answer from live CallInBell data…</p>
        </div>
      )}

      {r.chips.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {r.chips.map((c) => (
            <Chip key={`${c.key}-${c.label}`} label={c.label} size="small" variant="outlined" disabled={disabled}
              onDelete={c.key === 'sub' || c.key === 'category' || c.key === 'q' ? undefined : () => onRemoveChip(c.key, c.label)} />
          ))}
        </div>
      )}

      {r.results.length > 0 && (
        <ul className="space-y-2">
          {r.results.map((b) => <li key={b.id}><BusinessCard b={b} layout="compact" onOpen={onClose} /></li>)}
        </ul>
      )}
      {r.results.length < ANSWER_SIZE && (r.filters.sub || r.filters.category || r.filters.q) && (
        <NearbyPlaces filters={r.filters} need={ANSWER_SIZE - r.results.length} listed={r.results.length} onClose={onClose} />
      )}
      {r.total > r.results.length && (
        <Button component={Link} to={searchHref(r.filters)} onClick={onClose} size="small" endIcon={<ArrowForwardRounded />}>
          See all {r.total.toLocaleString('en-IN')} results
        </Button>
      )}

      {r.suggestions.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {r.suggestions.map((s) => (
            <Chip key={s} label={s} size="small" clickable disabled={disabled} onClick={() => onSuggest(s)}
              sx={{ bgcolor: 'var(--cb-surface)', border: '1px solid var(--cb-line)' }} />
          ))}
        </div>
      )}
    </div>
  );
}
