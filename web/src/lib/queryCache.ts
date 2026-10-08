import { QueryClient, type Query } from '@tanstack/react-query';
import { createSyncStoragePersister } from '@tanstack/query-sync-storage-persister';
import type { PersistQueryClientOptions } from '@tanstack/react-query-persist-client';
import { api, ApiError } from './api';

/** Bumped when location-keyed data changes shape or meaning (v2: cities per state, the one IP location request). */
const LOCATION_DATA_VERSION = 'location-v2';

/** How long a saved page stays usable on a later visit; it is shown at once and refreshed in the background. */
const PERSIST_MAX_AGE = 24 * 60 * 60_000;

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 60_000,
      // Kept in memory as long as it is saved, so going back to a page is instant and the saved copy isn't dropped early.
      gcTime: PERSIST_MAX_AGE,
      refetchOnWindowFocus: false,
      retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2,
    },
  },
});

/**
 * Public, non-personal data saved in the browser between visits: home page sections, categories, cities and areas, page content, plans
 * and banners. Never saved: anything personal (account, owner and admin portals, notifications, payments), Google Maps data (Google's
 * terms don't allow storing it) and search-as-you-type results.
 */
const PERSISTED = new Set([
  'home', 'categories', 'cities', 'city-areas', 'lookups', 'content', 'banners', 'plans', 'top-picks', 'nearby-services',
  'assistant-starters', 'local-reviews', 'local-reviews-summary', 'popular-searches',
]);

/** Located by the visitor's IP address when no city is in the key; the next visit may come from somewhere else. */
const LOCATED = new Set(['top-picks', 'nearby-services', 'local-reviews', 'local-reviews-summary']);

function shouldPersist(query: Query): boolean {
  if (query.state.status !== 'success' || !PERSISTED.has(String(query.queryKey[0]))) return false;
  if (LOCATED.has(String(query.queryKey[0])) && !query.queryKey[1]) return false;
  // An AI answer still being generated would only be replaced on the next visit; save the finished one instead.
  const data = query.state.data as { aiPending?: boolean } | undefined;
  return !data?.aiPending;
}

function storage(): Storage | undefined {
  try {
    const s = window.localStorage;
    s.setItem('cb-probe', '1');
    s.removeItem('cb-probe');
    return s;
  } catch {
    return undefined; // Private mode or blocked storage: the app works as before, just without the saved copy.
  }
}

export const persistOptions: Omit<PersistQueryClientOptions, 'queryClient'> = {
  persister: createSyncStoragePersister({ storage: storage(), key: 'cb-query-cache', throttleTime: 2000 }),
  maxAge: PERSIST_MAX_AGE,
  // A new build drops the saved copy, so an updated app never reads data in an older shape; so does a new location data version
  // (LOCATION_DATA_VERSION), whose keys and meaning differ from the old ones.
  buster: `${import.meta.env.VITE_BUILD_ID ?? 'dev'}|${LOCATION_DATA_VERSION}`,
  dehydrateOptions: { shouldDehydrateQuery: shouldPersist },
};

/**
 * Loads a business page's code and data before the click (on hover, focus or touch), so it opens instantly. Same query key and request
 * as the page itself; does nothing when the data is already fresh.
 */
export function prefetchBusiness(slug: string) {
  void import('@/features/business/BusinessPage');
  void queryClient.prefetchQuery({ queryKey: ['business', slug], queryFn: () => api.get(`/api/businesses/${slug}`), staleTime: 60_000 });
}

/** Downloads the most visited pages' code while the browser is idle, so the first visit to each doesn't wait for it. */
export function preloadMainPages() {
  const load = () => {
    void import('@/features/search/SearchPage');
    void import('@/features/business/BusinessPage');
    void import('@/features/categories/CategoriesPage');
  };
  if ('requestIdleCallback' in window) window.requestIdleCallback(load, { timeout: 4000 });
  else setTimeout(load, 2500);
}
