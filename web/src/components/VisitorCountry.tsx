import { useQuery } from '@tanstack/react-query';
import { Tooltip } from '@mui/material';
import { api } from '@/lib/api';

interface VisitorCountry { countryCode: string; source: 'cdn' | 'geoip' | 'default'; attribution?: { text: string; url: string } | null }

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

/** Credit required by the geolocation database licence (DB-IP Lite: CC BY 4.0). Renders nothing when not required. */
export function GeoAttribution({ className }: { className?: string }) {
  const { data } = useVisitorCountry();
  if (!data?.attribution) return null;
  return <a href={data.attribution.url} target="_blank" rel="noopener" className={className}>{data.attribution.text}</a>;
}
