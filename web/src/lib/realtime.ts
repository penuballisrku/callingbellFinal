import { useEffect, useRef } from 'react';
import type { HubConnection } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useSnackbar } from 'notistack';
import { useAuth } from '@/stores/auth';
import type { BusinessGrowthStats, HomeData, MarketingPage, NotificationItem } from './types';

export interface AvailabilityEvent { businessId: string; status: string; lastSeenOn: string }

/**
 * The SignalR client is loaded on first use (a separate chunk), not with the first page: visitors who never see live
 * availability or sign in don't download it, and the first paint isn't held up by it.
 */
const signalR = () => import('@microsoft/signalr');

let presence: Promise<HubConnection> | null = null;

/** The shared presence connection, started once; resolves when it is ready to use (or failed to connect). */
function presenceConnection(): Promise<HubConnection> {
  presence ??= signalR().then(async ({ HubConnectionBuilder, LogLevel }) => {
    const conn = new HubConnectionBuilder().withUrl('/hubs/presence').withAutomaticReconnect().configureLogging(LogLevel.Error).build();
    await conn.start().catch(() => undefined);
    return conn;
  });
  return presence;
}

/**
 * Subscribes to live availability for the businesses currently on screen.
 * The callback receives status changes pushed by PresenceHub.
 */
export function usePresence(businessIds: string[], onChange: (e: AvailabilityEvent) => void) {
  const handler = useRef(onChange);
  handler.current = onChange;
  const key = [...businessIds].sort().join(',');

  useEffect(() => {
    if (!key) return;
    const ids = key.split(',');
    let active = true;
    let conn: HubConnection | null = null;
    // Returns nothing: SignalR treats a returned value as a reply to the server and logs an error.
    const listener = (e: AvailabilityEvent) => { handler.current(e); };
    const watch = async () => {
      if (active && conn?.state === 'Connected') await conn.invoke('Watch', ids).catch(() => undefined);
    };
    void presenceConnection().then((c) => {
      if (!active) return;
      conn = c;
      c.on('availabilityChanged', listener);
      c.onreconnected(() => void watch());
      void watch();
    });
    return () => {
      active = false;
      if (!conn) return;
      conn.off('availabilityChanged', listener);
      if (conn.state === 'Connected') void conn.invoke('Unwatch', ids).catch(() => undefined);
    };
  }, [key]);
}

/**
 * Keeps the platform figures on screen (listed businesses, published reviews, completed bookings…) current while the page is open.
 * The API pushes new figures over PresenceHub when a listing, review or booking changes; they are written into the cached home and
 * marketing page data, so the page shows them without another request. After a lost connection the pages are refetched instead.
 */
export function useLivePlatformStats() {
  const queryClient = useQueryClient();

  useEffect(() => {
    let active = true;
    let conn: HubConnection | null = null;
    const listener = (s: BusinessGrowthStats) => {
      queryClient.setQueriesData<HomeData>({ queryKey: ['home'] }, (old) => old && {
        ...old, stats: { businesses: s.activeBusinesses, cities: s.cities, reviews: s.reviews, bookingsCompleted: s.bookingsCompleted },
      });
      queryClient.setQueriesData<MarketingPage>({ queryKey: ['content'] }, (old) => old?.stats ? { ...old, stats: s } : old);
    };
    const watch = async () => {
      if (active && conn?.state === 'Connected') await conn.invoke('WatchStats').catch(() => undefined);
    };
    const resync = () => {
      if (!active) return;
      void watch();
      void queryClient.invalidateQueries({ queryKey: ['home'] });
      void queryClient.invalidateQueries({ queryKey: ['content'] });
    };
    void presenceConnection().then((c) => {
      if (!active) return;
      conn = c;
      c.on('platformStats', listener);
      c.onreconnected(resync);
      void watch();
    });
    return () => {
      active = false;
      if (!conn) return;
      conn.off('platformStats', listener);
      if (conn.state === 'Connected') void conn.invoke('UnwatchStats').catch(() => undefined);
    };
  }, [queryClient]);
}

// One notification connection per signed-in user, shared across layouts and re-mounts.
let notifications: { userId: string; conn: Promise<HubConnection> } | null = null;

function notificationConnection(userId: string): Promise<HubConnection> {
  if (notifications?.userId !== userId) {
    void notifications?.conn.then((c) => c.stop());
    const conn = signalR().then(({ HubConnectionBuilder, LogLevel }) => {
      const c = new HubConnectionBuilder()
        .withUrl('/hubs/notifications', { accessTokenFactory: () => useAuth.getState().accessToken ?? '' })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Error)
        .build();
      void c.start().catch(() => undefined);
      return c;
    });
    notifications = { userId, conn };
  }
  return notifications.conn;
}

useAuth.subscribe((s) => {
  if (!s.user && notifications) { void notifications.conn.then((c) => c.stop()); notifications = null; }
});

/** Live notifications for the signed-in user (new leads, bookings, review and listing updates). */
export function useNotificationStream() {
  const userId = useAuth((s) => s.user?.id);
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();

  useEffect(() => {
    if (!userId) return;
    let active = true;
    let conn: HubConnection | null = null;
    const handler = (n: NotificationItem) => {
      enqueueSnackbar(`${n.title} - ${n.message}`, { variant: 'info' });
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
      if (n.notificationType === 'Lead') void queryClient.invalidateQueries({ queryKey: ['owner', 'leads'] });
      if (n.notificationType === 'Booking') {
        void queryClient.invalidateQueries({ queryKey: ['owner', 'bookings'] });
        void queryClient.invalidateQueries({ queryKey: ['me', 'bookings'] });
      }
    };
    void notificationConnection(userId).then((c) => {
      if (!active) return;
      conn = c;
      c.on('notification', handler);
    });
    return () => { active = false; conn?.off('notification', handler); };
  }, [userId, queryClient, enqueueSnackbar]);
}
