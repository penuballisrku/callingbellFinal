import { useEffect, useState } from 'react';
import { Link, NavLink, Outlet, useLocation } from 'react-router';
import { Button, Drawer, IconButton } from '@mui/material';
import AutoAwesomeRounded from '@mui/icons-material/AutoAwesomeRounded';
import MenuRounded from '@mui/icons-material/MenuRounded';
import { useCategories } from '@/lib/hooks';
import { useNotificationStream } from '@/lib/realtime';
import { homeFor, isAdmin, isOwner, useAuth } from '@/stores/auth';
import { Logo, NotificationBell, ThemeMenu, UserMenu } from './Shared';
import { CountryCode, useDistrictAutoSelect, useNetworkTagline } from '@/components/VisitorCountry';
import { CitySelect } from '@/components/LocationPicker';
import { SeoHead } from '@/features/seo/seo';
import { AskAiButton, SearchAssistant } from '@/features/assistant/SearchAssistant';
import { useAssistant } from '@/features/assistant/store';

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
      <SeoHead />
      <SearchAssistant />

      {/* At least a screen tall, so the footer starts below the fold and content arriving doesn't move it in view (no layout shift). */}
      <main className="min-h-[calc(100svh-72px)] flex-1"><Outlet /></main>
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
