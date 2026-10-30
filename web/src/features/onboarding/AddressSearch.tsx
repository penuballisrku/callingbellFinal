import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Autocomplete, CircularProgress, TextField } from '@mui/material';
import SearchRounded from '@mui/icons-material/SearchRounded';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import { api } from '@/lib/api';
import { useCountryCatalog, useDebounced } from '@/lib/hooks';
import type { AddressSuggestion, AddressSuggestions } from '@/lib/types';

/** Geoapify's free plan asks for this credit wherever its results are shown. */
export function GeoapifyCredit({ className = '' }: { className?: string }) {
  return (
    <span className={`text-[11px] text-muted ${className}`}>
      Powered by <a href="https://www.geoapify.com/" target="_blank" rel="noopener noreferrer" className="underline">Geoapify</a>
      {' · © '}<a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer" className="underline">OpenStreetMap contributors</a>
    </span>
  );
}

/**
 * "Find your address": type an address, pick a suggestion, and the form's city, area, address line, PIN code and map position are
 * filled in (GET /api/geo/address-suggestions; each suggestion comes with the listed city and area it lies in). Hidden when address
 * suggestions aren't available (Geoapify not configured or out of credits): the fields below still work by hand.
 */
export function AddressSearch({ onPick }: { onPick: (s: AddressSuggestion) => void }) {
  const [text, setText] = useState('');
  const term = useDebounced(text.trim(), 300);
  const { data: catalog } = useCountryCatalog();
  const country = catalog?.countryCode || null;
  const availability = useQuery({
    queryKey: ['address-suggestions', 'enabled'],
    queryFn: ({ signal }) => api.get<AddressSuggestions>('/api/geo/address-suggestions', undefined, signal),
    staleTime: 10 * 60_000,
  });
  const results = useQuery({
    queryKey: ['address-suggestions', country, term.toLowerCase()],
    queryFn: ({ signal }) => api.get<AddressSuggestions>('/api/geo/address-suggestions', { text: term, country }, signal),
    enabled: !!availability.data?.enabled && term.length >= 3,
    staleTime: 10 * 60_000,
    placeholderData: (previous) => previous,
  });
  if (!availability.data?.enabled) return null;

  const options = term.length >= 3 ? results.data?.items ?? [] : [];
  return (
    <div className="sm:col-span-2">
      <Autocomplete<AddressSuggestion, false, false, true>
        freeSolo
        options={options}
        filterOptions={(x) => x}
        inputValue={text}
        onInputChange={(_, v, reason) => { if (reason !== 'reset') setText(v); }}
        onChange={(_, s) => { if (s && typeof s !== 'string') { onPick(s); setText(''); } }}
        getOptionLabel={(s) => (typeof s === 'string' ? s : s.formatted)}
        loading={results.isFetching}
        noOptionsText={term.length < 3 ? 'Type at least 3 letters' : 'No matching address'}
        renderOption={({ key: _key, ...props }, s) => (
          <li key={`${s.latitude},${s.longitude},${s.formatted}`} {...props}>
            <span className="flex w-full min-w-0 items-start gap-2.5">
              <PlaceOutlined fontSize="small" className="mt-0.5 shrink-0 text-muted" />
              <span className="min-w-0">
                <span className="block truncate text-sm text-ink">{s.addressLine ?? s.areaName ?? s.locality ?? s.formatted}</span>
                <span className="block truncate text-xs text-muted">
                  {[s.areaName && s.addressLine ? s.areaName : null, s.cityName ?? s.district, s.state, s.postcode && `PIN ${s.postcode}`].filter(Boolean).join(' · ')}
                  {!s.citySlug && ' · city not listed yet'}
                </span>
              </span>
            </span>
          </li>
        )}
        renderInput={(params) => (
          <TextField {...params} label="Find your address" placeholder="Start typing your shop, building or street"
            helperText={<span className="flex flex-wrap items-center justify-between gap-x-3">
              <span>Pick a suggestion to fill in the city, area and PIN code</span><GeoapifyCredit />
            </span>}
            slotProps={{
              input: {
                ...params.InputProps,
                startAdornment: <SearchRounded fontSize="small" sx={{ color: 'text.secondary', ml: 0.5 }} />,
                endAdornment: results.isFetching ? <CircularProgress size={16} sx={{ mr: 1 }} /> : null,
              },
              htmlInput: params.inputProps,
            }} />
        )}
      />
    </div>
  );
}
