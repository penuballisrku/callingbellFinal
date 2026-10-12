import { useId, useMemo, useRef, useState, type KeyboardEvent, type ReactNode, type Ref } from 'react';
import { useNavigate } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import SearchRounded from '@mui/icons-material/SearchRounded';
import StarRounded from '@mui/icons-material/StarRounded';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import NearMeOutlined from '@mui/icons-material/NearMeOutlined';
import TrendingUpRounded from '@mui/icons-material/TrendingUpRounded';
import CloseRounded from '@mui/icons-material/CloseRounded';
import { api } from '@/lib/api';
import { useDebounced } from '@/lib/hooks';
import { namesPlace, parsedSearchParams, parseSearch } from '@/lib/searchParse';
import type { SearchSuggestion, SearchSuggestions } from '@/lib/types';
import { useCity } from '@/stores/city';
import { Img } from './ui';

/** Suggestions are only requested once the trimmed text reaches this length (the API enforces the same minimum). */
export const SUGGEST_MIN_CHARS = 2;

/** Default destination for a suggestion. */
export function suggestionHref(s: SearchSuggestion, citySlug?: string | null) {
  const city = citySlug ? `&city=${citySlug}` : '';
  switch (s.kind) {
    case 'Category': return `/categories/${s.slug}`;
    case 'SubCategory': return `/search?sub=${s.slug}${city}`;
    case 'Service': return `/search?sub=${s.subCategorySlug}&q=${encodeURIComponent(s.label)}${city}`;
    case 'Business': return `/business/${s.slug}`;
    case 'Popular': return s.subCategorySlug ? `/search?sub=${s.subCategorySlug}${city}` : `/search?q=${encodeURIComponent(s.query ?? s.label)}${city}`;
    case 'Combined': {
      const p = new URLSearchParams({ sub: s.subCategorySlug ?? s.slug });
      if (s.citySlug) p.set('city', s.citySlug);
      if (s.citySlug && s.areaId) p.set('area', s.areaId);
      return `/search?${p}`;
    }
    case 'Location': {
      const p = new URLSearchParams();
      if (s.citySlug) p.set('city', s.citySlug);
      if (s.areaId) p.set('area', s.areaId);
      return `/search?${p}`;
    }
  }
}

/** Makes a place suggestion the selected location (as the location picker would), so later suggestions and searches are there. */
export function selectSuggestedPlace(s: SearchSuggestion) {
  if (s.kind !== 'Location' || !s.citySlug) return;
  useCity.getState().setCity(s.citySlug, s.areaId ?? null, s.areaId ? { areaName: s.label, areaSlug: s.slug } : { cityName: s.label });
}

/**
 * Search results link for a search box: the category/sub-category/service picked from the suggestions (while the box still shows its
 * name), otherwise the typed text, within the city and area chosen in the location dropdown.
 */
export function searchHref(text: string, picked: SearchSuggestion | null, citySlug?: string | null, areaId?: string | null) {
  const params = new URLSearchParams();
  const q = text.trim();
  if (picked && picked.label === q) {
    if (picked.kind === 'Category') params.set('category', picked.slug);
    else if (picked.kind === 'SubCategory') params.set('sub', picked.slug);
    else if (picked.kind === 'Service') { params.set('sub', picked.subCategorySlug ?? picked.slug); params.set('q', picked.label); }
  } else if (q) params.set('q', q);
  if (citySlug) params.set('city', citySlug);
  if (citySlug && areaId) params.set('area', areaId);
  return `/search?${params}`;
}

/**
 * {@link searchHref}, but typed text that names a place ("lawyers in Nellore") is parsed first: a listed city/area replaces the
 * dropdown's location, and an unlisted place is searched by name. Falls back to the plain link if parsing fails.
 */
export async function resolveSearchHref(text: string, picked: SearchSuggestion | null, citySlug?: string | null, areaId?: string | null) {
  const q = text.trim();
  if ((picked && picked.label === q) || !namesPlace(q)) return searchHref(text, picked, citySlug, areaId);
  try {
    const parsed = await parseSearch(q, citySlug);
    return `/search?${parsedSearchParams(parsed, new URLSearchParams(), { citySlug, areaId })}`;
  } catch {
    return searchHref(text, picked, citySlug, areaId);
  }
}

type GroupKey = keyof Omit<SearchSuggestions, 'query'>;
/** Order of the groups: what (categories), what + where, services, businesses, places, then popular searches. */
const groupTitles: [GroupKey, string][] = [
  ['categories', 'Categories'], ['combined', 'Near you'], ['services', 'Services'], ['businesses', 'Businesses'],
  ['locations', 'Locations'], ['popular', 'Popular searches'],
];

