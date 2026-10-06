import { useId, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { useNavigate } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import SearchRounded from '@mui/icons-material/SearchRounded';
import StarRounded from '@mui/icons-material/StarRounded';
import { api } from '@/lib/api';
import { useDebounced } from '@/lib/hooks';
import { namesPlace, parsedSearchParams, parseSearch } from '@/lib/searchParse';
import type { SearchSuggestion, SearchSuggestions } from '@/lib/types';
import { Img } from './ui';

/** Suggestions are only requested once the trimmed text reaches this length (the API enforces the same minimum). */
export const SUGGEST_MIN_CHARS = 3;

/** Default destination for a suggestion. */
export function suggestionHref(s: SearchSuggestion, citySlug?: string | null) {
  const city = citySlug ? `&city=${citySlug}` : '';
  switch (s.kind) {
    case 'Category': return `/categories/${s.slug}`;
    case 'SubCategory': return `/search?sub=${s.slug}${city}`;
    case 'Service': return `/search?sub=${s.subCategorySlug}&q=${encodeURIComponent(s.label)}${city}`;
    case 'Business': return `/b/${s.slug}`;
  }
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

const groupTitles:[keyof Omit<SearchSuggestions, 'query'>, string][] = [['categories', 'Categories'], ['services', 'Services'], ['businesses', 'Businesses']];

/** "Suggested by AI" (GET /api/search/suggest/ai) is asked for phrases only, once typing pauses a little longer. */
const AI_MIN_CHARS = 8;
const AI_DEBOUNCE_MS = 600;
const kindLabel: Record<SearchSuggestion['kind'], string> = { Category: 'Category', SubCategory: 'Category', Service: 'Service', Business: 'Business' };

/** Bolds the first case-insensitive occurrence of the typed text. */
function Highlight({ text, term }: { text: string; term: string }) {
  const i = text.toLowerCase().indexOf(term.toLowerCase());
  if (!term || i < 0) return <>{text}</>;
  return <>{text.slice(0, i)}<mark className="bg-transparent font-semibold text-ink">{text.slice(i, i + term.length)}</mark>{text.slice(i + term.length)}</>;
}

/**
 * Search input with autocomplete ("intellisense"). Renders the <input> plus a suggestion list that opens once
 * {@link SUGGEST_MIN_CHARS} characters are typed. Fully keyboard operable (↑ ↓ Enter Esc) using the ARIA combobox pattern.
 * Place it inside the caller's <form>: pressing Enter with no highlighted suggestion submits that form as before.
 */
export function SearchSuggest({ value, onChange, citySlug, onSelect, inputClassName, placeholder, ariaLabel, className, panelClassName, children }: {
  value: string;
  onChange: (value: string) => void;
  citySlug?: string | null;
  /** Override what happens when a suggestion is chosen; defaults to navigating to {@link suggestionHref}. */
  onSelect?: (s: SearchSuggestion) => void;
  inputClassName?: string;
  placeholder?: string;
  ariaLabel: string;
  className?: string;
  panelClassName?: string;
  children?: ReactNode;
}) {
  const navigate = useNavigate();
  const id = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);

  const trimmed = value.trim();
  const term = useDebounced(trimmed, 220);
  const ready = term.length >= SUGGEST_MIN_CHARS;
  const { data, isFetching, isError } = useQuery({
    queryKey: ['search-suggest', term.toLowerCase(), citySlug ?? null],
    queryFn: () => api.get<SearchSuggestions>('/api/search/suggest', { q: term, city: citySlug ?? undefined }),
    enabled: open && ready,
    staleTime: 60_000,
    placeholderData: keepPreviousData,
  });

  // Categories the local AI matches by meaning ("water dripping from ceiling" → Plumbers). Separate from the name-based list so typing
  // never waits for the model; the group appears at the top when it arrives.
  const aiTerm = useDebounced(trimmed, AI_DEBOUNCE_MS);
  const { data: aiData } = useQuery({
    queryKey: ['search-suggest-ai', aiTerm.toLowerCase()],
    queryFn: () => api.get<SearchSuggestion[]>('/api/search/suggest/ai', { q: aiTerm }),
    enabled: open && aiTerm.length >= AI_MIN_CHARS && aiTerm.includes(' '),
    staleTime: 300_000,
  });

  const groups = useMemo(() => {
    const byName = data ? groupTitles.map(([key, title]) => ({ title, items: data[key] })).filter((g) => g.items.length) : [];
    const listed = new Set(byName.flatMap((g) => g.items).map((s) => s.slug));
    const ai = aiTerm === trimmed ? (aiData ?? []).filter((s) => !listed.has(s.slug)) : [];
    return ai.length ? [{ title: 'Suggested by AI', items: ai }, ...byName] : byName;
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
    else navigate(suggestionHref(s, citySlug));
    inputRef.current?.blur();
  };

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Escape') { if (showPanel) { e.preventDefault(); setOpen(false); setActive(-1); } return; }
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

  let index = -1;
  return (
    <div className={`relative ${className ?? ''}`}>
      <input ref={inputRef} value={value} placeholder={placeholder} aria-label={ariaLabel} autoComplete="off" spellCheck={false}
        role="combobox" aria-autocomplete="list" aria-expanded={showPanel} aria-controls={listId}
        aria-activedescendant={showPanel && active >= 0 ? optionId(active) : undefined}
        onChange={(e) => { onChange(e.target.value); setOpen(true); setActive(-1); }}
        onFocus={() => setOpen(true)} onBlur={() => { setOpen(false); setActive(-1); }} onKeyDown={onKeyDown}
        className={inputClassName} />
      {children}

      {showPanel && (
        <div className={`absolute left-0 right-0 top-full z-50 mt-2 overflow-hidden rounded-xl border border-line bg-surface text-left text-ink shadow-[var(--cb-shadow-lg)] ${panelClassName ?? ''}`}
          // Keep focus in the input while clicking inside the panel so the click lands before blur closes it.
          onMouseDown={(e) => e.preventDefault()}>
          <ul id={listId} role="listbox" aria-label="Search suggestions" className="max-h-[min(65vh,440px)] overflow-y-auto py-1.5">
            {waiting && Array.from({ length: 3 }, (_, i) => (
              <li key={i} aria-hidden className="flex items-center gap-3 px-3 py-2">
                <span className="h-8 w-8 animate-pulse rounded-lg bg-subtle" />
                <span className="h-3 flex-1 animate-pulse rounded bg-subtle" />
              </li>
            ))}
            {!waiting && isError && <li className="px-4 py-3 text-sm text-muted">Suggestions are unavailable right now. Press Enter to search.</li>}
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
                      <li key={`${s.kind}-${s.slug}-${s.label}`} id={optionId(i)} role="option" aria-selected={isActive}
                        onMouseEnter={() => setActive(i)} onClick={() => choose(s)}
                        className={`flex cursor-pointer items-center gap-3 px-3 py-2 ${isActive ? 'bg-subtle' : ''}`}>
                        {s.kind === 'Business'
                          ? <Img src={s.imageUrl} alt="" className="h-8 w-8 shrink-0" rounded="rounded-lg" fallbackText={s.label} />
                          : <span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-subtle">
                              <Img src={s.imageUrl} alt="" className="h-5 w-5" rounded="rounded" fit="contain" fallbackText={s.label} />
                            </span>}
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
