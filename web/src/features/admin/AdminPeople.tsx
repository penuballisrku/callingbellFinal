import { useState } from 'react';
import { Link } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AgGridReact } from 'ag-grid-react';
import type { ColDef } from 'ag-grid-community';
import { Button, InputAdornment, MenuItem, Pagination, Skeleton, Switch, Tab, Tabs, TextField } from '@mui/material';
import SearchRounded from '@mui/icons-material/SearchRounded';
import { useSnackbar } from 'notistack';
import { api, errorMessage } from '@/lib/api';
import { ago, date, moneyExact, moneyShort, number } from '@/lib/format';
import { useDebounced, useDocumentTitle, useLookup } from '@/lib/hooks';
import type { AdminAd, AdminReview, AdminSubscriptions as Subs, AdminUser, OwnerList } from '@/lib/types';
import { ConfirmDialog, EmptyState, ErrorState, PageHeader, Panel, StatusBadge, Stars } from '@/components/ui';
import { useGridTheme, useResponsiveColumns } from '@/components/grid';
import { VoiceSearchButton } from '@/components/VoiceSearch';

function SearchBox({ value, onChange, placeholder }: { value: string; onChange: (v: string) => void; placeholder: string }) {
  return <TextField placeholder={placeholder} value={value} onChange={(e) => onChange(e.target.value)}
    slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRounded fontSize="small" /></InputAdornment>,
      endAdornment: <InputAdornment position="end"><VoiceSearchButton onText={(t) => onChange(t)} /></InputAdornment> } }} />;
}