/** Related services by meaning (GET /api/search/suggest/ai) are asked for phrases only, once typing pauses a little longer. */
const AI_MIN_CHARS = 8;
const AI_DEBOUNCE_MS = 600;
/** Pause in typing before suggestions are asked for. */
const DEBOUNCE_MS = 280;
const kindLabel: Record<SearchSuggestion['kind'], string> = {
  Category: 'Category', SubCategory: 'Category', Service: 'Service', Business: 'Business', Location: 'Place', Popular: 'Popular', Combined: 'Near you',
};

/** Bolds the first case-insensitive occurrence of the typed text. */
function Highlight({ text, term }: { text: string; term: string }) {
  const i = text.toLowerCase().indexOf(term.toLowerCase());
  if (!term || i < 0) return <>{text}</>;
  return <>{text.slice(0, i)}<mark className="bg-transparent font-semibold text-ink">{text.slice(i, i + term.length)}</mark>{text.slice(i + term.length)}</>;
}

function SuggestionIcon({ s }: { s: SearchSuggestion }) {
  if (s.kind === 'Business') return <Img src={s.imageUrl} alt="" className="h-8 w-8 shrink-0" rounded="rounded-lg" fallbackText={s.label} />;
  const box = 'grid h-8 w-8 shrink-0 place-items-center rounded-lg';
  if (s.kind === 'Location') return <span className={`${box} bg-subtle text-muted`}><PlaceOutlined sx={{ fontSize: 19 }} /></span>;
  if (s.kind === 'Combined') return <span className={`${box} bg-accent-soft text-accent-ink`}><NearMeOutlined sx={{ fontSize: 18 }} /></span>;
  if (s.kind === 'Popular' && !s.imageUrl) return <span className={`${box} bg-subtle text-muted`}><TrendingUpRounded sx={{ fontSize: 19 }} /></span>;
  return (
    <span className={`${box} bg-subtle`}>
      <Img src={s.imageUrl} alt="" className="h-5 w-5" rounded="rounded" fit="contain" fallbackText={s.label} />
    </span>
  );
}

/**
 * Search input with autocomplete ("intellisense"). Renders the <input> plus a suggestion list that opens once
 * {@link SUGGEST_MIN_CHARS} characters are typed: categories, services and businesses, places (cities, areas, PIN codes), popular
 * searches, and the best match in the selected area and city ("AC Repair in Kukatpally", with real business counts). Fully keyboard
 * operable (↑ ↓ Enter Esc) using the ARIA combobox pattern. Place it inside the caller's <form>: pressing Enter with no highlighted
 * suggestion submits that form as before.
 *
 * Suggestions follow the selected location: they are cached per text and place, a newer answer always replaces an older one, a request
 * for text the visitor has moved on from is cancelled, and a previous place's suggestions are never shown for a new one.
 */
