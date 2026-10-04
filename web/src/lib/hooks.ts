import { useEffect, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './api';
import type { Category, City, CityAreas, CountryCatalog, Lookup, Lookups } from './types';

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

/**
 * The visitor's country (from their IP) and its city catalogue. While the city catalogue agent is importing the country's cities this
 * polls, and when the import finishes every city list reloads.
 */
export function useCountryCatalog() {
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: ['geo', 'country-catalog'],
    queryFn: () => api.get<CountryCatalog>('/api/geo/country-catalog'),
    staleTime: 30 * 60_000,
    retry: 1,
    refetchInterval: (q) => (q.state.data?.importing && q.state.dataUpdateCount < 60 ? 5_000 : false),
  });
  const importing = query.data?.importing;
  const wasImporting = useRef(false);
  useEffect(() => {
    if (wasImporting.current && importing === false) void queryClient.invalidateQueries({ queryKey: ['cities'] });
    wasImporting.current = !!importing;
  }, [importing, queryClient]);
  return query;
}

/** Every city of the visitor's country (curated cities first, then largest first), shared by all city dropdowns. */
export function useCities() {
  const catalog = useCountryCatalog();
  // If the country can't be detected the API's default country is used.
  const country = catalog.data?.countryCode ?? (catalog.isError ? '' : undefined);
  return useQuery({
    queryKey: ['cities', country],
    queryFn: () => api.get<City[]>('/api/locations/cities', { country: country || null }),
    enabled: country !== undefined,
    staleTime: 30 * 60_000,
  });
}

/**
 * Every area of a city with alternate names and sub-localities (GET /api/locations/cities/{slug}/areas). The first request for a city
 * queues the area-discovery agent; while it is working this polls so new areas appear without a reload.
 */
export function useCityAreas(citySlug?: string | null) {
  const query = useQuery({
    queryKey: ['city-areas', citySlug],
    queryFn: () => api.get<CityAreas>(`/api/locations/cities/${citySlug}/areas`),
    enabled: !!citySlug,
    staleTime: 30 * 60_000,
    refetchInterval: (q) => (q.state.data?.discovering && q.state.dataUpdateCount < 60 ? 10_000 : false),
  });
  return { ...query, areas: query.data?.areas ?? [], discovering: !!query.data?.discovering };
}

/** Slug of the selected area (by id) within the selected city: from the cities payload, else from the city's full area list. */
export function useSelectedAreaSlug(citySlug: string | null, areaId: string | null): string | null {
  const { data: cities } = useCities();
  const inline = areaId ? cities?.find((c) => c.slug === citySlug)?.areas.find((a) => a.id === areaId) : undefined;
  const { areas } = useCityAreas(areaId && !inline && cities ? citySlug : null);
  return inline?.slug ?? (areaId ? areas.find((a) => a.id === areaId)?.slug ?? null : null);
}

/** Full category → sub-category tree with business counts, shared by every screen that lists or filters categories. */
export function useCategories() {
  return useQuery({ queryKey: ['categories'], queryFn: () => api.get<Category[]>('/api/categories'), staleTime: 600_000 });
}

export function useDocumentTitle(title?: string) {
  useEffect(() => {
    document.title = title ? `${title} · Calling Bell` : 'Calling Bell - Discover. Connect. Book. Grow.';
  }, [title]);
}