/* ===================== Users ===================== */
export function AdminUsers() {
  useDocumentTitle('Users');
  const gridTheme = useGridTheme();
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const [userType, setUserType] = useState('');
  const [active, setActive] = useState('');
  const [q, setQ] = useState('');
  const [page, setPage] = useState(1);
  const [confirm, setConfirm] = useState<AdminUser | null>(null);
  const search = useDebounced(q);
  const { data, isLoading, isError, isFetching, refetch } = useQuery({
    queryKey: ['admin', 'users', userType, active, search, page],
    queryFn: () => api.paged<AdminUser>('/api/admin/users', { userType, isActive: active || undefined, q: search, page, pageSize: 20 }),
    placeholderData: keepPreviousData,
  });
  const toggle = useMutation({
    mutationFn: (u: AdminUser) => api.patch(`/api/admin/users/${u.id}`, { isActive: !u.isActive }),
    onSuccess: (r) => { enqueueSnackbar(r.message, { variant: 'success' }); setConfirm(null); void queryClient.invalidateQueries({ queryKey: ['admin', 'users'] }); },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });

  const columns: ColDef<AdminUser>[] = [
    { headerName: 'Name', field: 'displayName', flex: 1.4, minWidth: 200, cellRenderer: ({ data: u }: { data: AdminUser }) => <div className="leading-tight"><div className="font-semibold">{u.displayName}</div><div className="text-xs text-muted">{u.email}</div></div> },
    { headerName: 'Phone', field: 'phoneNumber', width: 150 },
    { headerName: 'Type', field: 'userType', width: 150, valueFormatter: (p) => String(p.value).replace(/([a-z])([A-Z])/g, '$1 $2') },
    { headerName: 'City', field: 'city', width: 130, valueFormatter: (p) => p.value ?? '-' },
    { headerName: 'Bookings', field: 'bookings', width: 105, type: 'rightAligned' },
    { headerName: 'Reviews', field: 'reviews', width: 100, type: 'rightAligned' },
    { headerName: 'Joined', field: 'createdOn', width: 120, valueFormatter: (p) => date(p.value) },
    { headerName: 'Last active', field: 'lastLoginOn', width: 130, valueFormatter: (p) => (p.value ? ago(p.value) : 'Never') },
    { headerName: 'Active', field: 'isActive', width: 95, sortable: false, cellRenderer: ({ data: u }: { data: AdminUser }) => <Switch size="small" checked={u.isActive} onChange={() => setConfirm(u)} slotProps={{ input: { 'aria-label': `${u.displayName} active` } }} /> },
  ];
  const responsiveColumns = useResponsiveColumns(columns);

  return (
    <>
      <PageHeader title="Users" subtitle={data ? `${number(data.pagination.totalCount)} accounts` : 'Loading…'} />
      <div className="mb-4 grid gap-3 sm:grid-cols-[2fr_1fr_1fr]">
        <SearchBox value={q} onChange={(v) => { setQ(v); setPage(1); }} placeholder="Search name, email or phone" />
        <TextField select value={userType} onChange={(e) => { setUserType(e.target.value); setPage(1); }} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'User type' } }}>
          <MenuItem value="">All user types</MenuItem>
          {['Customer', 'BusinessOwner', 'ServiceProvider', 'Administrator'].map((t) => <MenuItem key={t} value={t}>{t.replace(/([a-z])([A-Z])/g, '$1 $2')}</MenuItem>)}
        </TextField>
        <TextField select value={active} onChange={(e) => { setActive(e.target.value); setPage(1); }} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Status' } }}>
          <MenuItem value="">Active & inactive</MenuItem><MenuItem value="true">Active only</MenuItem><MenuItem value="false">Deactivated</MenuItem>
        </TextField>
      </div>
      <div className="card overflow-hidden">
        {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <div className="p-4"><Skeleton variant="rounded" height={480} /></div> : !data!.items.length ? <EmptyState title="No users found" /> : (
          <div className={isFetching ? 'opacity-60' : undefined}>
            <AgGridReact<AdminUser> theme={gridTheme} rowData={data!.items} columnDefs={responsiveColumns} domLayout="autoHeight" getRowId={(p) => p.data.id} suppressCellFocus defaultColDef={{ resizable: true, sortable: true }} />
          </div>
        )}
      </div>
      {data && data.pagination.totalPages > 1 && <div className="mt-6 flex justify-center"><Pagination count={data.pagination.totalPages} page={page} onChange={(_, p) => setPage(p)} shape="rounded" /></div>}
      <ConfirmDialog open={!!confirm} loading={toggle.isPending} destructive={confirm?.isActive}
        title={confirm?.isActive ? `Deactivate ${confirm.displayName}?` : `Reactivate ${confirm?.displayName}?`}
        message={confirm?.isActive ? 'They will be signed out everywhere and cannot sign in until reactivated.' : 'They will be able to sign in again.'}
        confirmLabel={confirm?.isActive ? 'Deactivate' : 'Reactivate'} onConfirm={() => confirm && toggle.mutate(confirm)} onClose={() => setConfirm(null)} />
    </>
  );
}

