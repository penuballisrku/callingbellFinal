import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from './api';
import type { City, Lookup, Lookups } from './types';

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

export function useCities() {
  return useQuery({ queryKey: ['cities'], queryFn: () => api.get<City[]>('/api/locations/cities'), staleTime: 30 * 60_000 });
}

export function useDocumentTitle(title?: string) {
  useEffect(() => {
    document.title = title ? `${title} · Calling Bell` : 'Calling Bell - Discover. Connect. Book. Grow.';
  }, [title]);
}
