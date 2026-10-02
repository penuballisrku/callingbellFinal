import { create } from 'zustand';
import { persist } from 'zustand/middleware';

/** The business the owner is currently managing in the business portal. */
export const useSelectedBusiness = create<{ id: string | null; set: (id: string) => void }>()(
  persist((set) => ({ id: null, set: (id) => set({ id }) }), { name: 'cb-owner-business' }),
);
