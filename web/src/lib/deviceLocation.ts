import { useCallback, useEffect, useState } from 'react';
import { api } from './api';
import type { DeviceLocation } from './types';
import { useCity } from '@/stores/city';

/**
 * The visitor's device location (browser Geolocation API), used to pick their area more precisely than their IP address can.
 *
 * - Asked for on the first page of a session (useDeviceFirstLocation), and again whenever the visitor picks "Use my current location"
 *   (browsers show their own permission prompt; it needs HTTPS or localhost). The IP's location is the fallback.
 * - Tried at most once per browser session, so nobody is asked again on every page or keystroke.
 * - Never blocks anything: until (or unless) it succeeds, the existing location stays: the visitor's own choice, else the IP's.
 * - The position is rounded to about 100 m before it leaves the browser and is resolved by GET /api/geo/locate against our listed
 *   areas and cities; the server neither stores nor logs it.
 * - A visitor who picked a place by hand keeps it: the device location is only used automatically for an IP-based location.
 */
export type DeviceLocationStatus =
  /** No Geolocation API (or not a secure page). */
  | 'unsupported'
  /** Not tried this session; the browser will ask the visitor. */
  | 'prompt'
  | 'locating'
  | 'located'
  /** The visitor (or their browser settings) refused. */
  | 'denied'
  /** No position: location services off, timeout, or the lookup failed. */
  | 'unavailable'
  /** A position, but not near any listed city. */
  | 'outside';

const SESSION_KEY = 'cb-device-location';
const remembered = (): DeviceLocationStatus | null => {
  try { return (sessionStorage.getItem(SESSION_KEY) as DeviceLocationStatus | null) ?? null; } catch { return null; }
};
const remember = (s: DeviceLocationStatus) => { try { sessionStorage.setItem(SESSION_KEY, s); } catch { /* private mode */ } };

export const deviceLocationSupported = () => typeof window !== 'undefined' && window.isSecureContext && 'geolocation' in navigator;

async function permissionState(): Promise<PermissionState | null> {
  try { return (await navigator.permissions?.query({ name: 'geolocation' }))?.state ?? null; } catch { return null; }
}

function currentPosition(): Promise<GeolocationPosition> {
  return new Promise((resolve, reject) => navigator.geolocation.getCurrentPosition(resolve, reject, {
    // A city area is all that's needed: the faster network position is enough, and a recent one is reused.
    enableHighAccuracy: false, timeout: 10_000, maximumAge: 10 * 60_000,
  }));
}

/** ~100 m: enough to find the area, without sending the exact position. */
const round = (v: number) => Math.round(v * 1000) / 1000;

export function useDeviceLocation({ auto }: { auto: boolean }) {
  const [status, setStatus] = useState<DeviceLocationStatus>(() => (deviceLocationSupported() ? remembered() ?? 'prompt' : 'unsupported'));
  const [place, setPlace] = useState<DeviceLocation | null>(null);

  /** Selects the place at the device's position; false when there is none (refused, unavailable or outside the listed cities). */
  const locate = useCallback(async (): Promise<boolean> => {
    if (!deviceLocationSupported()) { setStatus('unsupported'); return false; }
    setStatus('locating');
    try {
      const { coords } = await currentPosition();
      const country = useCity.getState().countryCode;
      const found = await api.get<DeviceLocation | null>('/api/geo/locate', { lat: round(coords.latitude), lon: round(coords.longitude), country });
      if (!found) { remember('outside'); setStatus('outside'); return false; }
      useCity.getState().setDevice({
        countryCode: found.countryCode, citySlug: found.citySlug, areaId: found.areaId ?? null, cityName: found.cityName,
        stateSlug: found.stateSlug, stateName: found.stateName, areaSlug: found.areaSlug, areaName: found.areaName,
        areaPincode: found.postcode ?? null,
      });
      setPlace(found);
      remember('located');
      setStatus('located');
      return true;
    } catch (e) {
      const denied = typeof GeolocationPositionError !== 'undefined' && e instanceof GeolocationPositionError && e.code === e.PERMISSION_DENIED;
      const next = denied ? 'denied' : 'unavailable';
      remember(next);
      setStatus(next);
      return false;
    }
  }, []);

  // Opening the assistant: use the device location straight away when the browser already allows it (no prompt is shown), once per
  // session, and only over an IP-based location.
  useEffect(() => {
    if (!auto || status !== 'prompt') return;
    let cancelled = false;
    void permissionState().then((state) => {
      if (cancelled) return;
      if (state === 'denied') setStatus('denied');
      else if (state === 'granted' && useCity.getState().source !== 'user') void locate();
    });
    return () => { cancelled = true; };
  }, [auto, status, locate]);

  return { status, place, locate };
}

/**
 * The visitor's area from their device first (mount once, in the layout): once per browser session the browser is asked for the
 * position (its own permission prompt, unless already allowed or blocked). Until it answers, and whenever it can't (refused, location
 * services off, outside the listed cities), the IP's location stays. A place the visitor picked by hand is never replaced.
 */
export function useDeviceFirstLocation() {
  const { status, locate } = useDeviceLocation({ auto: false });
  useEffect(() => {
    if (status !== 'prompt') return;
    let cancelled = false;
    void permissionState().then((state) => {
      if (cancelled || useCity.getState().source === 'user') return;
      if (state === 'denied') { remember('denied'); return; }
      void locate();
    });
    return () => { cancelled = true; };
  }, [status, locate]);
}
