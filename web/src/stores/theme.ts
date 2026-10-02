import { useEffect, useSyncExternalStore } from 'react';
import { create } from 'zustand';
import { persist } from 'zustand/middleware';

export type ThemeMode = 'light' | 'dark' | 'system';
export type AccentTheme = 'amber' | 'ocean' | 'emerald' | 'violet' | 'rose';

/** Colour themes offered to users. Values mirror the --cb-accent tokens in index.css. */
export const accents: { key: AccentTheme; label: string; light: string; dark: string }[] = [
  { key: 'amber', label: 'Calling Bell', light: '#F4A62C', dark: '#F4A62C' },
  { key: 'ocean', label: 'Ocean', light: '#2E90FA', dark: '#53B1FD' },
  { key: 'emerald', label: 'Emerald', light: '#079455', dark: '#3CCB7F' },
  { key: 'violet', label: 'Violet', light: '#7A5AF8', dark: '#9B8AFB' },
  { key: 'rose', label: 'Rose', light: '#E31B54', dark: '#FD6F8E' },
];

interface ThemeState {
  mode: ThemeMode;
  accent: AccentTheme;
  setMode: (mode: ThemeMode) => void;
  setAccent: (accent: AccentTheme) => void;
}

/** Dark is the default experience; users can switch to Light or Auto (follow the device). */
export const DEFAULT_MODE: ThemeMode = 'dark';

// The storage key and version are also read by the pre-paint script in index.html - keep them in sync.
export const useThemeSettings = create<ThemeState>()(
  persist((set) => ({ mode: DEFAULT_MODE, accent: 'amber', setMode: (mode) => set({ mode }), setAccent: (accent) => set({ accent }) }), {
    name: 'cb-theme',
    version: 1,
    // v0 defaulted to "system"; move those devices to the new dark default.
    migrate: (persisted, version) => {
      const state = persisted as Partial<ThemeState>;
      return version < 1 && state.mode === 'system' ? { ...state, mode: DEFAULT_MODE } : state;
    },
  }),
);

const darkQuery = typeof window !== 'undefined' ? window.matchMedia('(prefers-color-scheme: dark)') : null;
const subscribeSystem = (cb: () => void) => { darkQuery?.addEventListener('change', cb); return () => darkQuery?.removeEventListener('change', cb); };

/** The mode actually in effect ("system" resolved against the OS preference). */
export function useResolvedMode(): 'light' | 'dark' {
  const mode = useThemeSettings((s) => s.mode);
  const systemDark = useSyncExternalStore(subscribeSystem, () => !!darkQuery?.matches, () => false);
  return mode === 'system' ? (systemDark ? 'dark' : 'light') : mode;
}

/** Applies the theme to <html> so CSS tokens, Tailwind `dark:` and native controls follow it. */
export function useApplyTheme() {
  const resolved = useResolvedMode();
  const accent = useThemeSettings((s) => s.accent);
  useEffect(() => {
    const root = document.documentElement;
    root.dataset.theme = resolved;
    root.dataset.accent = accent;
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', resolved === 'dark' ? '#060A11' : '#0B1220');
  }, [resolved, accent]);
  return { resolved, accent };
}
