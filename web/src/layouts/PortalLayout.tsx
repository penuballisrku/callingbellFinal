import { useEffect, useState, type ReactNode } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router';
import { Drawer, IconButton, useMediaQuery } from '@mui/material';
import MenuRounded from '@mui/icons-material/MenuRounded';
import { useNotificationStream } from '@/lib/realtime';
import { Logo, NotificationBell, ThemeMenu, UserMenu } from './Shared';

export interface PortalNavItem { to: string; label: string; icon: ReactNode; end?: boolean; badge?: number }

/** Shared shell for the business portal and the admin console. */
export function PortalLayout({ title, nav, headerExtra, footer }: { title: string; nav: PortalNavItem[]; headerExtra?: ReactNode; footer?: ReactNode }) {
  const desktop = useMediaQuery('(min-width:1024px)');
  const [open, setOpen] = useState(false);
  const location = useLocation();
  useNotificationStream();
  useEffect(() => setOpen(false), [location.pathname]);

  const sidebar = (
    <div className="flex h-full w-[248px] flex-col bg-navy text-[#CBD5E1]">
      <div className="flex h-16 items-center px-5"><Logo light /></div>
      <div className="px-5 pb-3 text-[11px] font-semibold uppercase tracking-[0.08em] text-[#7C8BA1]">{title}</div>
      <nav className="flex-1 space-y-0.5 overflow-y-auto px-3" aria-label={title}>
        {nav.map((n) => (
          <NavLink key={n.to} to={n.to} end={n.end}
            className={({ isActive }) => `flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors ${isActive ? 'bg-white/10 text-white' : 'hover:bg-white/5 hover:text-white'}`}>
            {({ isActive }) => (
              <>
                <span className={isActive ? 'text-accent' : 'text-[#7C8BA1]'}>{n.icon}</span>
                <span className="flex-1">{n.label}</span>
                {!!n.badge && <span className="rounded-full bg-accent px-2 py-0.5 text-[11px] font-bold text-on-accent">{n.badge}</span>}
              </>
            )}
          </NavLink>
        ))}
      </nav>
      {footer && <div className="border-t border-white/10 p-4">{footer}</div>}
    </div>
  );

  return (
    <div className="flex min-h-screen">
      {desktop ? <aside className="sticky top-0 h-screen shrink-0">{sidebar}</aside> : (
        <Drawer open={open} onClose={() => setOpen(false)}>{sidebar}</Drawer>
      )}
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-30 flex h-16 items-center gap-3 border-b border-line bg-surface/95 px-4 backdrop-blur md:px-6">
          {!desktop && <IconButton onClick={() => setOpen(true)} aria-label="Open navigation"><MenuRounded /></IconButton>}
          <div className="min-w-0 flex-1">{headerExtra}</div>
          <ThemeMenu />
          <NotificationBell />
          <UserMenu />
        </header>
        <main className="min-w-0 flex-1 px-4 py-6 md:px-8 md:py-8"><Outlet /></main>
      </div>
    </div>
  );
}