/* ===================== Review moderation ===================== */
export function AdminReviews() {
  useDocumentTitle('Review moderation');
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const statuses = useLookup('ReviewStatus');
  const [status, setStatus] = useState('Flagged');
  const [q, setQ] = useState('');
  const [page, setPage] = useState(1);
  const search = useDebounced(q);
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['admin', 'reviews', status, search, page],
    queryFn: () => api.get<OwnerList<AdminReview>>('/api/admin/reviews', { status, q: search, page, pageSize: 10 }),
    placeholderData: keepPreviousData,
  });
  const moderate = useMutation({
    mutationFn: (v: { id: string; action: 'publish' | 'reject' }) => api.patch(`/api/admin/reviews/${v.id}`, { action: v.action }),
    onSuccess: (r) => { enqueueSnackbar(r.message, { variant: 'success' }); void queryClient.invalidateQueries({ queryKey: ['admin'] }); },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });

  return (
    <>
      <PageHeader title="Review moderation" subtitle="Reported and pending reviews. Publishing or rejecting recalculates the business rating." />
      <Tabs value={statuses.length ? status : ''} onChange={(_, v) => { setStatus(v); setPage(1); }} sx={{ borderBottom: '1px solid var(--cb-line)', mb: 2 }} variant="scrollable" allowScrollButtonsMobile>
        {statuses.map((s) => <Tab key={s.code} value={s.code} label={`${s.name} (${number(data?.statusCounts[s.code] ?? 0)})`} />)}
        <Tab value="" label="All" />
      </Tabs>
      <div className="mb-4 max-w-md"><SearchBox value={q} onChange={(v) => { setQ(v); setPage(1); }} placeholder="Search review text, business or customer" /></div>
      {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <Skeleton variant="rounded" height={400} /> : !data!.page.items.length ? (
        <div className="card"><EmptyState title="Nothing to moderate" message="All caught up for this queue." /></div>
      ) : (
        <div className="space-y-3">
          {data!.page.items.map((r) => (
            <article key={r.id} className="card p-5">
              <div className="flex flex-wrap items-center gap-2">
                <Stars value={r.rating} /><span className="font-semibold">{r.title}</span><StatusBadge type="ReviewStatus" code={r.status} />
                <span className="ml-auto text-xs text-muted">{ago(r.createdOn)}</span>
              </div>
              <p className="mt-2 text-sm leading-6 text-ink-2">{r.comment}</p>
              <p className="mt-1 text-xs text-muted">{r.customerName} ({r.customerEmail}) on <Link to={`/business/${r.businessSlug}`} target="_blank" className="font-medium text-ink hover:underline">{r.businessName}</Link>, {r.city}</p>
              {r.reportReason && <div className="mt-3 rounded-lg bg-danger-soft px-3 py-2 text-sm text-danger">{r.reportReason}</div>}
              <div className="mt-3 flex gap-2">
                {r.status !== 'Published' && <Button size="small" variant="contained" color="success" disabled={moderate.isPending} onClick={() => moderate.mutate({ id: r.id, action: 'publish' })}>Publish</Button>}
                {r.status !== 'Rejected' && <Button size="small" variant="outlined" color="error" disabled={moderate.isPending} onClick={() => moderate.mutate({ id: r.id, action: 'reject' })}>Reject</Button>}
              </div>
            </article>
          ))}
        </div>
      )}
      {data && data.page.meta.totalPages > 1 && <div className="mt-6 flex justify-center"><Pagination count={data.page.meta.totalPages} page={page} onChange={(_, p) => setPage(p)} shape="rounded" /></div>}
    </>
  );
}

