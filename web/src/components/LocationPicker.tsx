import { useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router';
import { Autocomplete, Button, CircularProgress, Popover, TextField } from '@mui/material';
import ExpandMoreRounded from '@mui/icons-material/ExpandMoreRounded';
import NearMeOutlined from '@mui/icons-material/NearMeOutlined';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import PublicRounded from '@mui/icons-material/PublicRounded';
import InfoOutlined from '@mui/icons-material/InfoOutlined';
import { useCities, useCityAreas, usePageCity, useStates, useVisitorLocation } from '@/lib/hooks';
import { number } from '@/lib/format';
import type { City, CityArea, StateRegion } from '@/lib/types';
import { useCity, type PlaceNames } from '@/stores/city';
import { filterCities } from './CityAutocomplete';
import { countryName } from './VisitorCountry';

/**
 * Areas whose name, PIN / postcode, alternate spelling or sub-locality matches ("Hydernagar" finds Kukatpally): names starting with
 * the text first, then names containing it, then postcode and alias matches.
 */
function filterAreas(areas: CityArea[], text: string): CityArea[] {
  const term = text.trim().toLowerCase();
  if (!term) return areas;
  const rank = (a: CityArea) => {
    const name = a.name.toLowerCase();
    if (name.startsWith(term)) return 0;
    if (name.includes(term)) return 1;
    if (a.pincode.toLowerCase().startsWith(term)) return 2;
    return a.altNames.some((n) => n.toLowerCase().includes(term)) || a.subLocalities.some((n) => n.toLowerCase().includes(term)) ? 3 : -1;
  };
  return areas.map((a) => ({ a, r: rank(a) })).filter((x) => x.r >= 0).sort((x, y) => x.r - y.r).map((x) => x.a);
}

/** The sub-locality or alternate name that matched a search, shown under the area's name. */
function matchedAlias(a: CityArea, text: string): string | null {
  const term = text.trim().toLowerCase();
  if (!term || a.name.toLowerCase().includes(term)) return null;
  return a.subLocalities.find((n) => n.toLowerCase().includes(term)) ?? a.altNames.find((n) => n.toLowerCase().includes(term)) ?? null;
}

/**
 * Location picker. The country is the visitor's (from their IP address, shown read-only); state / province / region, city and area are
 * filled in from the same IP lookup and can each be changed. Each level loads only for the level above: the selected state's cities,
 * the selected city's areas (GET /api/locations/cities/{slug}/areas, which the area-discovery agent fills for cities in any country).
 * Choosing a state clears the city and area; choosing a city clears the area; a manual choice wins over the IP for the session.
 * Choices made in the picker are a draft until an area is picked or the picker closes, so the page reloads its sections once, for the
 * final place, not for every step. On the results page the current search is refined in place.
 */
export function CitySelect({ size = 'small', fullWidth = false, height, bare = false }: {
  size?: 'small' | 'medium'; fullWidth?: boolean; height?: number;
  /** No outline of its own, for use inside a combined search bar. */
  bare?: boolean;
}) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const open = !!anchor;
  // Lists start loading when the visitor points at or focuses the button, so they are usually ready when it opens.
  const [warm, setWarm] = useState(false);
  const active = open || warm;
  const place = usePageCity();
  const setState = useCity((s) => s.setState);
  const setCity = useCity((s) => s.setCity);
  const describe = useCity((s) => s.describe);
  const resetToDetected = useCity((s) => s.resetToDetected);
  const navigate = useNavigate();
  const location = useLocation();

  const detected = useVisitorLocation();
  const country = detected.data?.country ?? null;
  const importing = !!detected.data?.importing;
  const located = detected.data?.located ?? false;
  const countryLabel = detected.data?.countryName ?? (country ? countryName(country) : null);

  // The picker's own copy of a changed state / city until it is applied (area picked, or the picker closed).
  const [draft, setDraft] = useState<{ stateSlug: string | null; stateName: string | null; citySlug: string | null; cityName: string | null } | null>(null);
  const view = draft
    ? { ...draft, areaId: null, areaSlug: null, areaName: null }
    : { stateSlug: place.stateSlug, stateName: place.stateName, citySlug: place.citySlug, cityName: place.cityName,
        areaId: place.areaId, areaSlug: place.areaSlug, areaName: place.areaName };

  const { data: states, isLoading: statesLoading } = useStates(active ? country : null);
  const { data: cities, isLoading: citiesLoading } = useCities(view.stateSlug, active && !!view.stateSlug);
  const cityAreas = useCityAreas(active ? view.citySlug : null);
  const areas = cityAreas.areas;
  // Names for a city chosen elsewhere by its slug alone (e.g. a shared search link), from that city's area list.
  const needNames = !!place.citySlug && (!place.cityName || !place.stateSlug || (!!place.areaId && !place.areaName));
  const named = useCityAreas(needNames ? place.citySlug : null);
  useEffect(() => {
    const d = named.data;
    if (!d || !needNames || d.citySlug !== place.citySlug) return;
    const area = place.areaId ? d.areas.find((a) => a.id === place.areaId) : undefined;
    describe(d.citySlug, { cityName: d.cityName, stateSlug: d.stateSlug, stateName: d.state, areaSlug: area?.slug, areaName: area?.name });
  }, [named.data, needNames, place.citySlug, place.areaId, describe]);

  // Current values stay shown while their lists load (no flash of empty fields); the lists replace them once loaded.
  const stateValue: StateRegion | null = states?.find((s) => s.slug === view.stateSlug)
    ?? (view.stateSlug ? { id: view.stateSlug, slug: view.stateSlug, name: view.stateName ?? '', cityCount: 0 } : null);
  const cityValue: City | null = cities?.find((c) => c.slug === view.citySlug)
    ?? (view.citySlug ? { id: view.citySlug, slug: view.citySlug, name: view.cityName ?? '', state: view.stateName ?? '', isPopular: false, businessCount: 0, areas: [] } : null);
  const areaValue: CityArea | null = areas.find((a) => a.id === view.areaId)
    ?? (view.areaId ? { id: view.areaId, slug: view.areaSlug ?? '', name: view.areaName ?? '', pincode: '', altNames: [], subLocalities: [] } : null);
  const cityOptions = useMemo(() => cities ?? [], [cities]);
  const [areaInput, setAreaInput] = useState('');

  /** On the results page the current search is refined in place for the new place (replacing a place typed in the search). */
  const refineSearch = (citySlug: string | null, areaId: string | null) => {
    if (location.pathname !== '/search') return;
    const params = new URLSearchParams(location.search);
    if (citySlug) params.set('city', citySlug); else params.delete('city');
    if (areaId) params.set('area', areaId); else params.delete('area');
    ['place', 'tab', 'page'].forEach((k) => params.delete(k));
    navigate(`/search?${params}`, { replace: true });
  };
  const chooseState = (s: StateRegion | null) => {
    if (s?.slug === view.stateSlug) return;
    setDraft({ stateSlug: s?.slug ?? null, stateName: s?.name ?? null, citySlug: null, cityName: null });
  };
  const chooseCity = (c: City | null) => {
    if (c?.slug === view.citySlug) return;
    setDraft({ stateSlug: view.stateSlug, stateName: view.stateName, citySlug: c?.slug ?? null, cityName: c?.name ?? null });
  };
  /** Applies the place shown in the picker (with an area, or the whole city / state). */
  const commit = (areaId: string | null = null, names: PlaceNames = {}) => {
    if (view.citySlug) setCity(view.citySlug, areaId, { cityName: view.cityName, stateSlug: view.stateSlug, stateName: view.stateName, ...names });
    else setState(view.stateSlug, view.stateName);
    refineSearch(view.citySlug, areaId);
    setDraft(null);
  };

  const close = () => {
    if (draft) commit();
    setAnchor(null);
    setAreaInput('');
  };

  const label = !place.ready ? 'Locating…'
    : place.citySlug ? (place.areaId && place.areaName ? `${place.areaName}, ${place.cityName ?? ''}` : place.cityName ?? 'Loading…')
      : 'All cities';

  return (
    <>
      <Button onClick={(e) => setAnchor(e.currentTarget)} onMouseEnter={() => setWarm(true)} onFocus={() => setWarm(true)}
        color="inherit" variant={bare ? 'text' : 'outlined'} fullWidth={fullWidth}
        aria-haspopup="dialog" aria-expanded={open} aria-label={`Location: ${label}. Change location`} title={label}
        startIcon={<PlaceOutlined sx={{ fontSize: '18px !important', color: 'text.secondary' }} />}
        endIcon={<ExpandMoreRounded sx={{ color: 'text.secondary', transition: 'transform .15s', transform: open ? 'rotate(180deg)' : 'none' }} />}
        sx={{
          height: height ?? (size === 'small' ? 40 : 48), justifyContent: 'flex-start', textTransform: 'none', fontWeight: 500,
          fontSize: size === 'small' ? 14 : 15, px: 1.5, minWidth: 0, borderColor: 'divider', bgcolor: bare ? 'transparent' : 'background.paper',
          ...(fullWidth ? {} : { width: 220, maxWidth: '100%' }),
          '& .MuiButton-endIcon': { ml: 'auto' },
          '&:hover': { borderColor: 'text.secondary', bgcolor: bare ? 'transparent' : 'background.paper' },
        }}>
        <span className="min-w-0 truncate text-left">{label}</span>
      </Button>

      <Popover open={open} anchorEl={anchor} onClose={close} anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
        transformOrigin={{ vertical: 'top', horizontal: 'left' }}
        slotProps={{ paper: { role: 'dialog', 'aria-label': 'Choose location', sx: { mt: 1, width: 380, maxWidth: 'calc(100vw - 32px)', p: 2 } } }}>
        <div className="mb-3 flex items-center justify-between gap-2">
          <h2 className="text-sm font-semibold">Choose location</h2>
          {place.source === 'user' && located && (
            <Button size="small" startIcon={<NearMeOutlined sx={{ fontSize: 16 }} />}
              onClick={() => { setDraft(null); resetToDetected(); refineSearch(null, null); setAnchor(null); setAreaInput(''); }}>
              Use my location
            </Button>
          )}
        </div>

        {/* The country comes from the visitor's connection; it isn't chosen here. */}
        <div className="mb-3 flex items-center gap-2 rounded-lg bg-subtle px-3 py-2 text-sm" aria-live="polite">
          <PublicRounded sx={{ fontSize: 18 }} className="shrink-0 text-muted" />
          {detected.isPending ? <span className="text-muted">Detecting your country…</span> : (
            <span className="min-w-0">
              <span className="font-medium text-ink">{countryLabel ?? 'Country unknown'}</span>
              <span className="block text-xs text-muted">
                {importing ? `Loading the cities of ${countryLabel}…` : located ? 'Detected from your internet connection' : 'Best guess from your connection'}
              </span>
            </span>
          )}
        </div>
        {!detected.isPending && (!located || (located && !detected.data?.district && !importing)) && place.source !== 'user' && (
          <p className="mb-3 flex gap-2 text-xs leading-5 text-muted" role="status">
            <InfoOutlined sx={{ fontSize: 16, mt: '2px' }} />
            We couldn’t automatically detect your city. Please select your location below.
          </p>
        )}

        <div className="flex flex-col gap-3">
          <Autocomplete<StateRegion>
            options={states ?? []}
            value={stateValue}
            onChange={(_, s) => chooseState(s)}
            getOptionLabel={(s) => s.name}
            isOptionEqualToValue={(a, b) => a.slug === b.slug}
            loading={statesLoading || importing}
            loadingText={importing ? `Adding the regions of ${countryLabel}…` : 'Loading…'}
            noOptionsText="No state or province matches"
            disabled={!country}
            autoHighlight
            size="small"
            renderOption={({ key: _key, ...props }, s) => (
              <li key={s.slug} {...props}>
                <span className="flex w-full items-baseline justify-between gap-3">
                  <span className="truncate">{s.name}</span>
                  <span className="shrink-0 text-xs text-muted tabular">{number(s.cityCount)} {s.cityCount === 1 ? 'city' : 'cities'}</span>
                </span>
              </li>
            )}
            renderInput={(params) => <TextField {...params} label="State / province / region" placeholder="Choose" />}
          />

          <Autocomplete<City>
            options={cityOptions}
            value={cityValue}
            onChange={(_, c) => { setAreaInput(''); chooseCity(c); }}
            filterOptions={(options, state) => filterCities(options, state.inputValue)}
            getOptionLabel={(c) => c.name}
            isOptionEqualToValue={(a, b) => a.slug === b.slug}
            loading={citiesLoading}
            loadingText="Loading cities…"
            noOptionsText="No city matches. Try another spelling."
            disabled={!view.stateSlug}
            autoHighlight
            size="small"
            renderOption={({ key: _key, ...props }, c) => (
              <li key={c.slug} {...props}>
                <span className="flex w-full min-w-0 items-baseline justify-between gap-3">
                  <span className="min-w-0 truncate">{c.name}</span>
                  {c.businessCount > 0 && <span className="shrink-0 text-xs text-muted tabular">{number(c.businessCount)} listed</span>}
                </span>
              </li>
            )}
            renderInput={(params) => (
              <TextField {...params} label="City" placeholder={view.stateSlug ? 'All cities' : 'Choose a state first'}
                helperText={!draft && place.source === 'ip' && place.citySlug ? 'Detected from your location' : undefined} />
            )}
          />

          <Autocomplete<CityArea>
            options={areas}
            value={areaValue}
            inputValue={areaInput || (areaValue?.name ?? '')}
            onInputChange={(_, v, reason) => { if (reason === 'input' || reason === 'clear') setAreaInput(v); }}
            onChange={(_, a) => {
              setAreaInput('');
              if (!view.citySlug) return;
              commit(a?.id ?? null, { areaSlug: a?.slug ?? null, areaName: a?.name ?? null });
              if (a) { setAnchor(null); }
            }}
            filterOptions={(options, state) => filterAreas(options, areaInput ? state.inputValue : '').slice(0, 200)}
            getOptionLabel={(a) => a.name}
            isOptionEqualToValue={(a, b) => a.id === b.id}
            disabled={!view.citySlug}
            loading={cityAreas.isLoading || (cityAreas.discovering && areas.length === 0)}
            loadingText={view.cityName ? `Finding areas in ${view.cityName}…` : 'Loading…'}
            noOptionsText={areaInput ? 'No area matches' : 'No areas listed yet'}
            autoHighlight
            size="small"
            renderOption={({ key: _key, ...props }, a) => {
              const alias = matchedAlias(a, areaInput);
              return (
                <li key={a.id} {...props}>
                  <span className="flex w-full min-w-0 items-baseline justify-between gap-3">
                    <span className="min-w-0">
                      <span className="block truncate">{a.name}</span>
                      {alias && <span className="block truncate text-xs text-muted">includes {alias}</span>}
                    </span>
                    {a.pincode && <span className="shrink-0 text-xs text-faint tabular">{a.pincode}</span>}
                  </span>
                </li>
              );
            }}
            renderInput={(params) => (
              <TextField {...params} label="Area / neighbourhood" placeholder={view.citySlug ? `All of ${view.cityName ?? 'the city'}` : 'Choose a city first'}
                helperText={!view.citySlug ? undefined
                  : cityAreas.discovering ? <span className="inline-flex items-center gap-1.5"><CircularProgress size={10} />Finding more areas in {view.cityName}…</span>
                  : cityAreas.data && areas.length === 0 ? `No areas listed for ${view.cityName} yet. Results cover the whole city.`
                  : cityAreas.data ? `${number(areas.length)} areas${cityAreas.data.subLocalityCount ? ` · ${number(cityAreas.data.subLocalityCount)} localities` : ''}`
                  : undefined} />
            )}
          />
        </div>

        <div className="mt-4 flex justify-end">
          <Button variant="contained" size="small" onClick={close}>Done</Button>
        </div>
      </Popover>
    </>
  );
}
