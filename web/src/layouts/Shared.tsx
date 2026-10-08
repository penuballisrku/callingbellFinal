import { useState } from 'react';
import { useNavigate } from 'react-router';
import { Avatar, Badge, Button, Divider, IconButton, ListItemIcon, Menu, MenuItem, Popover, ToggleButton, ToggleButtonGroup, Tooltip, Typography } from '@mui/material';
import LightModeOutlined from '@mui/icons-material/LightModeOutlined';
import DarkModeOutlined from '@mui/icons-material/DarkModeOutlined';
import SettingsBrightnessOutlined from '@mui/icons-material/SettingsBrightnessOutlined';
import CheckRounded from '@mui/icons-material/CheckRounded';
import { Logo as BrandLogo } from '@/components/Logo';
import { accents, useResolvedMode, useThemeSettings, type ThemeMode } from '@/stores/theme';
import NotificationsNoneRounded from '@mui/icons-material/NotificationsNoneRounded';
import LogoutRounded from '@mui/icons-material/LogoutRounded';
import DashboardOutlined from '@mui/icons-material/DashboardOutlined';
import EventNoteOutlined from '@mui/icons-material/EventNoteOutlined';
import AdminPanelSettingsOutlined from '@mui/icons-material/AdminPanelSettingsOutlined';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { ago, initials } from '@/lib/format';
import { isAdmin, isOwner, useAuth } from '@/stores/auth';
import type { NotificationItem } from '@/lib/types';
import { EmptyState } from '@/components/ui';

/** Brand logo. `light` renders the inverted version for navy surfaces. */
export function Logo({ light }: { light?: boolean }) {
  return <BrandLogo tone={light ? 'onDark' : 'auto'} />;
}

/** Appearance menu: light / dark / system mode and colour theme. */
export function ThemeMenu({ onDark }: { onDark?: boolean }) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const { mode, accent, setMode, setAccent } = useThemeSettings();
  const resolved = useResolvedMode();
  return (
    <>
      <Tooltip title="Appearance">
        <IconButton onClick={(e) => setAnchor(e.currentTarget)} aria-label="Appearance settings" sx={{ color: onDark ? '#fff' : 'text.primary' }}>
          {resolved === 'dark' ? <DarkModeOutlined /> : <LightModeOutlined />}
        </IconButton>
      </Tooltip>
      <Popover open={!!anchor} anchorEl={anchor} onClose={() => setAnchor(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }} slotProps={{ paper: { sx: { width: 300, mt: 1, p: 2 } } }}>
        <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">Mode</div>
        <ToggleButtonGroup exclusive fullWidth size="small" value={mode} onChange={(_, v: ThemeMode | null) => v && setMode(v)} aria-label="Colour mode">
          <ToggleButton value="light" aria-label="Light"><LightModeOutlined fontSize="small" sx={{ mr: 0.75 }} />Light</ToggleButton>
          <ToggleButton value="dark" aria-label="Dark"><DarkModeOutlined fontSize="small" sx={{ mr: 0.75 }} />Dark</ToggleButton>
          <ToggleButton value="system" aria-label="System"><SettingsBrightnessOutlined fontSize="small" sx={{ mr: 0.75 }} />Auto</ToggleButton>
        </ToggleButtonGroup>
        <div className="mb-2 mt-5 text-xs font-semibold uppercase tracking-wide text-muted">Theme</div>
        <div className="grid grid-cols-5 gap-2" role="radiogroup" aria-label="Colour theme">
          {accents.map((a) => {
            const selected = a.key === accent;
            return (
              <Tooltip key={a.key} title={a.label}>
                <button type="button" role="radio" aria-checked={selected} aria-label={a.label} onClick={() => setAccent(a.key)}
                  className={`flex h-10 items-center justify-center rounded-lg border transition-colors ${selected ? 'border-ink' : 'border-line hover:border-line-strong'}`}>
                  <span className="flex h-6 w-6 items-center justify-center rounded-full" style={{ backgroundColor: resolved === 'dark' ? a.dark : a.light }}>
                    {selected && <CheckRounded sx={{ fontSize: 16, color: a.key === 'amber' || resolved === 'dark' ? '#0B1220' : '#fff' }} />}
                  </span>
                </button>
              </Tooltip>
            );
          })}
        </div>
        <p className="mt-3 text-xs text-muted">{accents.find((a) => a.key === accent)?.label} theme · saved on this device</p>
      </Popover>
    </>
  );
}

