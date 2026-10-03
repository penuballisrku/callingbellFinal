import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Tooltip } from '@mui/material';
import { api } from '@/lib/api';
import { useCities } from '@/lib/hooks';
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

export interface VisitorDistrict { citySlug: string; cityName: string; state: string; areaSlug?: string | null; matchedBy: 'area' | 'city' | 'distance' }
/** Approximate visitor location from the IP (the IP itself is never sent to the browser). */
interface VisitorLocation {
  place?: string | null; region?: string | null; district?: VisitorDistrict | null;
  country?: string | null; postcode?: string | null; latitude?: number | null; longitude?: number | null;
}

/** The listed city (district) the visitor is browsing from, detected on the server from their IP address. */
export function useVisitorDistrict() {
  return useQuery({
    queryKey: ['geo', 'district'],
    queryFn: () => api.get<VisitorLocation>('/api/geo/district'),
    select: (d) => d.district ?? null,
    staleTime: Infinity,
    retry: false,
  });
}

/** Pre-selects the visitor's detected district (and area, when the IP names one we list) unless they've picked a city themselves. */
export function useDistrictAutoSelect() {
  const { data: district } = useVisitorDistrict();
  const { data: cities } = useCities();
  const applyDetected = useCity((s) => s.applyDetected);
  useEffect(() => {
    const city = district && cities?.find((c) => c.slug === district.citySlug);
    if (!city) return;
    applyDetected(city.slug, city.areas.find((a) => a.slug === district.areaSlug)?.id ?? null);
  }, [district, cities, applyDetected]);
}
