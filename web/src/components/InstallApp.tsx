import { useEffect, useState } from 'react';
import { useLocation } from 'react-router';
import { useSnackbar } from 'notistack';
import { Button, Dialog, DialogContent, DialogTitle, IconButton, useMediaQuery } from '@mui/material';
import InstallMobileRounded from '@mui/icons-material/InstallMobileRounded';
import IosShareRounded from '@mui/icons-material/IosShareRounded';
import AddBoxOutlined from '@mui/icons-material/AddBoxOutlined';
import CloseRounded from '@mui/icons-material/CloseRounded';
import { useInstallApp, useIosInstallSteps } from '@/lib/pwa';

/** Opens the browser's install dialog, or the iPhone steps (shown by <InstallPrompt />, which every layout renders). */
function useInstall() {
  const { available, mode, install } = useInstallApp();
  const { enqueueSnackbar } = useSnackbar();
  const start = async () => {
    if ((await install()) === 'installed') enqueueSnackbar('Calling Bell is on your home screen', { variant: 'success' });
  };
  return { available, mode, start };
}

/**
 * "Install app" for menus and the home page. Renders nothing where the app can't be installed (already installed, or a browser
 * without "Add to Home Screen").
 */
export function InstallAppButton({ variant = 'menu', onStart }: { variant?: 'menu' | 'button'; onStart?: () => void }) {
  const { available, start } = useInstall();
  if (!available) return null;
  const click = () => { onStart?.(); void start(); };
  return (
    <>
      {variant === 'menu' ? (
        <button type="button" onClick={click} className="flex items-center gap-2 rounded-lg px-3 py-2.5 text-left font-medium hover:bg-subtle">
          <InstallMobileRounded sx={{ fontSize: 20 }} className="text-accent-ink" /> Install the app
        </button>
      ) : (
        <Button variant="contained" size="large" startIcon={<InstallMobileRounded />} onClick={click}>Install the web app</Button>
      )}
    </>
  );
}

const DISMISSED_KEY = 'cb-install-dismissed';
/** After "Not now", the prompt stays away this long. */
const SNOOZE_MS = 14 * 24 * 60 * 60_000;
/** It appears once the visitor has looked around a little. */
const DELAY_MS = 20_000;
/** Pages with their own bar along the bottom of the screen (booking buttons, chat, video controls). */
const HIDDEN_ON = /^\/(business\/|video\/|login|register|owner\/messages|account)/;

const snoozed = () => { try { return Date.now() - Number(localStorage.getItem(DISMISSED_KEY) ?? 0) < SNOOZE_MS; } catch { return true; } };

/**
 * A small "Add Calling Bell to your home screen" card on phones, once per two weeks at most, and the iPhone steps dialog for every
 * install button on the page. Rendered once by each layout.
 */
export function InstallPrompt() {
  const phone = useMediaQuery('(max-width: 640px)');
  const { pathname } = useLocation();
  const { available, mode, start } = useInstall();
  const iosSteps = useIosInstallSteps();
  const [ready, setReady] = useState(false);
  const [closed, setClosed] = useState(snoozed);

  useEffect(() => {
    const t = setTimeout(() => setReady(true), DELAY_MS);
    return () => clearTimeout(t);
  }, []);

  const dismiss = () => {
    setClosed(true);
    try { localStorage.setItem(DISMISSED_KEY, String(Date.now())); } catch { /* private mode */ }
  };
  const visible = phone && ready && available && !closed && !HIDDEN_ON.test(pathname);

  return (
    <>
      {visible && (
        <aside aria-label="Install Calling Bell"
          className="fixed inset-x-3 bottom-3 z-[1100] flex items-center gap-3 rounded-2xl border border-line bg-surface p-3 shadow-[var(--cb-shadow-lg)] pb-[max(0.75rem,env(safe-area-inset-bottom))]">
          <img src="/icons/app-192.png" alt="" width={44} height={44} className="h-11 w-11 shrink-0 rounded-xl" />
          <div className="min-w-0 flex-1">
            <p className="text-sm font-semibold text-ink">Add Calling Bell to your home screen</p>
            <p className="text-xs text-muted">Opens like an app, full screen, in one tap.</p>
          </div>
          <Button size="small" variant="contained" disableElevation onClick={() => { dismiss(); void start(); }} sx={{ flexShrink: 0 }}>
            {mode === 'ios' ? 'How' : 'Install'}
          </Button>
          <IconButton size="small" onClick={dismiss} aria-label="Not now"><CloseRounded fontSize="small" /></IconButton>
        </aside>
      )}
      <IosInstallSteps open={iosSteps.open} onClose={iosSteps.close} />
    </>
  );
}

/** iPhone and iPad have no install prompt: Safari's Share menu has "Add to Home Screen". */
function IosInstallSteps({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs" aria-labelledby="ios-install-title">
      <DialogTitle id="ios-install-title" sx={{ pr: 6 }}>
        Add Calling Bell to your home screen
        <IconButton onClick={onClose} aria-label="Close" sx={{ position: 'absolute', right: 8, top: 8 }}><CloseRounded /></IconButton>
      </DialogTitle>
      <DialogContent>
        <ol className="space-y-4 text-sm text-ink">
          <li className="flex items-start gap-3">
            <span className="grid h-7 w-7 shrink-0 place-items-center rounded-full bg-subtle text-xs font-bold">1</span>
            <span>Tap the <strong>Share</strong> button <IosShareRounded sx={{ fontSize: 18, verticalAlign: 'text-bottom' }} aria-label="Share icon" /> in the browser bar.</span>
          </li>
          <li className="flex items-start gap-3">
            <span className="grid h-7 w-7 shrink-0 place-items-center rounded-full bg-subtle text-xs font-bold">2</span>
            <span>Scroll down and tap <strong>Add to Home Screen</strong> <AddBoxOutlined sx={{ fontSize: 18, verticalAlign: 'text-bottom' }} aria-hidden />.</span>
          </li>
          <li className="flex items-start gap-3">
            <span className="grid h-7 w-7 shrink-0 place-items-center rounded-full bg-subtle text-xs font-bold">3</span>
            <span>Tap <strong>Add</strong>. Calling Bell appears on your home screen and opens full screen, like an app.</span>
          </li>
        </ol>
        <Button fullWidth variant="contained" onClick={onClose} sx={{ mt: 3 }}>Got it</Button>
      </DialogContent>
    </Dialog>
  );
}
