import { useMemo } from 'react';
import { Autocomplete, TextField, type FilterOptionsState } from '@mui/material';
import { useCities, useCountryCatalog } from '@/lib/hooks';
import { number } from '@/lib/format';
import type { City } from '@/lib/types';

/** How many cities the menu shows at once; typing narrows the whole country's list. */
const MAX_OPTIONS = 60;

/** Cities whose name (or state) matches the typed text: names starting with it first, then names containing it, then state matches. */
export function filterCities(cities: City[], text: string, limit = MAX_OPTIONS): City[] {
  const term = text.trim().toLowerCase();
  if (!term) return cities.slice(0, limit);
  const starts: City[] = [], contains: City[] = [], byState: City[] = [];
  for (const c of cities) {
    const name = c.name.toLowerCase();
    if (name.startsWith(term)) starts.push(c);
    else if (name.includes(term)) contains.push(c);
    else if (c.state.toLowerCase().startsWith(term)) byState.push(c);
    if (starts.length >= limit) break;
  }
  return [...starts, ...contains, ...byState].slice(0, limit);
}

/**
 * Searchable city picker over every city of the visitor's country (filled by the city catalogue agent). Value is a city slug;
 * clearing it gives null ("all cities" where the caller allows that).
 */
export function CityAutocomplete({ value, onChange, label, placeholder = 'Search city', size = 'small', fullWidth = true, required, error, helperText,
  ariaLabel = 'City', disabled }: {
  value: string | null | undefined; onChange: (slug: string | null) => void; label?: string; placeholder?: string; size?: 'small' | 'medium';
  fullWidth?: boolean; required?: boolean; error?: boolean; helperText?: React.ReactNode; ariaLabel?: string; disabled?: boolean;
}) {
  const { data: cities, isLoading } = useCities();
  const { data: catalog } = useCountryCatalog();
  const selected = useMemo(() => cities?.find((c) => c.slug === value) ?? null, [cities, value]);
  const importing = !!catalog?.importing;
  const country = catalog?.countryName ?? 'your country';

  return (
    <Autocomplete<City>
      options={cities ?? []}
      value={selected}
      onChange={(_, c) => onChange(c?.slug ?? null)}
      filterOptions={(options: City[], state: FilterOptionsState<City>) => filterCities(options, state.inputValue)}
      getOptionLabel={(c) => c.name}
      isOptionEqualToValue={(a, b) => a.slug === b.slug}
      loading={isLoading || importing}
      loadingText={importing ? `Loading all cities in ${country}…` : 'Loading cities…'}
      noOptionsText="No city matches. Try another spelling."
      size={size}
      fullWidth={fullWidth}
      disabled={disabled}
      autoHighlight
      renderOption={({ key, ...props }, c) => (
        <li key={key} {...props}>
          <span className="flex w-full min-w-0 items-baseline justify-between gap-3">
            <span className="min-w-0 truncate">{c.name}<span className="text-muted">, {c.state}</span></span>
            {c.businessCount > 0 && <span className="shrink-0 text-xs text-muted tabular">{number(c.businessCount)} listed</span>}
          </span>
        </li>
      )}
      renderInput={(params) => (
        <TextField {...params} label={label} placeholder={selected ? undefined : placeholder} required={required} error={error}
          helperText={helperText ?? (importing ? `Adding every city in ${country}…` : undefined)}
          inputProps={{ ...params.inputProps, 'aria-label': label ? undefined : ariaLabel }} />
      )}
    />
  );
}
