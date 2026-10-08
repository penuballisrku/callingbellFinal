import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

/** Bumped when the stored shape or its meaning changes; older saved locations are discarded instead of being misread. */
const STORAGE_KEY = 'cb-location-v2';
/** Earlier versions kept the location in localStorage indefinitely, so an old manual choice outlived IP changes. */
const LEGACY_KEYS = ['cb-city'];

/** What the server resolved from the visitor's IP address (GET /api/geo/district); the IP itself never reaches the browser. */
export interface DetectedLocation {
  countryCode: string | null;
  /** False when the IP couldn't be located: the visitor picks the place themselves. */
  located: boolean;
  district: {
    stateSlug?: string | null; state: string; citySlug: string; cityName: string;
    areaId?: string | null; areaSlug?: string | null; areaName?: string | null;
  } | null;
}

/** Names that come with a choice, so labels show without loading lists; any of them can be filled in later. */
export interface PlaceNames { cityName?: string | null; stateSlug?: string | null; stateName?: string | null; areaSlug?: string | null; areaName?: string | null }

/**
 * Where the visitor is browsing - country, state / province / region, city, area - in one place, so every page agrees. The country is
 * always the IP's; the rest comes from the IP until the visitor chooses (a manual choice wins for the rest of the browser session).
 * Changing a level clears the levels below it. Kept per browser session, so a new session starts from the current IP again.
 */
interface LocationState {
  /** The IP's country, which the selection belongs to. */
  countryCode: string | null;
  stateSlug: string | null;
  stateName: string | null;
  citySlug: string | null;
  cityName: string | null;
  /** Area (locality) within the city, if one was picked. */
  areaId: string | null;
  areaSlug: string | null;
  areaName: string | null;
  /** 'user' once the visitor picks a place themselves; 'ip' while it comes from their detected location. */
  source: 'user' | 'ip' | null;
  /** A state / province / region: its city and area are cleared. */
  setState: (slug: string | null, name?: string | null) => void;
  /** A city (and optionally one of its areas): the previous area is cleared. */
  setCity: (slug: string | null, areaId?: string | null, names?: PlaceNames) => void;
  /** Fills in names learned later (e.g. from the city's area list) without changing the selection. */
  describe: (citySlug: string, names: PlaceNames) => void;
  /**
   * The IP's location. Applied unless the visitor chose a place in the same country this session; a different country (new network,
   * VPN) replaces the old choice, so a location from another IP is never kept.
   */
  applyDetected: (detected: DetectedLocation) => void;
  /** Back to the IP's location. */
  resetToDetected: () => void;
}

const EMPTY = { stateSlug: null, stateName: null, citySlug: null, cityName: null, areaId: null, areaSlug: null, areaName: null } as const;

/** The selection fields (no actions). */
export type LocationSelection = Pick<LocationState, 'countryCode' | 'stateSlug' | 'stateName' | 'citySlug' | 'cityName' | 'areaId' | 'areaSlug' | 'areaName' | 'source'>;

/**
 * The selection once the IP's location is taken into account: kept when the visitor chose a place in the same country this session,
 * otherwise the IP's place (nothing, when the IP couldn't be placed). Pure, so pages can use it in the same render the IP answer
 * arrives in, before the store has been updated.
 */
export function resolveLocation(s: LocationSelection, { countryCode, located, district: d }: DetectedLocation): LocationSelection {
  if (s.source === 'user' && countryCode && s.countryCode === countryCode) return s;
  // Chosen before the IP had answered (e.g. a city in a shared link): it was picked from this country's lists, so it stays.
  if (s.source === 'user' && s.citySlug && !s.countryCode) return { ...s, countryCode };
  if (!d) return { countryCode, ...EMPTY, source: located ? 'ip' : null };
  const next: LocationSelection = {
    countryCode, stateSlug: d.stateSlug ?? null, stateName: d.state, citySlug: d.citySlug, cityName: d.cityName,
    areaId: d.areaId ?? null, areaSlug: d.areaId ? d.areaSlug ?? null : null, areaName: d.areaId ? d.areaName ?? null : null, source: 'ip',
  };
  // Unchanged: the same object, so nothing re-renders.
  return (Object.keys(next) as (keyof LocationSelection)[]).every((k) => s[k] === next[k]) ? s : next;
}

function sessionStore(): Storage {
  try {
    for (const key of LEGACY_KEYS) window.localStorage.removeItem(key);
  } catch { /* storage unavailable */ }
  return window.sessionStorage;
}

export const useCity = create<LocationState>()(
  persist(
    (set) => ({
      countryCode: null,
      ...EMPTY,
      source: null,
      setState: (stateSlug, stateName = null) => set((s) => (s.stateSlug === stateSlug ? s
        : { ...EMPTY, countryCode: s.countryCode, stateSlug, stateName: stateSlug ? stateName : null, source: 'user' })),
      setCity: (citySlug, areaId = null, names = {}) => set((s) => {
        if (!citySlug) return { ...EMPTY, stateSlug: s.stateSlug, stateName: s.stateName, source: 'user' };
        const same = s.citySlug === citySlug;
        return {
          citySlug,
          cityName: names.cityName ?? (same ? s.cityName : null),
          stateSlug: names.stateSlug ?? (same ? s.stateSlug : null),
          stateName: names.stateName ?? (same ? s.stateName : null),
          areaId,
          areaSlug: areaId ? names.areaSlug ?? (same && s.areaId === areaId ? s.areaSlug : null) : null,
          areaName: areaId ? names.areaName ?? (same && s.areaId === areaId ? s.areaName : null) : null,
          source: 'user',
        };
      }),
      describe: (citySlug, names) => set((s) => (s.citySlug !== citySlug ? s : {
        cityName: s.cityName ?? names.cityName ?? null,
        stateSlug: s.stateSlug ?? names.stateSlug ?? null,
        stateName: s.stateName ?? names.stateName ?? null,
        areaSlug: s.areaId ? s.areaSlug ?? names.areaSlug ?? null : null,
        areaName: s.areaId ? s.areaName ?? names.areaName ?? null : null,
      })),
      applyDetected: (detected) => set((s) => resolveLocation(s, detected)),
      resetToDetected: () => set({ ...EMPTY, source: null }),
    }),
    {
      name: STORAGE_KEY,
      version: 2,
      storage: createJSONStorage(sessionStore),
      partialize: ({ countryCode, stateSlug, stateName, citySlug, cityName, areaId, areaSlug, areaName, source }) =>
        ({ countryCode, stateSlug, stateName, citySlug, cityName, areaId, areaSlug, areaName, source }),
    },
  ),
);
