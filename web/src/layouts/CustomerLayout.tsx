import { useEffect, useState } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router';
import { Button, Drawer, IconButton, MenuItem, Select } from '@mui/material';
import MenuRounded from '@mui/icons-material/MenuRounded';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import SearchRounded from '@mui/icons-material/SearchRounded';
import { useCities } from '@/lib/hooks';
import { useNotificationStream } from '@/lib/realtime';
import { homeFor, isAdmin, isOwner, useAuth } from '@/stores/auth';
import { useCity } from '@/stores/city';
import { Logo, NotificationBell, ThemeMenu, UserMenu } from './Shared';

export function CitySelect({ size = 'small' }: { size?: 'small' | 'medium' }) {
  const { data: cities } = useCities();
  const { citySlug, setCity } = useCity();
  return (
    <Select value={cities ? citySlug ?? '' : ''} displayEmpty onChange={(e) => setCity(e.target.value || null)} size={size}
      startAdornment={<PlaceOutlined sx={{ fontSize: 18, mr: 0.5, color: 'text.secondary' }} />}
      sx={{ minWidth: 150, '& .MuiSelect-select': { py: size === 'small' ? '7px' : undefined } }} inputProps={{ 'aria-label': 'Choose city' }}>
      <MenuItem value="">All cities</MenuItem>
      {cities?.map((c) => <MenuItem key={c.slug} value={c.slug}>{c.name}</MenuItem>)}
    </Select>
  );
}

function HeaderSearch() {
  const navigate = useNavigate();
  const location = useLocation();
  const [q, setQ] = useState('');
  if (location.pathname === '/' || location.pathname === '/search') return <div className="flex-1" />;
  return (
    <form className="relative mx-4 hidden max-w-md flex-1 md:block" onSubmit={(e) => { e.preventDefault(); navigate(`/search?q=${encodeURIComponent(q)}`); }}>
      <SearchRounded sx={{ position: 'absolute', left: 10, top: 9, fontSize: 20, color: 'var(--cb-faint)' }} />
      <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search electricians, doctors, salons…" aria-label="Search"
        className="h-[38px] w-full rounded-lg border border-line bg-surface pl-9 pr-3 text-sm outline-none focus:border-line-strong" />
    </form>
  );
}

const nav = [
  { to: '/categories', label: 'Categories' },
  { to: '/search?availability=now', label: 'Available now' },
  { to: '/pricing', label: 'For business' },
];

export default function CustomerLayout() {
  const user = useAuth((s) => s.user);
  const [open, setOpen] = useState(false);
  const location = useLocation();
  useNotificationStream();
  useEffect(() => { window.scrollTo({ top: 0 }); setOpen(false); }, [location.pathname]);

  return (
    <div className="flex min-h-screen flex-col">
      <header className="sticky top-0 z-40 border-b border-line bg-surface/95 backdrop-blur">
        <div className="container-page flex h-16 items-center gap-3">
          <Logo />
          <HeaderSearch />
          <nav className="hidden items-center gap-1 lg:flex" aria-label="Main">
            {nav.map((n) => (
              <NavLink key={n.to} to={n.to} className={({ isActive }) => `rounded-lg px-3 py-2 text-sm font-medium hover:bg-subtle ${isActive ? 'text-ink' : 'text-ink-2'}`}>{n.label}</NavLink>
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
  const cols = [
    { title: 'Discover', links: [['Electricians', '/search?sub=electrical'], ['Doctors', '/search?sub=doctors'], ['Salons', '/search?sub=beauty-salons'], ['Lawyers', '/search?sub=lawyers'], ['All categories', '/categories']] },
    { title: 'For business', links: [['List your business', '/register?type=business'], ['Plans & pricing', '/pricing'], ['Business sign in', '/login']] },
    { title: 'Company', links: [['About Calling Bell', '/'], ['Trust & safety', '/'], ['Contact support', '/']] },
  ];
  return (
    <footer className="mt-16 bg-navy text-[#CBD5E1]">
      <div className="container-page grid gap-10 py-12 md:grid-cols-[1.4fr_1fr_1fr_1fr]">
        <div>
          <Logo light />
          <p className="mt-3 max-w-xs text-sm leading-6">India's real-time local business network. Discover. Connect. Book. Grow.</p>
        </div>
        {cols.map((c) => (
          <div key={c.title}>
            <h3 className="mb-3 text-sm font-semibold text-white">{c.title}</h3>
            <ul className="space-y-2 text-sm">{c.links.map(([label, to]) => <li key={label}><Link to={to!} className="hover:text-white">{label}</Link></li>)}</ul>
          </div>
        ))}
      </div>
      <div className="border-t border-white/10">
        <div className="container-page flex flex-col gap-2 py-5 text-xs sm:flex-row sm:justify-between">
          <span>© {new Date().getFullYear()} Calling Bell Technologies Pvt. Ltd.</span>
          <span>Privacy · Terms · Grievance officer</span>
        </div>
      </div>
    </footer>
  );
}
