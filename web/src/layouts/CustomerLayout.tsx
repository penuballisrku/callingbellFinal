import { useEffect, useState } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router';
import { Button, Drawer, IconButton, InputAdornment, ListSubheader, MenuItem, Select, TextField } from '@mui/material';
import MenuRounded from '@mui/icons-material/MenuRounded';
import NearMeOutlined from '@mui/icons-material/NearMeOutlined';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import SearchRounded from '@mui/icons-material/SearchRounded';
import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { useCategories, useCities } from '@/lib/hooks';
import type { CityArea, CityAreas, SearchSuggestion } from '@/lib/types';
import { useNotificationStream } from '@/lib/realtime';
import { homeFor, isAdmin, isOwner, useAuth } from '@/stores/auth';
import { useCity } from '@/stores/city';
import { Logo, NotificationBell, ThemeMenu, UserMenu } from './Shared';
import { CountryCode, useDistrictAutoSelect, useVisitorDistrict } from '@/components/VisitorCountry';
import { SearchSuggest, searchHref } from '@/components/SearchSuggest';

const AREA = 'area:';

/**
 * City picker. Lists every area of the selected city (or of the visitor's IP-detected city while none is selected), loaded from
 * /api/locations/cities/{slug}/areas, which the area-discovery agent keeps complete. A search box filters by area name, PIN code,
 * alternate spelling or sub-locality ("Hydernagar" finds Kukatpally); picking an area opens search filtered to it.
 */
export function CitySelect({ size = 'small', fullWidth = false, height }: { size?: 'small' | 'medium'; fullWidth?: boolean; height?: number }) {
  const { data: cities } = useCities();
  const { data: district } = useVisitorDistrict();
  const { citySlug, areaId, setCity } = useCity();
  const navigate = useNavigate();
  const location = useLocation();
  const [areaFilter, setAreaFilter] = useState('');

  const areaCity = cities?.find((c) => c.slug === (citySlug ?? district?.citySlug));
  const isDistrict = !!areaCity && areaCity.slug === district?.citySlug;
  const { data: cityAreas } = useQuery({
    queryKey: ['city-areas', areaCity?.slug],
    queryFn: () => api.get<CityAreas>(`/api/locations/cities/${areaCity!.slug}/areas`),
    enabled: !!areaCity,
    staleTime: 30 * 60_000,
    // While the agent is discovering this city, check back so new areas appear without a reload.
    refetchInterval: (q) => (q.state.data?.discovering ? 10_000 : false),
  });
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
    if (!v.startsWith(AREA) || !areaCity) { setCity(v || null); return; }
    const id = v.slice(AREA.length);
    setCity(areaCity.slug, id);
    // Choosing a location only remembers it; results load when the visitor presses Search. On the results page the
    // current search is refined in place for the new area.
    if (location.pathname !== '/search') return;
    const params = new URLSearchParams(location.search);
    params.set('city', areaCity.slug);
    params.set('area', id);
    params.delete('page');
    navigate(`/search?${params}`, { replace: true });
  };

  const label = (v: string) => {
    if (!v) return 'All cities';
    if (v.startsWith(AREA)) return `${areas.find((a) => AREA + a.id === v)?.name}, ${areaCity?.name}`;
    return cities?.find((c) => c.slug === v)?.name ?? '';
  };

  return (
    <Select value={value} displayEmpty onChange={(e) => onChange(e.target.value)} size={size} renderValue={label} fullWidth={fullWidth}
      startAdornment={<PlaceOutlined sx={{ fontSize: 18, mr: 0.5, color: 'text.secondary', flexShrink: 0 }} />}
      // autoFocus off so the area search box keeps focus; the filter resets each time the menu closes.
      MenuProps={{ autoFocus: false, slotProps: { paper: { sx: { maxHeight: 480 } } } }} onClose={() => setAreaFilter('')}
      title={label(value)}
      sx={{
        minWidth: fullWidth ? 0 : 150, maxWidth: size === 'small' ? 230 : undefined, height,
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
        <ListSubheader key="cities-h" sx={{ lineHeight: '32px', fontSize: 12, fontWeight: 600 }}>Other cities</ListSubheader>,
      ]}
      {cities?.filter((c) => c.slug !== areaCity?.slug).map((c) => <MenuItem key={c.slug} value={c.slug}>{c.name}</MenuItem>)}
    </Select>
  );
}

