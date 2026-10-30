import { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router';
import { Autocomplete, CircularProgress, TextField } from '@mui/material';
import LocationCityRounded from '@mui/icons-material/LocationCityRounded';
import MapOutlined from '@mui/icons-material/MapOutlined';
import NearMeOutlined from '@mui/icons-material/NearMeOutlined';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import { useCityAreas, useDebounced, useLocationSearch, usePageCity, useVisitorLocation } from '@/lib/hooks';
import { number } from '@/lib/format';
import type { LocationSuggestion } from '@/lib/types';
import { useCity } from '@/stores/city';

/** What the box offers: places from the server, plus "use my location" and "all of the city" when nothing is typed. */
type Option = LocationSuggestion | { kind: 'detected' } | { kind: 'whole-city' };

const optionKey = (o: Option) =>
  o.kind === 'area' ? `area:${o.areaId}` : o.kind === 'city' ? `city:${o.citySlug}` : o.kind;

/** "Hyderabad district" ("Jagtial district" for a town in it); a GeoNames name that already says "District" is left as it is. */
const districtLabel = (district: string) => (/district/i.test(district) ? district : `${district} district`);

/** Where a place is: its city and district (one name when they match), and its state. */
function whereLabel(city: string | null, district: string | null | undefined, state: string | null | undefined) {
  const place = !district ? city : !city || district.toLowerCase() === city.toLowerCase() ? districtLabel(district) : `${city}, ${districtLabel(district)}`;
  return [place, state].filter(Boolean).join(', ');
}

/** The typed text in bold wherever it appears in a name. */
function Highlight({ text, term }: { text: string; term: string }) {
  const i = term ? text.toLowerCase().indexOf(term.toLowerCase()) : -1;
  if (i < 0) return <>{text}</>;
  return <>{text.slice(0, i)}<strong className="font-semibold text-ink">{text.slice(i, i + term.length)}</strong>{text.slice(i + term.length)}</>;
}

/**
 * Location box: type a city, area, locality or PIN code and pick from the suggestions (GET /api/locations/search, the browsed city's
 * areas first). Each suggestion shows its city, state, PIN code and how many businesses are listed there. Picking one applies it at
 * once; a manual choice wins over the IP's location for the session. With nothing typed it offers popular cities, the whole of the
 * current city and (after a manual choice) the visitor's own location. On the results page the current search is refined in place.
 */
export function CitySelect({ size = 'small', fullWidth = false, height, bare = false }: {
  size?: 'small' | 'medium'; fullWidth?: boolean; height?: number;
  /** No outline of its own, for use inside a combined search bar. */
  bare?: boolean;
}) {
  const place = usePageCity();
  const setCity = useCity((s) => s.setCity);
  const describe = useCity((s) => s.describe);
  const resetToDetected = useCity((s) => s.resetToDetected);
  const navigate = useNavigate();
  const location = useLocation();
  const inputRef = useRef<HTMLInputElement>(null);

  const detected = useVisitorLocation();
  const country = detected.data?.country ?? null;
  const located = detected.data?.located ?? false;

  // Names for a city chosen elsewhere by its slug alone (e.g. a shared search link), from that city's area list.
  const needNames = !!place.citySlug && (!place.cityName || !place.stateSlug || !place.district
    || (!!place.areaId && (!place.areaName || !place.areaPincode)));
  const named = useCityAreas(needNames ? place.citySlug : null);
  useEffect(() => {
    const d = named.data;
    if (!d || !needNames || d.citySlug !== place.citySlug) return;
    const area = place.areaId ? d.areas.find((a) => a.id === place.areaId) : undefined;
    describe(d.citySlug, { cityName: d.cityName, stateSlug: d.stateSlug, stateName: d.state, district: area?.district ?? d.district,
      areaSlug: area?.slug, areaName: area?.name,
      areaPincode: area?.pincode || null });
  }, [named.data, needNames, place.citySlug, place.areaId, describe]);

  const [open, setOpen] = useState(false);
  // While the box has focus it shows what the visitor types; otherwise the current place.
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState('');
  const term = useDebounced(text.trim(), 200);
  const results = useLocationSearch(term, country, place.citySlug, open);

  // An area with its district and PIN code ("Mansoorabad, Hyderabad - 500068"); a city with its district when that's another name.
  const label = !place.ready ? 'Locating…'
    : place.citySlug && place.areaId && place.areaName
      ? `${place.areaName}, ${place.district ?? place.cityName ?? ''}${place.areaPincode ? ` - ${place.areaPincode}` : ''}`
      : place.citySlug ? (!place.cityName ? 'Loading…'
        // "Koratla, Jagitial district"; just "Hyderabad" when the district has the city's name.
        : place.district && place.district.toLowerCase() !== place.cityName.toLowerCase() ? whereLabel(place.cityName, place.district, null) : place.cityName)
        : 'All cities';
  const fullLabel = place.citySlug
    ? [place.areaId ? place.areaName : null, whereLabel(place.cityName, place.district, place.stateName), place.areaId && place.areaPincode ? `PIN ${place.areaPincode}` : null]
      .filter(Boolean).join(' · ')
    : label;

  const extras: Option[] = term ? [] : [
    ...(place.source === 'user' && located ? [{ kind: 'detected' } as const] : []),
    ...(place.citySlug && place.areaId ? [{ kind: 'whole-city' } as const] : []),
  ];
  const options: Option[] = [...extras, ...(results.data ?? [])];

  /** On the results page the current search is refined in place for the new place (replacing a place typed in the search). */
  const refineSearch = (citySlug: string | null, areaId: string | null) => {
    if (location.pathname !== '/search') return;
    const params = new URLSearchParams(location.search);
    if (citySlug) params.set('city', citySlug); else params.delete('city');
    if (areaId) params.set('area', areaId); else params.delete('area');
    ['place', 'tab', 'page'].forEach((k) => params.delete(k));
    navigate(`/search?${params}`, { replace: true });
  };

  const choose = (o: Option | null) => {
    if (!o) return;
    if (o.kind === 'detected') {
      resetToDetected();
      refineSearch(null, null);
    } else if (o.kind === 'whole-city') {
      // The city's own district is filled in again from its area list (the area's may differ).
      setCity(place.citySlug, null, { cityName: place.cityName, stateSlug: place.stateSlug, stateName: place.stateName });
      refineSearch(place.citySlug, null);
    } else {
      const areaId = o.kind === 'area' ? o.areaId ?? null : null;
      setCity(o.citySlug, areaId, { cityName: o.cityName, stateSlug: o.stateSlug, stateName: o.stateName, district: o.district ?? null,
        areaSlug: o.areaSlug, areaName: o.areaName, areaPincode: areaId ? o.pincode ?? null : null });
      refineSearch(o.citySlug, areaId);
    }
    setEditing(false);
    setText('');
    inputRef.current?.blur();
  };

  const rowHeight = height ?? (size === 'small' ? 40 : 48);
  const typing = editing && text.trim().length > 0;

  return (
    <Autocomplete<Option>
      open={open}
      onOpen={() => setOpen(true)}
      onClose={() => setOpen(false)}
      options={options}
      value={null}
      onChange={(_, o) => choose(o)}
      inputValue={editing ? text : label}
      onInputChange={(_, v, reason) => { if (reason === 'input') { setEditing(true); setText(v); } }}
      filterOptions={(x) => x}
      getOptionLabel={(o) => (o.kind === 'area' ? o.areaName ?? '' : o.kind === 'city' ? o.cityName : '')}
      isOptionEqualToValue={(a, b) => optionKey(a) === optionKey(b)}
      loading={results.isFetching && !results.data}
      loadingText="Finding places…"
      noOptionsText={results.isError ? 'Location search is unavailable right now. Please try again.'
        : term.length < 2 ? 'Type a city, area or PIN code' : `No city or area matches “${term}”`}
      forcePopupIcon={false}
      autoHighlight
      handleHomeEndKeys
      fullWidth={fullWidth}
      size={size}
      sx={fullWidth ? undefined : { width: 280, maxWidth: '100%' }}
      slotProps={{
        popper: { placement: 'bottom-start', sx: { width: 'min(400px, calc(100vw - 32px)) !important' } },
        listbox: { sx: { maxHeight: 400 } },
      }}
      renderOption={({ key: _key, ...props }, o) => {
        const icon = o.kind === 'detected' ? <NearMeOutlined fontSize="small" />
          : o.kind === 'whole-city' ? <MapOutlined fontSize="small" />
            : o.kind === 'city' ? <LocationCityRounded fontSize="small" /> : <PlaceOutlined fontSize="small" />;
        const [primary, secondary, count] =
          o.kind === 'detected' ? ['Use my current location', 'Detected from your connection', null]
            : o.kind === 'whole-city' ? [`All of ${place.cityName ?? 'the city'}`, 'Every area in the city', null]
              : o.kind === 'city' ? [o.cityName, `City · ${o.district ? `${districtLabel(o.district)}, ` : ''}${o.stateName}`, o.businessCount]
                : [o.areaName ?? '', [whereLabel(o.cityName, o.district, o.stateName), o.pincode && `PIN ${o.pincode}`].filter(Boolean).join(' · '), o.businessCount];
        const alias = o.kind === 'city' || o.kind === 'area' ? o.matched : null;
        return (
          <li key={optionKey(o)} {...props}>
            <span className="flex w-full min-w-0 items-center gap-3">
              <span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-subtle text-muted">{icon}</span>
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm text-ink-2"><Highlight text={primary} term={typing ? term : ''} /></span>
                <span className="block truncate text-xs text-muted">
                  {secondary}{alias && <> · {o.kind === 'area' ? 'includes' : 'also'} <Highlight text={alias} term={term} /></>}
                </span>
              </span>
              {count != null && count > 0 && <span className="shrink-0 text-xs text-muted tabular">{number(count)} listed</span>}
            </span>
          </li>
        );
      }}
      renderInput={(params) => (
        <TextField
          {...params}
          fullWidth
          inputRef={inputRef}
          placeholder="Search city, area or PIN code"
          // Focus empties the box for a new search; leaving it unchosen shows the current place again.
          onFocus={() => { setEditing(true); setText(''); }}
          onBlur={() => { setEditing(false); setText(''); }}
          slotProps={{
            input: {
              ...params.InputProps,
              startAdornment: <PlaceOutlined sx={{ fontSize: 18, color: 'text.secondary', ml: 0.5, mr: 0.5 }} />,
              endAdornment: results.isFetching && open && term ? <CircularProgress size={14} sx={{ mr: 1 }} /> : null,
            },
            htmlInput: { ...params.inputProps, 'aria-label': 'Location: type a city, area or PIN code', title: fullLabel },
          }}
          sx={{
            '& .MuiOutlinedInput-root': {
              height: rowHeight, py: '0 !important', pl: 1, fontSize: size === 'small' ? 14 : 15, fontWeight: 500,
              bgcolor: bare ? 'transparent' : 'background.paper',
            },
            '& .MuiOutlinedInput-input': { textOverflow: 'ellipsis' },
            ...(bare ? { '& fieldset': { border: 'none' } } : { '& fieldset': { borderColor: 'divider' } }),
          }}
        />
      )}
    />
  );
}
