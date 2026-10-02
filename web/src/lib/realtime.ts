import { useEffect, useRef } from 'react';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useSnackbar } from 'notistack';
import { useAuth } from '@/stores/auth';
import type { NotificationItem } from './types';

export interface AvailabilityEvent { businessId: string; status: string; lastSeenOn: string }

let presence: HubConnection | null = null;
let presenceStart: Promise<void> | null = null;

function presenceConnection() {
  presence ??= new HubConnectionBuilder().withUrl('/hubs/presence').withAutomaticReconnect().configureLogging(LogLevel.Error).build();
  if (presence.state === HubConnectionState.Disconnected) {
    presenceStart ??= presence.start().catch(() => undefined).finally(() => { presenceStart = null; });
  }
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
    const conn = presenceConnection();
    const listener = (e: AvailabilityEvent) => handler.current(e);
    conn.on('availabilityChanged', listener);
    const watch = async () => {
      if (presenceStart) await presenceStart;
      if (conn.state === HubConnectionState.Connected) await conn.invoke('Watch', ids).catch(() => undefined);
    };
    void watch();
    conn.onreconnected(() => void watch());
    return () => {
      conn.off('availabilityChanged', listener);
      if (conn.state === HubConnectionState.Connected) void conn.invoke('Unwatch', ids).catch(() => undefined);
    };
  }, [key]);
}

// One notification connection per signed-in user, shared across layouts and re-mounts.
let notifications: { userId: string; conn: HubConnection } | null = null;

function notificationConnection(userId: string) {
  if (notifications?.userId !== userId) {
    void notifications?.conn.stop();
    const conn = new HubConnectionBuilder()
      .withUrl('/hubs/notifications', { accessTokenFactory: () => useAuth.getState().accessToken ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Error)
      .build();
    void conn.start().catch(() => undefined);
    notifications = { userId, conn };
  }
  return notifications.conn;
}

useAuth.subscribe((s) => {
  if (!s.user && notifications) { void notifications.conn.stop(); notifications = null; }
});

/** Live notifications for the signed-in user (new leads, bookings, review and listing updates). */
export function useNotificationStream() {
  const userId = useAuth((s) => s.user?.id);
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();

  useEffect(() => {
    if (!userId) return;
    const conn = notificationConnection(userId);
    const handler = (n: NotificationItem) => {
      enqueueSnackbar(`${n.title} - ${n.message}`, { variant: 'info' });
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
      if (n.notificationType === 'Lead') void queryClient.invalidateQueries({ queryKey: ['owner', 'leads'] });
      if (n.notificationType === 'Booking') {
        void queryClient.invalidateQueries({ queryKey: ['owner', 'bookings'] });
        void queryClient.invalidateQueries({ queryKey: ['me', 'bookings'] });
      }
    };
    conn.on('notification', handler);
    return () => conn.off('notification', handler);
  }, [userId, queryClient, enqueueSnackbar]);
}
