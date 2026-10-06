import { useEffect, useState } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router';
import { Button, Drawer, IconButton, InputAdornment, ListSubheader, MenuItem, Select, TextField } from '@mui/material';
import AutoAwesomeRounded from '@mui/icons-material/AutoAwesomeRounded';
import MenuRounded from '@mui/icons-material/MenuRounded';
import NearMeOutlined from '@mui/icons-material/NearMeOutlined';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import SearchRounded from '@mui/icons-material/SearchRounded';
import { useCategories, useCities, useCityAreas, useCountryCatalog } from '@/lib/hooks';
import { number } from '@/lib/format';
import { filterCities } from '@/components/CityAutocomplete';
import type { CityArea } from '@/lib/types';
import { useNotificationStream } from '@/lib/realtime';
import { homeFor, isAdmin, isOwner, useAuth } from '@/stores/auth';
import { useCity } from '@/stores/city';
import { Logo, NotificationBell, ThemeMenu, UserMenu } from './Shared';
import { CountryCode, useDistrictAutoSelect, useNetworkTagline, useVisitorDistrict } from '@/components/VisitorCountry';
import { AskAiButton, SearchAssistant } from '@/features/assistant/SearchAssistant';
import { useAssistant } from '@/features/assistant/store';

const AREA = 'area:';

/**
 * City picker. Lists every area of the selected city (or of the visitor's IP-detected city while none is selected), loaded from
 * /api/locations/cities/{slug}/areas, which the area-discovery agent keeps complete. A search box filters by area name, PIN code,
 * alternate spelling or sub-locality ("Hydernagar" finds Kukatpally); picking an area opens search filtered to it.
 */
