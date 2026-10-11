import { create } from 'zustand';

/**
 * "Add to Home Screen": the app service worker (public/sw.js) and the browser's install prompt. Chromium browsers (Chrome, Edge,
 * Samsung Internet) offer an install prompt the page can show from a button; iPhone and iPad have none, so they get instructions
 * (Share, then Add to Home Screen). Once installed, the app opens full screen and nothing here is shown.
 */
interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

/** Running from the home screen (or as an installed desktop app). */
const isStandalone = () =>
  typeof window !== 'undefined'
  && (window.matchMedia('(display-mode: standalone)').matches || (navigator as Navigator & { standalone?: boolean }).standalone === true);

/** iPhone or iPad (iPadOS reports itself as a Mac with a touch screen). */
export const isIos = () =>
  typeof navigator !== 'undefined'
  && (/iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1));

const useInstallState = create<{ prompt: BeforeInstallPromptEvent | null; installed: boolean; iosSteps: boolean }>(() => ({
  prompt: null, installed: isStandalone(), iosSteps: false,
}));

/** The iPhone steps dialog, opened from any "Install" button and shown by the layout (so it outlives a menu that closes). */
export function useIosInstallSteps() {
  const open = useInstallState((s) => s.iosSteps);
  return { open, close: () => useInstallState.setState({ iosSteps: false }) };
}

/** Registers the app service worker and listens for the install prompt; called once, before the app renders. */
export function setupPwa() {
  window.addEventListener('beforeinstallprompt', (e) => {
    // Keep it for the "Install app" button instead of the browser's own mini bar.
    e.preventDefault();
    useInstallState.setState({ prompt: e as BeforeInstallPromptEvent });
  });
  window.addEventListener('appinstalled', () => useInstallState.setState({ prompt: null, installed: true }));
  if ('serviceWorker' in navigator && window.isSecureContext) {
    // After the page has loaded, so it doesn't compete with it (in a production build the page may already have loaded by now).
    const register = () => void navigator.serviceWorker.register('/sw.js', { scope: '/' }).catch(() => undefined);
    if (document.readyState === 'complete') register();
    else window.addEventListener('load', register, { once: true });
  }
}

/**
 * Whether the app can be added to the home screen here, and how. `mode` is "prompt" (the browser's install dialog) or "ios"
 * (Share → Add to Home Screen); `install` opens the browser's dialog, or the steps dialog that <InstallPrompt /> renders.
 */
export function useInstallApp() {
  const { prompt, installed } = useInstallState();
  const mode: 'prompt' | 'ios' | null = installed ? null : prompt ? 'prompt' : isIos() ? 'ios' : null;
  const install = async (): Promise<'installed' | 'dismissed' | 'ios' | 'unavailable'> => {
    if (mode === 'ios') { useInstallState.setState({ iosSteps: true }); return 'ios'; }
    if (!prompt) return 'unavailable';
    await prompt.prompt();
    const { outcome } = await prompt.userChoice;
    // A prompt can only be used once; the browser fires a new one later if the app is still not installed.
    useInstallState.setState({ prompt: null, installed: outcome === 'accepted' });
    return outcome === 'accepted' ? 'installed' : 'dismissed';
  };
  return { available: mode !== null, mode, installed, install };
}