export function SearchSuggest({ value, onChange, citySlug, areaId, country, onSelect, inputClassName, placeholder, ariaLabel, className,
  panelClassName, placement = 'below', clearable = false, maxLength, inputRef: externalRef, children }: {
  value: string;
  onChange: (value: string) => void;
  citySlug?: string | null;
  /** The selected area within {@link citySlug}: businesses and suggestions there come first. */
  areaId?: string | null;
  /** The visitor's country (ISO code), for places and popular searches. */
  country?: string | null;
  /** Override what happens when a suggestion is chosen; defaults to navigating to {@link suggestionHref} (a place is selected instead). */
  onSelect?: (s: SearchSuggestion) => void;
  inputClassName?: string;
  placeholder?: string;
  ariaLabel: string;
  className?: string;
  panelClassName?: string;
  /** "above" opens the list upwards, for a box at the bottom of a panel. */
  placement?: 'below' | 'above';
  /** Shows a button that clears the text (the input needs room on its right: pr-9). */
  clearable?: boolean;
  maxLength?: number;
  inputRef?: Ref<HTMLInputElement>;
  children?: ReactNode;
}) {
  const navigate = useNavigate();
  const id = useId();
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);

  const trimmed = value.trim();
  const term = useDebounced(trimmed, DEBOUNCE_MS);
  const ready = term.length >= SUGGEST_MIN_CHARS;
  const where = [citySlug ?? null, areaId ?? null, country ?? null] as const;
  const { data, isFetching, isError, refetch } = useQuery({
    queryKey: ['search-suggest', term.toLowerCase(), ...where],
    // The signal cancels the request when the text or place changes before it answers.
    queryFn: ({ signal }) => api.get<SearchSuggestions>('/api/search/suggest',
      { q: term, city: citySlug ?? undefined, area: citySlug ? areaId ?? undefined : undefined, country: country ?? undefined }, signal),
    enabled: open && ready,
    staleTime: 60_000,
    // While the next answer loads, the previous one stays only if it was for the same place.
    placeholderData: (previous, previousQuery) =>
      previousQuery && where.every((w, i) => previousQuery.queryKey[i + 2] === w) ? previous : undefined,
  });

  // Categories the local AI matches by meaning ("water dripping from ceiling" → Plumbers). Separate from the name-based list so typing
  // never waits for the model; the group appears at the top when it arrives.
  const aiTerm = useDebounced(trimmed, AI_DEBOUNCE_MS);
  const { data: aiData } = useQuery({
    queryKey: ['search-suggest-ai', aiTerm.toLowerCase()],
    queryFn: ({ signal }) => api.get<SearchSuggestion[]>('/api/search/suggest/ai', { q: aiTerm }, signal),
    enabled: open && aiTerm.length >= AI_MIN_CHARS && aiTerm.includes(' '),
    staleTime: 300_000,
  });

  const groups = useMemo(() => {
    const byName = data ? groupTitles.map(([key, title]) => ({ title, items: data[key] ?? [] })).filter((g) => g.items.length) : [];
    // The same suggestion never shows twice.
    const seen = new Set<string>();
    const unique = byName.map((g) => ({ ...g, items: g.items.filter((s) => {
      const key = `${s.kind}|${s.label.toLowerCase()}|${s.citySlug ?? ''}|${s.areaId ?? ''}`;
      if (seen.has(key)) return false;
      seen.add(key);
      return true;
    }) })).filter((g) => g.items.length);
    const listed = new Set(unique.flatMap((g) => g.items).map((s) => s.slug));
    const ai = aiTerm === trimmed ? (aiData ?? []).filter((s) => !listed.has(s.slug)) : [];
    return ai.length ? [{ title: 'Related services', items: ai }, ...unique] : unique;
  }, [data, aiData, aiTerm, trimmed]);
  const flat = useMemo(() => groups.flatMap((g) => g.items), [groups]);
  const showPanel = open && trimmed.length >= SUGGEST_MIN_CHARS;
  const waiting = showPanel && (!data || term !== trimmed) && flat.length === 0;
  const listId = `${id}-list`;
  const optionId = (i: number) => `${id}-opt-${i}`;

  const choose = (s: SearchSuggestion) => {
    setOpen(false);
    setActive(-1);
    if (onSelect) onSelect(s);
    else if (s.kind === 'Location') {
      // A place changes where to search, not what: the text typed to find it is cleared, ready for the service.
      selectSuggestedPlace(s);
      onChange('');
      inputRef.current?.focus();
      return;
    } else navigate(suggestionHref(s, citySlug));
    inputRef.current?.blur();
  };

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    // Escape closes the list only; a surrounding dialog (the assistant panel) stays open until Escape is pressed again.
    if (e.key === 'Escape') { if (showPanel) { e.preventDefault(); e.stopPropagation(); setOpen(false); setActive(-1); } return; }
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      if (!showPanel) { setOpen(true); return; }
      if (!flat.length) return;
      e.preventDefault();
      setActive((i) => (e.key === 'ArrowDown' ? (i + 1) % flat.length : (i <= 0 ? flat.length - 1 : i - 1)));
      return;
    }
    if (e.key === 'Enter' && showPanel && active >= 0 && flat[active]) { e.preventDefault(); choose(flat[active]); return; }
    if (e.key === 'Enter') { setOpen(false); setActive(-1); } // let the surrounding form submit the free text
  };

  const setRefs = (el: HTMLInputElement | null) => {
    inputRef.current = el;
    if (typeof externalRef === 'function') externalRef(el);
    else if (externalRef) (externalRef as { current: HTMLInputElement | null }).current = el;
  };

  let index = -1;
  return (
    <div className={`relative ${className ?? ''}`}>
      <input ref={setRefs} value={value} placeholder={placeholder} aria-label={ariaLabel} autoComplete="off" spellCheck={false} maxLength={maxLength}
        role="combobox" aria-autocomplete="list" aria-expanded={showPanel} aria-controls={listId}
        aria-activedescendant={showPanel && active >= 0 ? optionId(active) : undefined}
        onChange={(e) => { onChange(e.target.value); setOpen(true); setActive(-1); }}
        onFocus={() => setOpen(true)} onBlur={() => { setOpen(false); setActive(-1); }} onKeyDown={onKeyDown}
        className={inputClassName} />
      {clearable && value && (
        <button type="button" aria-label="Clear search" onMouseDown={(e) => e.preventDefault()}
          onClick={() => { onChange(''); setActive(-1); inputRef.current?.focus(); }}
          className="absolute right-2 top-1/2 grid h-7 w-7 -translate-y-1/2 place-items-center rounded-full text-muted transition-colors hover:bg-subtle hover:text-ink">
          <CloseRounded sx={{ fontSize: 17 }} />
        </button>
      )}
      {children}

      {showPanel && (
        <div className={`absolute left-0 right-0 z-50 overflow-hidden rounded-xl border border-line bg-surface text-left text-ink shadow-[var(--cb-shadow-lg)] ${
          placement === 'above' ? 'bottom-full mb-2' : 'top-full mt-2'} ${panelClassName ?? ''}`}
          // Keep focus in the input while clicking inside the panel so the click lands before blur closes it.
          onMouseDown={(e) => e.preventDefault()}>
          <span className="sr-only" role="status" aria-live="polite">
            {waiting || isFetching ? 'Loading suggestions' : `${flat.length} suggestion${flat.length === 1 ? '' : 's'}`}
          </span>
          <ul id={listId} role="listbox" aria-label="Search suggestions" aria-busy={waiting || undefined}
            className={`${placement === 'above' ? 'max-h-[min(50vh,380px)]' : 'max-h-[min(65vh,440px)]'} overflow-y-auto overscroll-contain py-1.5`}>
            {waiting && Array.from({ length: 3 }, (_, i) => (
              <li key={i} aria-hidden className="flex items-center gap-3 px-3 py-2">
                <span className="h-8 w-8 animate-pulse rounded-lg bg-subtle" />
                <span className="h-3 flex-1 animate-pulse rounded bg-subtle" />
              </li>
            ))}
            {!waiting && isError && flat.length === 0 && (
              <li className="flex items-center gap-2 px-4 py-3 text-sm text-muted">
                <span className="min-w-0 flex-1">Suggestions are unavailable right now. Press Enter to search.</span>
                <button type="button" onClick={() => void refetch()} className="shrink-0 rounded-md px-2 py-1 text-sm font-semibold text-accent-ink hover:bg-subtle">Retry</button>
              </li>
            )}
            {!waiting && !isError && flat.length === 0 && !isFetching && (
              <li className="px-4 py-3 text-sm text-muted">No matches for “{trimmed}”. Press Enter to search all listings.</li>
            )}
            {groups.map((g) => (
              <li key={g.title} role="presentation">
                <div className="px-3 pb-1 pt-2 text-[11px] font-semibold uppercase tracking-[0.08em] text-muted">{g.title}</div>
                <ul role="presentation">
                  {g.items.map((s) => {
                    index += 1;
                    const i = index;
                    const isActive = i === active;
                    return (
                      <li key={`${s.kind}-${s.slug}-${s.label}-${s.areaId ?? ''}`} id={optionId(i)} role="option" aria-selected={isActive}
                        onMouseEnter={() => setActive(i)} onClick={() => choose(s)}
                        className={`flex min-h-11 cursor-pointer items-center gap-3 px-3 py-2 ${isActive ? 'bg-subtle' : ''}`}>
                        <SuggestionIcon s={s} />
                        <span className="min-w-0 flex-1">
                          <span className="block truncate text-sm text-ink-2"><Highlight text={s.label} term={trimmed} /></span>
                          {s.detail && <span className="block truncate text-xs text-muted">{s.detail}</span>}
                        </span>
                        {s.rating != null && (
                          <span className="inline-flex shrink-0 items-center gap-0.5 text-xs font-semibold tabular text-ink-2">
                            <StarRounded sx={{ fontSize: 14, color: '#F4A62C' }} />{s.rating.toFixed(1)}
                          </span>
                        )}
                        <span className="hidden shrink-0 text-[11px] text-faint sm:inline">{kindLabel[s.kind]}</span>
                      </li>
                    );
                  })}
                </ul>
              </li>
            ))}
          </ul>
          <div className="flex items-center gap-2 border-t border-line bg-subtle/60 px-3 py-2 text-xs text-muted">
            <SearchRounded sx={{ fontSize: 16 }} />
            <span className="min-w-0 flex-1 truncate">Press <kbd className="rounded border border-line bg-surface px-1 font-sans">Enter</kbd> to search all results for “{trimmed}”</span>
            <span className="hidden shrink-0 md:inline">↑ ↓ to navigate · Esc to close</span>
          </div>
        </div>
      )}
    </div>
  );
}