export function CitySelect({ size = 'small', fullWidth = false, height, bare = false }: {
  size?: 'small' | 'medium'; fullWidth?: boolean; height?: number;
  /** No outline of its own, for use inside a combined search bar. */
  bare?: boolean;
}) {
  const { data: cities } = useCities();
  const { data: district } = useVisitorDistrict();
  const { citySlug, areaId, setCity } = useCity();
  const navigate = useNavigate();
  const location = useLocation();
  const [areaFilter, setAreaFilter] = useState('');
  const [cityFilter, setCityFilter] = useState('');
  const { data: catalog } = useCountryCatalog();

  const areaCity = cities?.find((c) => c.slug === (citySlug ?? district?.citySlug));
  const isDistrict = !!areaCity && areaCity.slug === district?.citySlug;
  // While the area-discovery agent works on this city the hook polls, so new areas appear without a reload.
  const { data: cityAreas } = useCityAreas(areaCity?.slug);
  // Every city of the visitor's country: the first ones (curated, then largest) until the visitor types, then matches from the whole list.
  // A typed search also matches the current city, so "hyd" in Hyderabad doesn't claim there is no such city.
  const otherCities = cityFilter.trim()
    ? filterCities(cities ?? [], cityFilter, 60)
    : filterCities((cities ?? []).filter((c) => c.slug !== areaCity?.slug), '', 30);
  // The full list once loaded; the cities payload's areas meanwhile.
  const areas: CityArea[] = cityAreas?.areas ?? areaCity?.areas.map((a) => ({ ...a, areaType: null, altNames: [], subLocalities: [] })) ?? [];
  const term = areaFilter.trim().toLowerCase();
  // `via` names the alias or sub-locality that matched, shown under the area name.
  const filteredAreas: { a: CityArea; via: string | null }[] = !term ? areas.map((a) => ({ a, via: null })) : areas.flatMap((a): { a: CityArea; via: string | null }[] => {
    if (a.name.toLowerCase().includes(term) || a.pincode.startsWith(term)) return [{ a, via: null }];
    const alias = a.altNames.find((n) => n.toLowerCase().includes(term)) ?? a.subLocalities.find((n) => n.toLowerCase().includes(term));
    return alias ? [{ a, via: alias }] : [];
  });
  const value = !cities ? '' : areaId && areas.some((a) => a.id === areaId) ? AREA + areaId : citySlug ?? '';

  const onChange = (v: string) => {
    const isArea = v.startsWith(AREA) && !!areaCity;
    const nextCity = isArea ? areaCity!.slug : v || null;
    const id = isArea ? v.slice(AREA.length) : null;
    setCity(nextCity, id);
    // Choosing a location only remembers it; results load when the visitor presses Search. On the results page the
    // current search is refined in place for the new location (replacing a place typed in the search, e.g. "in Nellore").
    if (location.pathname !== '/search') return;
    const params = new URLSearchParams(location.search);
    if (nextCity) params.set('city', nextCity); else params.delete('city');
    if (id) params.set('area', id); else params.delete('area');
    params.delete('place');
    params.delete('tab');
    params.delete('page');
    navigate(`/search?${params}`, { replace: true });
  };

  const label = (v: string) => {
    // While the city list loads the value is empty; don't claim "All cities" for a visitor who has picked one.
    if (!v) return !cities && citySlug ? 'Loading…' : 'All cities';
    if (v.startsWith(AREA)) return `${areas.find((a) => AREA + a.id === v)?.name}, ${areaCity?.name}`;
    return cities?.find((c) => c.slug === v)?.name ?? '';
  };

  return (
    <Select value={value} displayEmpty onChange={(e) => onChange(e.target.value)} size={size} renderValue={label} fullWidth={fullWidth}
      startAdornment={<PlaceOutlined sx={{ fontSize: 18, mr: 0.5, color: 'text.secondary', flexShrink: 0 }} />}
      // autoFocus off so the area search box keeps focus; the filter resets each time the menu closes.
      MenuProps={{ autoFocus: false, slotProps: { paper: { sx: { maxHeight: 480 } } } }} onClose={() => { setAreaFilter(''); setCityFilter(''); }}
      title={label(value)}
      sx={{
        // A fixed width in the header, so a long area name is cut short ("Jubilee Hills, Hyd…") instead of shifting the items beside it.
        ...(fullWidth ? { minWidth: 0 } : { width: 220, maxWidth: '100%' }), height,
        ...(bare && { '& .MuiOutlinedInput-notchedOutline': { border: 'none' }, bgcolor: 'transparent' }),
        '& .MuiSelect-select': { py: size === 'small' ? '7px' : undefined, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
      }}
      inputProps={{ 'aria-label': 'Choose city or area' }}>
      <MenuItem value="">All cities</MenuItem>
      {areaCity && [
        <ListSubheader key="areas-h" sx={{ lineHeight: '32px', fontSize: 12, fontWeight: 600, display: 'flex', alignItems: 'center', gap: 0.5 }}>
          {isDistrict && <NearMeOutlined sx={{ fontSize: 14 }} />}
          {isDistrict ? `Near you · ${areaCity.name}` : `Areas in ${areaCity.name}`}
          <span className="ml-auto font-normal text-faint">
            {cityAreas?.discovering ? 'Finding more areas…' : `${areas.length}${cityAreas?.subLocalityCount ? ` · ${cityAreas.subLocalityCount} localities` : ''}`}
          </span>
        </ListSubheader>,
        areas.length > 8 && (
          <ListSubheader key="areas-search" sx={{ py: 1, lineHeight: 'normal' }}>
            <TextField size="small" fullWidth autoFocus value={areaFilter} placeholder="Search area, locality or PIN code"
              onChange={(e) => setAreaFilter(e.target.value)}
              // Keep typing in the box instead of triggering the menu's own type-ahead; Escape still closes the menu.
              onKeyDown={(e) => { if (e.key !== 'Escape') e.stopPropagation(); }}
              slotProps={{
                htmlInput: { 'aria-label': `Search areas in ${areaCity.name}` },
                input: { startAdornment: <InputAdornment position="start"><SearchRounded fontSize="small" /></InputAdornment> },
              }} />
          </ListSubheader>
        ),
        <MenuItem key={areaCity.slug} value={areaCity.slug}>All of {areaCity.name}</MenuItem>,
        ...filteredAreas.map(({ a, via }) => (
          <MenuItem key={a.id} value={AREA + a.id} sx={{ pl: 3.5, justifyContent: 'space-between', gap: 2 }}>
            <span className="min-w-0">
              <span className="block truncate">{a.name}</span>
              {via && <span className="block truncate text-xs text-muted">includes {via}</span>}
            </span>
            <span className="shrink-0 text-xs text-faint">{a.pincode}</span>
          </MenuItem>
        )),
        areaFilter && filteredAreas.length === 0 && (
          <MenuItem key="areas-none" disabled sx={{ pl: 3.5, fontSize: 14 }}>No areas match “{areaFilter}”</MenuItem>
        ),
      ]}
      <ListSubheader key="cities-h" sx={{ lineHeight: '32px', fontSize: 12, fontWeight: 600, display: 'flex', alignItems: 'center' }}>
        {areaCity ? 'Other cities' : 'Cities'}
        <span className="ml-auto font-normal text-faint">
          {catalog?.importing ? `Adding every city in ${catalog.countryName ?? 'your country'}…` : cities ? `${number(cities.length)} in ${catalog?.countryName ?? 'your country'}` : ''}
        </span>
      </ListSubheader>
      <ListSubheader key="cities-search" sx={{ py: 1, lineHeight: 'normal' }}>
        <TextField size="small" fullWidth value={cityFilter} placeholder="Search any city or state"
          onChange={(e) => setCityFilter(e.target.value)}
          onKeyDown={(e) => { if (e.key !== 'Escape') e.stopPropagation(); }}
          slotProps={{
            htmlInput: { 'aria-label': 'Search cities' },
            input: { startAdornment: <InputAdornment position="start"><SearchRounded fontSize="small" /></InputAdornment> },
          }} />
      </ListSubheader>
      {otherCities.map((c) => (
        // Prefixed: the current city's slug is also the key of its "All of …" item above.
        <MenuItem key={`city:${c.slug}`} value={c.slug} sx={{ justifyContent: 'space-between', gap: 2 }}>
          <span className="min-w-0 truncate">{c.name}<span className="text-xs text-muted">, {c.state}</span></span>
          {c.businessCount > 0 && <span className="shrink-0 text-xs text-faint tabular">{number(c.businessCount)}</span>}
        </MenuItem>
      ))}
      {cityFilter.trim() && otherCities.length === 0 && <MenuItem key="cities-none" disabled sx={{ fontSize: 14 }}>No city matches “{cityFilter}”</MenuItem>}
    </Select>
  );
}

const nav = [
  { to: '/categories', label: 'Categories' },
  { to: '/search?availability=now', label: 'Available now' },
  { to: '/nearby', label: 'Explore nearby' },
  { to: '/list-your-business', label: 'For business' },
];

export default function CustomerLayout() {
  const user = useAuth((s) => s.user);
  const [open, setOpen] = useState(false);
  const openAssistant = useAssistant((s) => s.openAssistant);
  const location = useLocation();
  useNotificationStream();
  useDistrictAutoSelect();
  useEffect(() => { window.scrollTo({ top: 0 }); setOpen(false); }, [location.pathname]);

  return (
    <div className="flex min-h-screen flex-col">
      <header className="sticky top-0 z-40 border-b border-line bg-surface/85 backdrop-blur-md backdrop-saturate-150">
        {/* One row at a fixed height; every control is 40px tall and centred on it, and labels never wrap. */}
        <div className="container-page flex h-[72px] items-center gap-2 xl:gap-3">
          <div className="flex shrink-0 items-start gap-1">
            <Logo />
            <CountryCode />
          </div>
          <div className="flex-1" />
          {/* The links need the full width; below it they are in the menu, so the header never overflows. */}
          <nav className="hidden shrink-0 items-center gap-0.5 xl:flex" aria-label="Main">
            {nav.map((n) => (
              <NavLink key={n.to} to={n.to} className={({ isActive }) => `inline-flex h-10 items-center whitespace-nowrap rounded-lg px-2.5 text-sm font-medium transition-colors hover:bg-subtle hover:text-ink 2xl:px-3 ${isActive ? 'bg-subtle text-ink' : 'text-muted'}`}>{n.label}</NavLink>
            ))}
          </nav>
          <div className="hidden shrink-0 items-center lg:flex"><AskAiButton /></div>
          <div className="flex shrink-0 items-center lg:hidden"><AskAiButton compact /></div>
          <div className="hidden shrink-0 items-center md:flex"><CitySelect height={40} /></div>
          <div className="hidden shrink-0 items-center sm:flex"><ThemeMenu /></div>
          {user ? (
            <div className="flex shrink-0 items-center gap-1">
              <NotificationBell />
              <UserMenu />
            </div>
          ) : (
            <div className="hidden shrink-0 items-center gap-2 sm:flex">
              <Button component={Link} to="/login" color="inherit" sx={{ height: 40, px: 1.5, whiteSpace: 'nowrap' }}>Sign in</Button>
              <Button component={Link} to="/register" variant="contained" sx={{ height: 40, px: 2, whiteSpace: 'nowrap' }}>Join free</Button>
            </div>
          )}
          <IconButton className="shrink-0 xl:!hidden" onClick={() => setOpen(true)} aria-label="Open menu"><MenuRounded /></IconButton>
        </div>
      </header>

      <Drawer anchor="right" open={open} onClose={() => setOpen(false)} slotProps={{ paper: { sx: { width: 300, p: 2 } } }}>
        <div className="mb-4 flex items-center justify-between"><Logo /><ThemeMenu /></div>
        <div className="mb-4"><CitySelect size="medium" fullWidth /></div>
        <nav className="flex flex-col gap-1" aria-label="Mobile">
          {nav.map((n) => <Link key={n.to} to={n.to} className="rounded-lg px-3 py-2.5 font-medium hover:bg-subtle">{n.label}</Link>)}
          {user && <Link to={homeFor(user) === '/' ? '/account' : homeFor(user)} className="rounded-lg px-3 py-2.5 font-medium hover:bg-subtle">
            {isAdmin(user) ? 'Admin console' : isOwner(user) ? 'Business dashboard' : 'My account'}</Link>}
          <button type="button" onClick={() => { setOpen(false); openAssistant(); }}
            className="flex items-center gap-2 rounded-lg px-3 py-2.5 text-left font-medium hover:bg-subtle">
            <AutoAwesomeRounded className="text-accent-ink" sx={{ fontSize: 20 }} /> Ask the AI assistant
          </button>
        </nav>
        {!user && <div className="mt-6 flex flex-col gap-2">
          <Button component={Link} to="/register" variant="contained" size="large">Join free</Button>
          <Button component={Link} to="/login" variant="outlined" size="large">Sign in</Button>
        </div>}
      </Drawer>
      <SearchAssistant />

      <main className="flex-1"><Outlet /></main>
      <Footer />
    </div>
  );
}

function Footer() {
  const tagline = useNetworkTagline();
  const { data: categories } = useCategories();
  // Most-listed featured services, straight from the category tree.
  const discover = (categories ?? []).flatMap((c) => c.subCategories).filter((s) => s.isFeatured)
    .sort((a, b) => b.businessCount - a.businessCount).slice(0, 6)
    .map((s) => [s.name, `/search?sub=${s.slug}`]);
  const cols = [
    { title: 'Discover', links: [...discover, ['All categories', '/categories']] },
    { title: 'For business', links: [['List your business', '/list-your-business'], ['Plans & pricing', '/pricing'], ['Business sign in', '/login']] },
    { title: 'Company', links: [['About Calling Bell', '/about'], ['Trust & safety', '/trust-and-safety'], ['Contact support', '/support']] },
  ];
  return (
    <footer className="mt-16 bg-navy text-on-navy-muted">
      <div className="container-page grid gap-10 py-12 md:grid-cols-[1.4fr_1fr_1fr_1fr]">
        <div>
          <Logo light />
          <p className="mt-3 max-w-xs text-sm leading-6">{tagline} Discover. Connect. Book. Grow.</p>
        </div>
        {cols.map((c) => (
          <div key={c.title}>
            <h3 className="mb-3 text-sm font-semibold text-white">{c.title}</h3>
            <ul className="space-y-2 text-sm">{c.links.map(([label, to]) => <li key={label}><Link to={to!} className="transition-colors hover:text-white">{label}</Link></li>)}</ul>
          </div>
        ))}
      </div>
      <div className="border-t border-navy-line">
        <div className="container-page flex flex-col gap-2 py-5 text-xs sm:flex-row sm:justify-between">
          <span>© {new Date().getFullYear()} TekOrtus Pvt., Ltd.</span>
          <span>Privacy · Terms · Grievance officer</span>
        </div>
      </div>
    </footer>
  );
}