/* ===================== Advertisements ===================== */
export function AdminAds() {
  useDocumentTitle('Advertisements');
  const gridTheme = useGridTheme();
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const statuses = useLookup('AdStatus');
  const adTypes = useLookup('AdType');
  const [status, setStatus] = useState('');
  const [adType, setAdType] = useState('');
  const [q, setQ] = useState('');
  const [page, setPage] = useState(1);
  const search = useDebounced(q);
  const { data, isLoading, isError, isFetching, refetch } = useQuery({
    queryKey: ['admin', 'ads', status, adType, search, page],
    queryFn: () => api.get<OwnerList<AdminAd>>('/api/admin/advertisements', { status, adType, q: search, page, pageSize: 20 }),
    placeholderData: keepPreviousData,
  });
  const moderate = useMutation({
    mutationFn: (v: { id: string; action: string }) => api.patch(`/api/admin/advertisements/${v.id}`, { action: v.action }),
    onSuccess: () => { enqueueSnackbar('Campaign updated - owner notified', { variant: 'success' }); void queryClient.invalidateQueries({ queryKey: ['admin'] }); },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });
  const counts = data?.statusCounts ?? {};

  const columns: ColDef<AdminAd>[] = [
    { headerName: 'Campaign', flex: 1.6, minWidth: 260, valueGetter: (p) => p.data?.ad.title, cellRenderer: ({ data: a }: { data: AdminAd }) => <div className="leading-tight"><div className="truncate font-semibold">{a.ad.title}</div><div className="text-xs text-muted">{a.ad.campaignCode} · <Link to={`/business/${a.businessSlug}`} target="_blank" className="hover:underline">{a.businessName}</Link>, {a.city}</div></div> },
    { headerName: 'Type', width: 180, valueGetter: (p) => p.data?.ad.adType, cellRenderer: ({ value }: { value: string }) => <StatusBadge type="AdType" code={value} /> },
    { headerName: 'Dates', width: 200, valueGetter: (p) => p.data && `${date(p.data.ad.startDate)} - ${date(p.data.ad.endDate)}` },
    { headerName: 'Budget', width: 120, type: 'rightAligned', valueGetter: (p) => p.data?.ad.budget, valueFormatter: (p) => moneyExact(p.value) },
    { headerName: 'Impressions', width: 125, type: 'rightAligned', valueGetter: (p) => p.data?.ad.impressions, valueFormatter: (p) => number(p.value) },
    { headerName: 'CTR', width: 90, type: 'rightAligned', valueGetter: (p) => p.data?.ad.ctr, valueFormatter: (p) => `${p.value}%` },
    { headerName: 'Status', width: 150, valueGetter: (p) => p.data?.ad.status, cellRenderer: ({ value }: { value: string }) => <StatusBadge type="AdStatus" code={value} /> },
    {
      headerName: 'Actions', width: 200, sortable: false, cellRenderer: ({ data: a }: { data: AdminAd }) => (
        <div className="flex h-full items-center gap-1">
          {a.ad.status === 'PendingApproval' && <><Button size="small" variant="contained" color="success" onClick={() => moderate.mutate({ id: a.ad.id, action: 'approve' })}>Approve</Button><Button size="small" color="error" onClick={() => moderate.mutate({ id: a.ad.id, action: 'reject' })}>Reject</Button></>}
          {a.ad.status === 'Active' && <Button size="small" onClick={() => moderate.mutate({ id: a.ad.id, action: 'pause' })}>Pause</Button>}
          {a.ad.status === 'Paused' && <Button size="small" onClick={() => moderate.mutate({ id: a.ad.id, action: 'resume' })}>Resume</Button>}
        </div>
      ),
    },
  ];
  const responsiveColumns = useResponsiveColumns(columns);

  return (
    <>
      <PageHeader title="Advertisements" subtitle="Approve new campaigns and monitor delivery across all placements." />
      <Tabs value={statuses.length ? status : ''} onChange={(_, v) => { setStatus(v); setPage(1); }} sx={{ borderBottom: '1px solid var(--cb-line)', mb: 2 }} variant="scrollable" allowScrollButtonsMobile>
        <Tab value="" label="All" />
        {statuses.map((s) => <Tab key={s.code} value={s.code} label={`${s.name} (${counts[s.code] ?? 0})`} />)}
      </Tabs>
      <div className="mb-4 grid gap-3 sm:grid-cols-[2fr_1fr]">
        <SearchBox value={q} onChange={(v) => { setQ(v); setPage(1); }} placeholder="Search campaign, business or code" />
        <TextField select value={adType} onChange={(e) => { setAdType(e.target.value); setPage(1); }} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Ad type' } }}>
          <MenuItem value="">All placements</MenuItem>{adTypes.map((t) => <MenuItem key={t.code} value={t.code}>{t.name}</MenuItem>)}
        </TextField>
      </div>
      <div className="card overflow-hidden">
        {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <div className="p-4"><Skeleton variant="rounded" height={480} /></div> : !data!.page.items.length ? <EmptyState title="No campaigns here" /> : (
          <div className={isFetching ? 'opacity-60' : undefined}>
            <AgGridReact<AdminAd> theme={gridTheme} rowData={data!.page.items} columnDefs={responsiveColumns} domLayout="autoHeight" getRowId={(p) => p.data.ad.id} suppressCellFocus defaultColDef={{ resizable: true, sortable: true }} />
          </div>
        )}
      </div>
      {data && data.page.meta.totalPages > 1 && <div className="mt-6 flex justify-center"><Pagination count={data.page.meta.totalPages} page={page} onChange={(_, p) => setPage(p)} shape="rounded" /></div>}
    </>
  );
}