export function NotificationBell({ dark }: { dark?: boolean }) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { data } = useQuery({
    queryKey: ['notifications'],
    queryFn: () => api.get<{ unreadCount: number; items: NotificationItem[] }>('/api/me/notifications', { take: 12 }),
    refetchInterval: 120_000,
  });
  const markRead = useMutation({
    mutationFn: () => api.post('/api/me/notifications/read'),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notifications'] }),
  });

  return (
    <>
      <Tooltip title="Notifications">
        <IconButton onClick={(e) => setAnchor(e.currentTarget)} aria-label={`Notifications, ${data?.unreadCount ?? 0} unread`} sx={{ color: dark ? '#fff' : 'text.primary' }}>
          <Badge badgeContent={data?.unreadCount ?? 0} color="secondary" max={99}><NotificationsNoneRounded /></Badge>
        </IconButton>
      </Tooltip>
      <Popover open={!!anchor} anchorEl={anchor} onClose={() => setAnchor(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }} slotProps={{ paper: { sx: { width: 380, maxWidth: 'calc(100vw - 32px)', mt: 1, border: '1px solid var(--cb-line)' } } }}>
        <div className="flex items-center justify-between border-b border-line px-4 py-3">
          <Typography fontWeight={700}>Notifications</Typography>
          <Button size="small" disabled={!data?.unreadCount || markRead.isPending} onClick={() => markRead.mutate()}>Mark all read</Button>
        </div>
        <div className="max-h-[420px] overflow-y-auto">
          {!data?.items.length ? <EmptyState title="You're all caught up" message="New leads, bookings and updates will appear here." /> : data.items.map((n) => (
            <button key={n.id} type="button" onClick={() => { setAnchor(null); if (n.linkUrl) navigate(n.linkUrl); }}
              className={`block w-full border-b border-line px-4 py-3 text-left hover:bg-subtle ${n.isRead ? '' : 'bg-accent-soft'}`}>
              <div className="flex items-center justify-between gap-2">
                <span className="text-sm font-semibold">{n.title}</span>
                <span className="shrink-0 text-[11px] text-muted">{ago(n.createdOn)}</span>
              </div>
              <p className="mt-0.5 line-clamp-2 text-[13px] text-muted">{n.message}</p>
            </button>
          ))}
        </div>
      </Popover>
    </>
  );
}

export function UserMenu({ dark }: { dark?: boolean }) {
  const { user, refreshToken, clear } = useAuth();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  if (!user) return null;

  const signOut = async () => {
    setAnchor(null);
    if (refreshToken) await api.post('/api/auth/logout', { refreshToken }).catch(() => undefined);
    clear();
    queryClient.clear();
    navigate('/');
  };

  return (
    <>
      <IconButton onClick={(e) => setAnchor(e.currentTarget)} aria-label="Account menu" size="small">
        <Avatar sx={{ width: 34, height: 34, fontSize: 14, fontWeight: 700, bgcolor: dark ? '#F4A62C' : 'var(--cb-inverse)', color: dark ? '#0B1220' : 'var(--cb-on-inverse)' }}>
          {initials(user.displayName)}
        </Avatar>
      </IconButton>
      <Menu anchorEl={anchor} open={!!anchor} onClose={() => setAnchor(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }} slotProps={{ paper: { sx: { minWidth: 240, mt: 1, border: '1px solid var(--cb-line)' } } }}>
        <div className="px-4 py-2">
          <div className="text-sm font-semibold">{user.displayName}</div>
          <div className="truncate text-xs text-muted">{user.email}</div>
        </div>
        <Divider />
        {isAdmin(user) && <MenuItem onClick={() => { setAnchor(null); navigate('/admin'); }}><ListItemIcon><AdminPanelSettingsOutlined fontSize="small" /></ListItemIcon>Admin console</MenuItem>}
        {isOwner(user) && <MenuItem onClick={() => { setAnchor(null); navigate('/owner'); }}><ListItemIcon><DashboardOutlined fontSize="small" /></ListItemIcon>Business dashboard</MenuItem>}
        {!isAdmin(user) && !isOwner(user) && <MenuItem onClick={() => { setAnchor(null); navigate('/account'); }}><ListItemIcon><EventNoteOutlined fontSize="small" /></ListItemIcon>My bookings & saved</MenuItem>}
        <MenuItem onClick={signOut}><ListItemIcon><LogoutRounded fontSize="small" /></ListItemIcon>Sign out</MenuItem>
      </Menu>
    </>
  );
}
