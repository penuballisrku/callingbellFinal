import { create } from 'zustand';
import { persist } from 'zustand/middleware';

interface CityState { citySlug: string | null; setCity: (slug: string | null) => void }

export const useCity = create<CityState>()(
  persist((set) => ({ citySlug: null, setCity: (citySlug) => set({ citySlug }) }), { name: 'cb-city' }),
);
