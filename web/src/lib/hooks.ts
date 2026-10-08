import { useEffect, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useShallow } from 'zustand/react/shallow';
import { api } from './api';
import type { Category, City, CityAreas, Lookup, Lookups, Plan, StateRegion } from './types';
import { resolveLocation, useCity, type DetectedLocation, type LocationSelection } from '@/stores/city';

/** <c>areaId</c> is set with <c>areaSlug</c>, so the area can be selected without loading the city list. */
export interface VisitorDistrict {
  citySlug: string; cityName: string; state: string; stateSlug?: string | null; cityId?: string | null;
  areaSlug?: string | null; areaId?: string | null; areaName?: string | null; matchedBy: 'area' | 'city' | 'distance';
}
/**
 * The visitor's location from their IP address (GET /api/geo/district; the IP itself is never sent to the browser): country, and
 * whether its cities are still being imported; state, city and area when the IP is near a listed city.
 */
export interface VisitorLocation {
  place?: string | null; region?: string | null; district?: VisitorDistrict | null;
  country?: string | null; countryName?: string | null; importing: boolean; located: boolean;
  postcode?: string | null; latitude?: number | null; longitude?: number | null;
}

/**
 * The one IP location request of a page: everything location-aware (country badge, country names, the location picker, the city
 * and area every section shows) reads it, so it is made once. Never cached by the browser; asked again after five minutes when the
 * window regains focus, so a new network or VPN gives a new location without a reload. While the country's cities are being imported
 * (the first visit from a new country) it polls, so the city and area appear when the import finishes.
 */
const locationQuery = {
  queryKey: ['geo', 'location'],
  queryFn: ({ signal }: { signal: AbortSignal }) => api.get<VisitorLocation>('/api/geo/district', undefined, signal),
  staleTime: 5 * 60_000,
  refetchOnWindowFocus: true,
  retry: 1,
  refetchInterval: (q: { state: { data?: VisitorLocation; dataUpdateCount: number } }) =>
    (q.state.data?.importing && q.state.dataUpdateCount < 60 ? 4_000 : false),
} as const;

export function useVisitorLocation() {
  return useQuery(locationQuery);
}

export const toDetected = (l: VisitorLocation): DetectedLocation =>
  ({ countryCode: l.country ?? null, located: l.located, district: l.district ?? null });

/** The listed city (district) the visitor is browsing from, detected on the server from their IP address. */
export function useVisitorDistrict() {
  return useQuery({ ...locationQuery, select: (d: VisitorLocation) => d.district ?? null });
}

const selectionFields = (s: LocationSelection): LocationSelection => ({
  countryCode: s.countryCode, stateSlug: s.stateSlug, stateName: s.stateName, citySlug: s.citySlug, cityName: s.cityName,
  areaId: s.areaId, areaSlug: s.areaSlug, areaName: s.areaName, source: s.source,
});

/**
 * The place a page should show: the visitor's choice this session, else their IP's. <c>ready</c> stays false until the IP location
 * has answered (a quick request), so a section loads once, for the right place, and a location saved earlier in the session is never
 * shown for a different IP. The IP's answer is applied here in the same render it arrives in (not a render later, via the store), so
 * sections don't first load the old place.
 */
export function usePageCity() {
  const selection = useCity(useShallow(selectionFields));
  const location = useVisitorLocation();
  const place = location.data ? resolveLocation(selection, toDetected(location.data)) : selection;
  return { ...place, ready: !location.isPending || location.isError };
}

export function useDebounced<T>(value: T, delay = 350): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(t);
  }, [value, delay]);
  return debounced;
}

/** Status / type metadata (labels, colours) comes from the LookupValues table. */
export function useLookups() {
  return useQuery({ queryKey: ['lookups'], queryFn: () => api.get<Lookups>('/api/lookups'), staleTime: 30 * 60_000 });
}

export function useLookup(type: string): Lookup[] {
  const { data } = useLookups();
  return data?.[type] ?? [];
}

/** The visitor's country (from their IP) and whether its cities are still being imported; from the one IP location request. */
export function useCountryCatalog() {
  return useQuery({
    ...locationQuery,
    select: (d: VisitorLocation) => ({ countryCode: d.country ?? '', countryName: d.countryName ?? null, importing: d.importing }),
  });
}

/**
 * Keeps the store in step with the IP location (mount once, in the layout): applies it (see resolveLocation), and when a country's city
 * import finishes reloads the city and state lists, which were empty or partial until then.
 */
