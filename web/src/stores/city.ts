import { create } from 'zustand';
import { persist } from 'zustand/middleware';

interface CityState {
  citySlug: string | null;
  /** Area (locality) within the city, if one was picked. */
  areaId: string | null;
  /** 'user' once the visitor picks a city themselves; 'ip' while it comes from their detected district. */
  source: 'user' | 'ip' | null;
  setCity: (slug: string | null, areaId?: string | null) => void;
  /** Applies the IP-detected district unless the visitor has already chosen a city. */
  applyDetected: (slug: string, areaId: string | null) => void;
}

export const useCity = create<CityState>()(
  persist(
    (set) => ({
      citySlug: null,
      areaId: null,
      source: null,
      setCity: (citySlug, areaId = null) => set({ citySlug, areaId: citySlug ? areaId : null, source: 'user' }),
      applyDetected: (citySlug, areaId) => set((s) => (s.source === 'user' ? s : { citySlug, areaId, source: 'ip' })),
    }),
    {
      name: 'cb-city',
      // Before detection existed, a stored city was always the visitor's own choice.
      merge: (persisted, current) => {
        const p = (persisted ?? {}) as Partial<CityState>;
        return { ...current, ...p, source: p.source ?? (p.citySlug ? 'user' : null) };
      },
    },
  ),
);
