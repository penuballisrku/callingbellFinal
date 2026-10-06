import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Tooltip } from '@mui/material';
import { api } from '@/lib/api';
import { useCities, useCityAreas, useVisitorDistrict } from '@/lib/hooks';

export { useVisitorDistrict, type VisitorDistrict } from '@/lib/hooks';
import { useCity } from '@/stores/city';

interface VisitorCountry { countryCode: string; source: 'cdn' | 'geoip' | 'default' }

let regionNames: Intl.DisplayNames | null = null;
const countryName = (code: string) => {
  try { regionNames ??= new Intl.DisplayNames(['en-IN'], { type: 'region' }); return regionNames.of(code) ?? code; } catch { return code; }
};

/** The visitor's country (detected on the server from their IP address). */
export function useVisitorCountry() {
  return useQuery({
    queryKey: ['geo', 'country'],
    queryFn: () => api.get<VisitorCountry>('/api/geo/country'),
    staleTime: Infinity,
    retry: false,
  });
}

/** The visitor's country name (e.g. "India") from their IP address; null until detected. */
export function useVisitorCountryName(): string | null {
  const { data } = useVisitorCountry();
  return data?.countryCode ? countryName(data.countryCode) : null;
}

/** "India's real-time local business network." for the visitor's country (from their IP); neutral wording until it is known. */
export function useNetworkTagline(): string {
  const country = useVisitorCountryName();
  // Possessive: "India's", but "United States'" for names ending in s.
  return country ? `${country}${/s$/i.test(country) ? "'" : "'s"} real-time local business network.` : 'The real-time local business network.';
}

/**
 * Small ISO country code shown above the logo. The line height is reserved while loading so the header never shifts.
 */
export function CountryCode({ onDark }: { onDark?: boolean }) {
  const { data } = useVisitorCountry();
  const tone = onDark ? 'text-on-navy-faint' : 'text-muted';
  if (!data) return <span className="block h-3" aria-hidden />;
  const name = countryName(data.countryCode);
  return (
    <Tooltip title={data.source === 'default' ? `${name} (default)` : `Browsing from ${name}`} placement="bottom-start">
      <span className={`block h-3 w-fit text-[10px] font-semibold leading-3 tracking-[0.14em] ${tone}`} aria-label={`Country: ${name}`}>
        {data.countryCode}
      </span>
    </Tooltip>
  );
}

/** Pre-selects the visitor's detected district (and area, when the IP names one we list) unless they've picked a city themselves. */
export function useDistrictAutoSelect() {
  const { data: district } = useVisitorDistrict();
  const applyDetected = useCity((s) => s.applyDetected);
  // The server names the area's id, so the detection applies at once, without waiting for the (large) city list. Only an older
  // response without it falls back to looking the area up.
  const direct = !!district && (!district.areaSlug || !!district.areaId);
  const { data: cities } = useCities();
  const city = district && !direct ? cities?.find((c) => c.slug === district.citySlug) : undefined;
  const inlineArea = city?.areas.find((a) => a.slug === district?.areaSlug);
  // Cities outside the curated list carry no areas in the cities payload: look the area up in the city's full area list.
  const fetched = useCityAreas(city && district?.areaSlug && !inlineArea ? city.slug : null);
  useEffect(() => {
    if (district && direct) { applyDetected(district.citySlug, district.areaId ?? null); return; }
    if (!city) return;
    if (district?.areaSlug && !inlineArea && fetched.isLoading) return;
    applyDetected(city.slug, (inlineArea ?? fetched.areas.find((a) => a.slug === district?.areaSlug))?.id ?? null);
  }, [direct, city, district, inlineArea, fetched.isLoading, fetched.areas, applyDetected]);
}