/* ===================== Subscriptions ===================== */
export function AdminSubscriptions() {
  useDocumentTitle('Subscriptions');
  const [plan, setPlan] = useState('');
  const [q, setQ] = useState('');
  const [page, setPage] = useState(1);
  const search = useDebounced(q);
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['admin', 'subscriptions', plan, search, page],
    queryFn: () => api.get<Subs>('/api/admin/subscriptions', { plan, q: search, page, pageSize: 20 }),
    placeholderData: keepPreviousData,
  });
  if (isError) return <ErrorState onRetry={() => refetch()} />;

  return (
    <>
      <PageHeader title="Subscriptions" subtitle="Plan mix, recurring revenue and current subscribers." />
      <div className="mb-6 grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-6">
        <div className="card p-4"><div className="text-xs text-muted">Total MRR</div><div className="mt-1 text-xl font-bold">{data ? moneyShort(data.totalMrr) : <Skeleton width={70} />}</div></div>
        {isLoading ? Array.from({ length: 5 }, (_, i) => <Skeleton key={i} variant="rounded" height={88} />) : data!.plans.map((p) => (
          <button key={p.code} type="button" onClick={() => { setPlan(plan === p.code ? '' : p.code); setPage(1); }}
            className={`card p-4 text-left transition-colors hover:border-line-strong ${plan === p.code ? '!border-inverse ring-1 ring-inverse' : ''}`}>
            <div className="flex items-center gap-2 text-xs text-muted"><span className="h-2 w-2 rounded-full" style={{ backgroundColor: p.badgeColor ?? 'var(--cb-faint)' }} />{p.name}</div>
            <div className="mt-1 text-xl font-bold">{number(p.activeCount)}</div>
            <div className="text-xs text-muted">{p.mrr ? `${moneyShort(p.mrr)} MRR · ${moneyShort(p.revenue12m)} / 12m` : 'No revenue'}</div>
          </button>
        ))}
      </div>
      <Panel title="Current subscribers" subtitle={data ? `${number(data.current.meta.totalCount)} active subscriptions${data.expiringIn30Days ? ` · ${data.expiringIn30Days} expiring without auto-renew in 30 days` : ''}` : undefined}
        action={<div className="w-64"><SearchBox value={q} onChange={(v) => { setQ(v); setPage(1); }} placeholder="Search business" /></div>} noPad>
        {isLoading ? <div className="p-5"><Skeleton height={300} /></div> : !data!.current.items.length ? <EmptyState title="No subscriptions match" /> : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] text-sm">
              <thead className="bg-subtle text-left text-xs uppercase tracking-wide text-muted">
                <tr>{['Business', 'Plan', 'Billing', 'Period', 'Amount', 'Status'].map((h) => <th key={h} className="px-5 py-3 font-semibold">{h}</th>)}</tr>
              </thead>
              <tbody className="divide-y divide-line">
                {data!.current.items.map((s) => (
                  <tr key={s.subscriptionNumber} className="hover:bg-subtle">
                    <td className="px-5 py-3"><Link to={`/admin/businesses/${s.businessId}`} className="font-semibold hover:underline">{s.businessName}</Link><div className="text-xs text-muted">{s.city} · {s.subscriptionNumber}</div></td>
                    <td className="px-5 py-3">{s.planName}</td>
                    <td className="px-5 py-3">{s.billingCycle}{s.autoRenew && <span className="block text-xs text-muted">Auto-renew</span>}</td>
                    <td className="px-5 py-3 whitespace-nowrap">{date(s.startDate)} - {s.planCode === 'FREE' ? 'ongoing' : date(s.endDate)}</td>
                    <td className="px-5 py-3 font-semibold">{s.amount ? moneyExact(s.amount) : 'Free'}</td>
                    <td className="px-5 py-3"><StatusBadge type="SubscriptionStatus" code={s.status} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>
      {data && data.current.meta.totalPages > 1 && <div className="mt-6 flex justify-center"><Pagination count={data.current.meta.totalPages} page={page} onChange={(_, p) => setPage(p)} shape="rounded" /></div>}
    </>
  );
}
