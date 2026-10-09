import { useEffect, useState, type ReactNode } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router';
import { Drawer, IconButton, useMediaQuery } from '@mui/material';
import MenuRounded from '@mui/icons-material/MenuRounded';
import { useNotificationStream } from '@/lib/realtime';
import { Logo, NotificationBell, ThemeMenu, UserMenu } from './Shared';
import { MessagesButton } from '@/features/chat/Chat';
import { CountryCode } from '@/components/VisitorCountry';

export interface PortalNavItem { to: string; label: string; icon: ReactNode; end?: boolean; badge?: number }

/** Shared shell for the business portal and the admin console. */
export function PortalLayout({ title, nav, headerExtra, footer }: { title: string; nav: PortalNavItem[]; headerExtra?: ReactNode; footer?: ReactNode }) {
  const desktop = useMediaQuery('(min-width:1024px)');
  const [open, setOpen] = useState(false);
  const location = useLocation();
  useNotificationStream();
  useEffect(() => setOpen(false), [location.pathname]);

  const sidebar = (
    <div className="flex h-full w-[248px] flex-col border-r border-navy-line bg-navy text-on-navy-muted">
      <div className="flex h-16 items-center border-b border-navy-line px-5"><div className="flex items-start gap-1"><Logo light /><CountryCode onDark /></div></div>
      <div className="px-5 pb-2 pt-5 text-[11px] font-semibold uppercase tracking-[0.08em] text-on-navy-faint">{title}</div>
      <nav className="flex-1 space-y-0.5 overflow-y-auto px-3 pb-4" aria-label={title}>
        {nav.map((n) => (
          <NavLink key={n.to} to={n.to} end={n.end}
            className={({ isActive }) => `relative flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors ${isActive ? 'bg-white/8 text-white' : 'hover:bg-white/4 hover:text-on-navy'}`}>
            {({ isActive }) => (
              <>
                {isActive && <span className="absolute inset-y-2 -left-3 w-0.75 rounded-r-full bg-accent" aria-hidden />}
                <span className={`flex [&_svg]:text-[20px] ${isActive ? 'text-accent' : 'text-on-navy-faint'}`}>{n.icon}</span>
                <span className="flex-1 truncate">{n.label}</span>
                {!!n.badge && <span className="tabular rounded-full bg-accent px-2 py-0.5 text-[11px] font-bold text-on-accent">{n.badge}</span>}
              </>
            )}
          </NavLink>
        ))}
      </nav>
      {footer && <div className="border-t border-navy-line p-4">{footer}</div>}
    </div>
  );

  return (
    <div className="flex min-h-screen">
      {desktop ? <aside className="sticky top-0 h-screen shrink-0">{sidebar}</aside> : (
        <Drawer open={open} onClose={() => setOpen(false)}>{sidebar}</Drawer>
      )}
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-30 flex h-16 items-center gap-1.5 border-b border-line bg-surface/85 px-4 backdrop-blur-md backdrop-saturate-150 md:px-6">
          {!desktop && <IconButton onClick={() => setOpen(true)} aria-label="Open navigation"><MenuRounded /></IconButton>}
          <div className="min-w-0 flex-1">{headerExtra}</div>
          <ThemeMenu />
          <span className="hidden sm:inline-flex"><MessagesButton /></span>
          <NotificationBell />
          <UserMenu />
        </header>
        <main className="mx-auto w-full min-w-0 max-w-[1600px] flex-1 px-4 py-6 md:px-8 md:py-8"><Outlet /></main>
      </div>
    </div>
  );
}