export function useLocationSync() {
  const queryClient = useQueryClient();
  const { data } = useVisitorLocation();
  const applyDetected = useCity((s) => s.applyDetected);
  // `source` returns to null after "Use my location", so the IP's place is applied again.
  const source = useCity((s) => s.source);
  useEffect(() => { if (data) applyDetected(toDetected(data)); }, [data, source, applyDetected]);
  const importing = data?.importing;
  const wasImporting = useRef(false);
  useEffect(() => {
    if (wasImporting.current && importing === false) {
      void queryClient.invalidateQueries({ queryKey: ['cities'] });
      void queryClient.invalidateQueries({ queryKey: ['states'] });
    }
    wasImporting.current = !!importing;
  }, [importing, queryClient]);
}

/** The country's states / provinces / regions that have listed cities. */
export function useStates(country: string | null | undefined) {
  return useQuery({
    queryKey: ['states', country],
    queryFn: ({ signal }) => api.get<StateRegion[]>('/api/locations/states', { country }, signal),
    enabled: !!country,
    staleTime: 30 * 60_000,
  });
}

/**
 * Cities of the visitor's country (curated cities first, then largest first). With <paramref name="stateSlug"/> only that state's
 * (what the location picker shows: a few dozen instead of thousands); without, the whole country (searchable city fields).
 */
export function useCities(stateSlug?: string | null, enabled = true) {
  const catalog = useCountryCatalog();
  // If the country can't be detected the API's default country is used.
  const country = catalog.data?.countryCode ?? (catalog.isError ? '' : undefined);
  return useQuery({
    queryKey: ['cities', country, stateSlug ?? null],
    queryFn: ({ signal }) => api.get<City[]>('/api/locations/cities', { country: country || null, state: stateSlug || null }, signal),
    enabled: enabled && country !== undefined,
    staleTime: 30 * 60_000,
  });
}

/**
 * Subscription plans priced for the visitor's country (the same country as the city lists): its currency, a fair local price and its tax.
 * If the country can't be detected, the API's default is used.
 */
export function usePlans() {
  const catalog = useCountryCatalog();
  const country = catalog.data?.countryCode ?? (catalog.isError ? '' : undefined);
  return useQuery({
    queryKey: ['plans', country],
    queryFn: () => api.get<Plan[]>('/api/plans', { country: country || null }),
    enabled: country !== undefined,
    staleTime: 600_000,
  });
}

/** Plans priced for the country a business is in, as its checkout charges them (owner portal). */
export function useBusinessPlans(businessId: string | undefined) {
  return useQuery({
    queryKey: ['plans', 'business', businessId],
    queryFn: () => api.get<Plan[]>('/api/plans', { business: businessId }),
    enabled: !!businessId,
    staleTime: 600_000,
  });
}

/**
 * Every area of one city with alternate names and sub-localities (GET /api/locations/cities/{slug}/areas), cached per city: only the
 * selected city's areas are ever loaded. The first request for a city queues the area-discovery agent; while it is working this polls
 * so new areas appear without a reload.
 */
export function useCityAreas(citySlug?: string | null) {
  const query = useQuery({
    queryKey: ['city-areas', citySlug],
    queryFn: ({ signal }) => api.get<CityAreas>(`/api/locations/cities/${citySlug}/areas`, undefined, signal),
    enabled: !!citySlug,
    staleTime: 30 * 60_000,
    refetchInterval: (q) => (q.state.data?.discovering && q.state.dataUpdateCount < 120 ? 4_000 : false),
  });
  return { ...query, areas: query.data?.areas ?? [], discovering: !!query.data?.discovering };
}

/**
 * Slug of the selected area (by id) within the city: known with the selection when it came from the IP or the picker, else from the
 * city's area list.
 */
export function useSelectedAreaSlug(citySlug: string | null, areaId: string | null): string | null {
  const known = useCity((s) => (s.citySlug === citySlug && s.areaId === areaId ? s.areaSlug : null));
  const { areas } = useCityAreas(areaId && !known ? citySlug : null);
  return known ?? (areaId ? areas.find((a) => a.id === areaId)?.slug ?? null : null);
}

/** Full category → sub-category tree with business counts, shared by every screen that lists or filters categories. */
export function useCategories() {
  return useQuery({ queryKey: ['categories'], queryFn: () => api.get<Category[]>('/api/categories'), staleTime: 600_000 });
}

/**
 * Sets the tab title of a page in the owner and admin portals. Public pages get their title (with the rest of their SEO head) from
 * the SEO service instead (SeoHead), which marks the document while it is in charge.
 */
export function useDocumentTitle(title?: string) {
  useEffect(() => {
    if (document.documentElement.dataset.seoManaged === '1') return;
    document.title = title ? `${title} · Calling Bell` : 'Calling Bell - Discover. Connect. Book. Grow.';
  }, [title]);
}
