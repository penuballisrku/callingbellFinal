import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { AuthResult, CurrentUser } from '@/lib/types';

interface AuthState {
  user: CurrentUser | null;
  accessToken: string | null;
  refreshToken: string | null;
  setSession: (result: AuthResult) => void;
  clear: () => void;
}

export const useAuth = create<AuthState>()(
  persist(
    (set) => ({
      user: null,
      accessToken: null,
      refreshToken: null,
      setSession: (r) => set({ user: r.user, accessToken: r.accessToken, refreshToken: r.refreshToken }),
      clear: () => set({ user: null, accessToken: null, refreshToken: null }),
    }),
    { name: 'cb-auth' },
  ),
);

export const isAdmin = (u: CurrentUser | null) => !!u?.roles.includes('Administrator');
export const isOwner = (u: CurrentUser | null) => !!u?.roles.includes('BusinessOwner');
export const homeFor = (u: CurrentUser | null) => (isAdmin(u) ? '/admin' : isOwner(u) ? '/owner' : '/');
