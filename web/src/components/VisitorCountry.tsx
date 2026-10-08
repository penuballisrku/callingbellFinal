import { Tooltip } from '@mui/material';
import { useLocationSync, useVisitorLocation } from '@/lib/hooks';

export { useVisitorDistrict, type VisitorDistrict } from '@/lib/hooks';

interface VisitorCountry { countryCode: string; source: 'geoip' | 'default' }

let regionNames: Intl.DisplayNames | null = null;
export const countryName = (code: string) => {
  try { regionNames ??= new Intl.DisplayNames(['en-IN'], { type: 'region' }); return regionNames.of(code) ?? code; } catch { return code; }
};

/**
 * The visitor's country, detected on the server from their IP address (part of the one IP location request). source "default" when
 * the IP couldn't be located (the server's best guess).
 */
export function useVisitorCountry() {
  const location = useVisitorLocation();
  const data: VisitorCountry | undefined = location.data?.country
    ? { countryCode: location.data.country, source: location.data.located ? 'geoip' : 'default' }
    : undefined;
  return { ...location, data };
}

/** The country being browsed: always the visitor's (from their IP); null until detected. */
export function useBrowsingCountryCode(): string | null {
  return useVisitorCountry().data?.countryCode ?? null;
}

/** The name of the country being browsed (e.g. "India"); null until detected. */
export function useVisitorCountryName(): string | null {
  const code = useBrowsingCountryCode();
  return code ? countryName(code) : null;
}

// Possessive: "India's", but "United States'" for names ending in s.
const possessive = (country: string) => `${country}${/s$/i.test(country) ? "'" : "'s"}`;

/** "India's real-time local business network." for the visitor's country (from their IP); neutral wording until it is known. */
export function useNetworkTagline(): string {
  const country = useVisitorCountryName();
  return country ? `${possessive(country)} real-time local business network.` : 'The real-time local business network.';
}

/**
 * Fills a country into database copy: "{country}" becomes "Canada" and "{country's}" becomes "Canada's" ("your country" while
 * the country is unknown).
 */
export const withCountry = (text: string, country: string | null) => text
  .replace(/\{country's\}/g, country ? possessive(country) : "your country's")
  .replace(/\{country\}/g, country ?? 'your country');

/**
 * Small ISO country code shown above the logo. The line height is reserved while loading so the header never shifts.
 */
export function CountryCode({ onDark }: { onDark?: boolean }) {
  const { data } = useVisitorCountry();
  const code = data?.countryCode;
  const tone = onDark ? 'text-on-navy-faint' : 'text-muted';
  if (!code) return <span className="block h-3" aria-hidden />;
  const name = countryName(code);
  const title = data.source === 'default' ? `${name} (default)` : `Browsing from ${name}`;
  return (
    <Tooltip title={title} placement="bottom-start">
      <span className={`block h-3 w-fit text-[10px] font-semibold leading-3 tracking-[0.14em] ${tone}`} aria-label={`Country: ${name}`}>
        {code}
      </span>
    </Tooltip>
  );
}

/** Keeps the selected place in step with the visitor's IP location (see useLocationSync). Mount once, in the layout. */
export function useDistrictAutoSelect() {
  useLocationSync();
}
