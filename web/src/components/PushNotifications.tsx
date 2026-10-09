import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { useQueryClient } from '@tanstack/react-query';
import { useSnackbar } from 'notistack';
import { Button, CircularProgress, Switch } from '@mui/material';
import NotificationsActiveOutlined from '@mui/icons-material/NotificationsActiveOutlined';
import NotificationsOffOutlined from '@mui/icons-material/NotificationsOffOutlined';
import { disablePush, enablePush, getPushState, syncPush, type PushState } from '@/lib/push';

/**
 * While signed in: keeps this browser's push registration fresh (only if the user already allowed notifications - it never asks by
 * itself), and handles messages from the push service worker: open the page a notification links to, refresh the notification list.
 */
export function usePushBridge() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  useEffect(() => {
    void syncPush().catch(() => undefined);
    if (!('serviceWorker' in navigator)) return;
    const onMessage = (event: MessageEvent<{ type?: string; url?: string }>) => {
      if (event.data?.type === 'cb-navigate' && event.data.url?.startsWith('/')) navigate(event.data.url);
      if (event.data?.type === 'cb-push') void queryClient.invalidateQueries({ queryKey: ['notifications'] });
    };
    navigator.serviceWorker.addEventListener('message', onMessage);
    return () => navigator.serviceWorker.removeEventListener('message', onMessage);
  }, [navigate, queryClient]);
}

export function usePushState() {
  const [state, setState] = useState<PushState | null>(null);
  const refresh = useCallback(() => { void getPushState().then(setState); }, []);
  useEffect(refresh, [refresh]);
  return { state, setState, refresh };
}

/**
 * "Browser notifications" switch for the notification menu: turning it on asks the browser's permission (from this click) and registers
 * the browser; off unregisters it. Hidden where the browser or the server doesn't support web push.
 */
export function PushNotificationToggle() {
  const { enqueueSnackbar } = useSnackbar();
  const { state, setState } = usePushState();
  const [busy, setBusy] = useState(false);
  if (state === null || state === 'unsupported' || state === 'unconfigured') return null;

  const toggle = async () => {
    setBusy(true);
    try {
      if (state === 'enabled') {
        await disablePush();
        setState('default');
        enqueueSnackbar('Browser notifications turned off', { variant: 'info' });
      } else {
        const next = await enablePush();
        setState(next);
        if (next === 'enabled') enqueueSnackbar('Browser notifications are on. You’ll hear about new leads and bookings at once.', { variant: 'success' });
        else if (next === 'denied') enqueueSnackbar('Notifications are blocked for this site. Allow them in your browser’s site settings.', { variant: 'warning' });
      }
    } catch {
      enqueueSnackbar('Couldn’t change browser notifications. Please try again.', { variant: 'error' });
    } finally {
      setBusy(false);
    }
  };

  const on = state === 'enabled';
  return (
    <div className="flex items-start gap-3 border-b border-line bg-subtle/60 px-4 py-3">
      <span className={`mt-0.5 flex ${on ? 'text-success' : 'text-muted'}`} aria-hidden>
        {on ? <NotificationsActiveOutlined fontSize="small" /> : <NotificationsOffOutlined fontSize="small" />}
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-semibold">Browser notifications</p>
        <p className="text-xs text-muted">
          {state === 'denied'
            ? 'Blocked in this browser. Allow notifications for this site in your browser settings.'
            : on ? 'On for this browser. New leads and bookings pop up even when this tab is closed.'
              : 'Get new leads and bookings instantly, even when this tab is closed.'}
        </p>
      </div>
      {state === 'denied' ? null : busy ? <CircularProgress size={20} sx={{ mt: 0.5 }} /> : state === 'default' ? (
        <Button size="small" variant="contained" disableElevation onClick={() => void toggle()} sx={{ flexShrink: 0 }}>Turn on</Button>
      ) : (
        <Switch checked={on} onChange={() => void toggle()} slotProps={{ input: { 'aria-label': 'Browser notifications' } }} />
      )}
    </div>
  );
}