function HeaderSearch() {
  const navigate = useNavigate();
  const location = useLocation();
  const citySlug = useCity((s) => s.citySlug);
  const areaId = useCity((s) => s.areaId);
  const [q, setQ] = useState('');
  const [picked, setPicked] = useState<SearchSuggestion | null>(null);
  // Pages with their own search box don't repeat it in the header.
  if (['/', '/search', '/categories'].includes(location.pathname)) return <div className="flex-1" />;
  // Searches the picked category (or typed text) within the location chosen in the city/area selector.
  const submit = () => navigate(searchHref(q, picked, citySlug, areaId));
  const pick = (s: SearchSuggestion) => {
    if (s.kind === 'Business') { navigate(`/b/${s.slug}`); return; }
    setQ(s.label);
    setPicked(s);
  };
  return (
    <form role="search" className="relative mx-4 hidden max-w-md flex-1 md:block" onSubmit={(e) => { e.preventDefault(); submit(); }}>
      <SearchRounded sx={{ position: 'absolute', left: 10, top: 9, fontSize: 20, color: 'var(--cb-faint)', zIndex: 1, pointerEvents: 'none' }} />
      <SearchSuggest value={q} onChange={setQ} citySlug={citySlug} onSelect={pick} ariaLabel="Search" placeholder="Search electricians, doctors, salons…"
        panelClassName="min-w-[380px]"
        inputClassName="h-[38px] w-full rounded-lg border border-line bg-subtle pl-9 pr-3 text-sm outline-none transition-[border-color,box-shadow,background-color] placeholder:text-faint hover:border-line-strong focus:border-accent focus:bg-surface focus:shadow-[0_0_0_3px_var(--cb-ring)]" />
    </form>
  );
}

const nav = [
  { to: '/categories', label: 'Categories' },
  { to: '/search?availability=now', label: 'Available now' },
  { to: '/list-your-business', label: 'For business' },
];

export default function CustomerLayout() {
  const user = useAuth((s) => s.user);
  const [open, setOpen] = useState(false);
  const location = useLocation();
  useNotificationStream();
  useDistrictAutoSelect();
  useEffect(() => { window.scrollTo({ top: 0 }); setOpen(false); }, [location.pathname]);

  return (
    <div className="flex min-h-screen flex-col">
      <header className="sticky top-0 z-40 border-b border-line bg-surface/85 backdrop-blur-md backdrop-saturate-150">
        <div className="container-page flex h-16 items-center gap-3">
          <div className="flex shrink-0 items-start gap-1">
            <Logo />
            <CountryCode />
          </div>
          <HeaderSearch />
          <nav className="hidden items-center gap-1 lg:flex" aria-label="Main">
            {nav.map((n) => (
              <NavLink key={n.to} to={n.to} className={({ isActive }) => `rounded-lg px-3 py-2 text-sm font-medium transition-colors hover:bg-subtle hover:text-ink ${isActive ? 'bg-subtle text-ink' : 'text-muted'}`}>{n.label}</NavLink>
            ))}
          </nav>
          <div className="hidden sm:block"><CitySelect /></div>
          <div className="hidden sm:block"><ThemeMenu /></div>
          {user ? (
            <div className="flex items-center gap-1">
              <NotificationBell />
              <UserMenu />
            </div>
          ) : (
            <div className="hidden items-center gap-2 sm:flex">
              <Button component={Link} to="/login" color="inherit">Sign in</Button>
              <Button component={Link} to="/register" variant="contained">Join free</Button>
            </div>
          )}
          <IconButton className="lg:!hidden" onClick={() => setOpen(true)} aria-label="Open menu"><MenuRounded /></IconButton>
        </div>
      </header>

      <Drawer anchor="right" open={open} onClose={() => setOpen(false)} slotProps={{ paper: { sx: { width: 300, p: 2 } } }}>
        <div className="mb-4 flex items-center justify-between"><Logo /><ThemeMenu /></div>
        <div className="mb-4"><CitySelect size="medium" /></div>
        <nav className="flex flex-col gap-1" aria-label="Mobile">
          {nav.map((n) => <Link key={n.to} to={n.to} className="rounded-lg px-3 py-2.5 font-medium hover:bg-subtle">{n.label}</Link>)}
          {user && <Link to={homeFor(user) === '/' ? '/account' : homeFor(user)} className="rounded-lg px-3 py-2.5 font-medium hover:bg-subtle">
            {isAdmin(user) ? 'Admin console' : isOwner(user) ? 'Business dashboard' : 'My account'}</Link>}
        </nav>
        {!user && <div className="mt-6 flex flex-col gap-2">
          <Button component={Link} to="/register" variant="contained" size="large">Join free</Button>
          <Button component={Link} to="/login" variant="outlined" size="large">Sign in</Button>
        </div>}
      </Drawer>

      <main className="flex-1"><Outlet /></main>
      <Footer />
    </div>
  );
}

function Footer() {
  const { data: categories } = useCategories();
  // Most-listed featured services, straight from the category tree.
  const discover = (categories ?? []).flatMap((c) => c.subCategories).filter((s) => s.isFeatured)
    .sort((a, b) => b.businessCount - a.businessCount).slice(0, 6)
    .map((s) => [s.name, `/search?sub=${s.slug}`]);
  const cols = [
    { title: 'Discover', links: [...discover, ['All categories', '/categories']] },
    { title: 'For business', links: [['List your business', '/list-your-business'], ['Plans & pricing', '/pricing'], ['Business sign in', '/login']] },
    { title: 'Company', links: [['About Calling Bell', '/'], ['Trust & safety', '/'], ['Contact support', '/']] },
  ];
  return (
    <footer className="mt-16 bg-navy text-on-navy-muted">
      <div className="container-page grid gap-10 py-12 md:grid-cols-[1.4fr_1fr_1fr_1fr]">
        <div>
          <Logo light />
          <p className="mt-3 max-w-xs text-sm leading-6">India's real-time local business network. Discover. Connect. Book. Grow.</p>
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
