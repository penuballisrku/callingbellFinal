import { api } from './api';

/** A listed city, or an area within it: the location dropdown's values. */
export interface SearchPlaceMatch { citySlug: string; cityName: string; areaId?: string | null; areaName?: string | null; matchedBy: string }

/** A typed search split into what and where (GET /api/search/parse). */
export interface ParsedSearch {
  text: string;
  /** What is searched for, without the place: "lawyers in Nellore" gives "lawyers". */
  query: string;
  /** The place after "in"/"near", as typed. */
  placeText?: string | null;
  /** The listed city/area the place matched; null when the place isn't listed (e.g. a city not on Calling Bell yet). */
  place?: SearchPlaceMatch | null;
  categorySlug?: string | null;
  subCategorySlug?: string | null;
}

/** True when the text may name a place or "near me" ("lawyers in Nellore", "plumbers near Madhapur"), so it is worth parsing. */
export const namesPlace = (text: string) => /\S\s+(in|near|around|at)\s+\S/i.test(text.trim());

export const parseSearch = (text: string, citySlug?: string | null) =>
  api.get<ParsedSearch>('/api/search/parse', { text: text.trim(), city: citySlug });

/**
 * Search-results URL parameters for a parsed search. A listed place selects its city/area (which the location dropdown then shows);
 * an unlisted place is kept as `place`, and a category or sub-category named by the query replaces the free text.
 */
export function parsedSearchParams(p: ParsedSearch, base: URLSearchParams, current: { citySlug?: string | null; areaId?: string | null }) {
  const next = new URLSearchParams(base);
  for (const key of ['q', 'category', 'sub', 'place', 'city', 'area', 'page', 'tab']) next.delete(key);
  if (p.subCategorySlug) next.set('sub', p.subCategorySlug);
  else if (p.categorySlug) next.set('category', p.categorySlug);
  else if (p.query) next.set('q', p.query);

  if (p.place) {
    next.set('city', p.place.citySlug);
    if (p.place.areaId) next.set('area', p.place.areaId);
  } else if (p.placeText) {
    next.set('place', p.placeText);
  } else if (current.citySlug) {
    next.set('city', current.citySlug);
    if (current.areaId) next.set('area', current.areaId);
  }
  return next;
}

/** "Madhapur, Hyderabad" or "Pune". */
export const placeLabel = (p: SearchPlaceMatch) => (p.areaName ? `${p.areaName}, ${p.cityName}` : p.cityName);
