import { useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AgGridReact } from 'ag-grid-react';
import type { ColDef, SortChangedEvent } from 'ag-grid-community';
import { Button, InputAdornment, MenuItem, Pagination, Skeleton, Switch, Tab, Tabs, TextField, FormControlLabel } from '@mui/material';
import SearchRounded from '@mui/icons-material/SearchRounded';
import OpenInNewRounded from '@mui/icons-material/OpenInNewRounded';
import { useSnackbar } from 'notistack';
import { api, errorMessage } from '@/lib/api';
import { ago, date, dateTime, hhmm, money, moneyExact, number } from '@/lib/format';
import { useCities, useDebounced, useDocumentTitle, useLookup } from '@/lib/hooks';
import type { AdminBusinessDetail as Detail, AdminBusinessRow, Category, Plan } from '@/lib/types';
import { AvailabilityBadge, ConfirmDialog, EmptyState, ErrorState, Img, PageHeader, Panel, Rating, StatusBadge, Stars } from '@/components/ui';
import { useGridTheme } from '@/components/grid';




export function AdminBusinesses() {
  useDocumentTitle('Businesses');
  const gridTheme = useGridTheme();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const [q, setQ] = useState(params.get('q') ?? '');
  const search = useDebounced(q);
  const statuses = useLookup('BusinessStatus');
  const verifications = useLookup('VerificationStatus');
  const { data: categories } = useQuery({ queryKey: ['categories'], queryFn: () => api.get<Category[]>('/api/categories'), staleTime: 600_000 });
  const { data: cities } = useCities();
  const { data: plans } = useQuery({ queryKey: ['plans'], queryFn: () => api.get<Plan[]>('/api/plans'), staleTime: 600_000 });

  const filters = {
    status: params.get('status') ?? '', verification: params.get('verification') ?? '', category: params.get('category') ?? '',
    city: params.get('city') ?? '', plan: params.get('plan') ?? '', sort: params.get('sort') ?? 'name', page: Number(params.get('page') ?? 1),
  };
  const set = (patch: Record<string, string | null>) => {
    const next = new URLSearchParams(params);
    Object.entries(patch).forEach(([k, v]) => (v ? next.set(k, v) : next.delete(k)));
    if (!('page' in patch)) next.delete('page');
    setParams(next, { replace: true });
  };

  const { data, isLoading, isError, isFetching, refetch } = useQuery({
    queryKey: ['admin', 'businesses', filters, search],
    queryFn: () => api.paged<AdminBusinessRow>('/api/admin/businesses', { ...filters, q: search, pageSize: 20 }),
    placeholderData: keepPreviousData,
  });

  const columns = useMemo<ColDef<AdminBusinessRow>[]>(() => [
    {
      headerName: 'Business', field: 'name', colId: 'name', minWidth: 260, flex: 2, sortable: true,
      cellRenderer: ({ data: b }: { data: AdminBusinessRow }) => (
        <div className="flex h-full items-center gap-3">
          <Img src={b.logoUrl} alt="" className="h-9 w-9 shrink-0" fallbackText={b.name} />
          <div className="min-w-0 leading-tight"><div className="truncate font-semibold">{b.name}</div><div className="truncate text-xs text-muted">{b.subCategoryName ?? b.categoryName}</div></div>
        </div>
      ),
    },
    { headerName: 'Location', field: 'city', colId: 'city', minWidth: 150, flex: 1, sortable: true, valueFormatter: (p) => `${p.data?.area ? `${p.data.area}, ` : ''}${p.value}` },
    { headerName: 'Owner', field: 'ownerName', minWidth: 160, flex: 1, sortable: false, cellRenderer: ({ data: b }: { data: AdminBusinessRow }) => <div className="leading-tight"><div className="truncate">{b.ownerName}</div><div className="truncate text-xs text-muted">{b.phoneNumber}</div></div> },
    { headerName: 'Status', field: 'status', width: 150, sortable: false, cellRenderer: ({ value }: { value: string }) => <StatusBadge type="BusinessStatus" code={value} /> },
    { headerName: 'Verification', field: 'verificationStatus', width: 130, sortable: false, cellRenderer: ({ value }: { value: string }) => <StatusBadge type="VerificationStatus" code={value} /> },
    { headerName: 'Plan', field: 'planCode', width: 110, sortable: false, valueFormatter: (p) => (p.value ? String(p.value).charAt(0) + String(p.value).slice(1).toLowerCase() : 'None') },
    { headerName: 'Rating', field: 'averageRating', colId: 'rating', width: 120, sortable: true, cellRenderer: ({ data: b }: { data: AdminBusinessRow }) => <Rating value={b.averageRating} count={b.reviewCount} /> },
    { headerName: 'Leads 30d', field: 'leads30', colId: 'leads', width: 110, sortable: true, type: 'rightAligned' },
    { headerName: 'Bookings 30d', field: 'bookings30', width: 120, sortable: false, type: 'rightAligned' },
    { headerName: 'Live ads', field: 'liveAds', width: 95, sortable: false, type: 'rightAligned' },
    { headerName: 'Listed', field: 'createdOn', colId: 'newest', width: 120, sortable: true, valueFormatter: (p) => date(p.value) },
  ], []);

  const onSort = (e: SortChangedEvent<AdminBusinessRow>) => {
    const s = e.api.getColumnState().find((c) => c.sort);
    set({ sort: s ? `${s.sort === 'desc' ? '-' : ''}${s.colId}` : null });
  };

  return (
    <>
      <PageHeader title="Businesses" subtitle={data ? `${number(data.pagination.totalCount)} businesses match` : 'Loading…'} />
      <Tabs value={statuses.length ? filters.status : ''} onChange={(_, v) => set({ status: v || null })} variant="scrollable" allowScrollButtonsMobile sx={{ borderBottom: '1px solid var(--cb-line)', mb: 2 }}>
        <Tab value="" label="All" />
        {statuses.map((s) => <Tab key={s.code} value={s.code} label={s.name} />)}
      </Tabs>
      <div className="mb-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-[2fr_1fr_1fr_1fr_1fr_auto]">
        <TextField placeholder="Search name, owner, phone or area" value={q} onChange={(e) => { setQ(e.target.value); set({}); }}
          slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRounded fontSize="small" /></InputAdornment> } }} />
        <TextField select value={categories ? filters.category : ''} onChange={(e) => set({ category: e.target.value })} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Category' } }}>
          <MenuItem value="">All categories</MenuItem>{categories?.map((c) => <MenuItem key={c.slug} value={c.slug}>{c.name}</MenuItem>)}
        </TextField>
        <TextField select value={cities ? filters.city : ''} onChange={(e) => set({ city: e.target.value })} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'City' } }}>
          <MenuItem value="">All cities</MenuItem>{cities?.map((c) => <MenuItem key={c.slug} value={c.slug}>{c.name}</MenuItem>)}
        </TextField>
        <TextField select value={verifications.length ? filters.verification : ''} onChange={(e) => set({ verification: e.target.value })} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Verification' } }}>
          <MenuItem value="">Any verification</MenuItem>{verifications.map((v) => <MenuItem key={v.code} value={v.code}>{v.name}</MenuItem>)}
        </TextField>
        <TextField select value={plans ? filters.plan : ''} onChange={(e) => set({ plan: e.target.value })} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Plan' } }}>
          <MenuItem value="">Any plan</MenuItem>{plans?.map((p) => <MenuItem key={p.code} value={p.code}>{p.name}</MenuItem>)}
        </TextField>
        <Button onClick={() => { setQ(''); setParams(new URLSearchParams(), { replace: true }); }}>Reset</Button>
      </div>

      <div className="card overflow-hidden">
        {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <div className="p-4"><Skeleton variant="rounded" height={480} /></div> : !data!.items.length ? (
          <EmptyState title="No businesses match these filters" action={<Button onClick={() => setParams(new URLSearchParams())}>Clear filters</Button>} />
        ) : (
          <div className={`w-full overflow-x-auto ${isFetching ? 'opacity-60' : ''}`}>
            <div style={{ minWidth: 1180 }}>
              <AgGridReact<AdminBusinessRow> theme={gridTheme} rowData={data!.items} columnDefs={columns} domLayout="autoHeight" getRowId={(p) => p.data.id}
                onRowClicked={(e) => e.data && navigate(`/admin/businesses/${e.data.id}`)} rowStyle={{ cursor: 'pointer' }} onSortChanged={onSort}
                suppressCellFocus suppressMultiSort defaultColDef={{ resizable: true, sortingOrder: ['asc', 'desc', null] }} />
            </div>
          </div>
        )}
      </div>
      {data && data.pagination.totalPages > 1 && (
        <div className="mt-6 flex justify-center"><Pagination count={data.pagination.totalPages} page={filters.page} onChange={(_, p) => set({ page: p === 1 ? null : String(p) })} shape="rounded" /></div>
      )}
    </>
  );
}

export function AdminBusinessDetail() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const [tab, setTab] = useState(0);
  const [confirm, setConfirm] = useState<{ title: string; body: Record<string, unknown>; destructive?: boolean } | null>(null);
  const [note, setNote] = useState('');
  const { data, isLoading, isError, refetch } = useQuery({ queryKey: ['admin', 'business', id], queryFn: () => api.get<Detail>(`/api/admin/businesses/${id}`) });
  useDocumentTitle(data?.summary.name);

  const update = useMutation({
    mutationFn: (body: Record<string, unknown>) => api.patch(`/api/admin/businesses/${id}`, { ...body, note: note || null }),
    onSuccess: () => {
      enqueueSnackbar('Business updated - owner notified', { variant: 'success' });
      setConfirm(null); setNote('');
      void queryClient.invalidateQueries({ queryKey: ['admin'] });
    },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });

  if (isError) return <ErrorState onRetry={() => refetch()} />;
  if (isLoading || !data) return <Skeleton variant="rounded" height={600} />;
  const s = data.summary;
  const p = data.profile;

  return (
    <>
      <PageHeader title={s.name} crumbs={[{ label: 'Businesses', to: '/admin/businesses' }, { label: s.name }]}
        subtitle={<span className="inline-flex flex-wrap items-center gap-2">{s.subCategoryName} · {s.area}, {s.city} <StatusBadge type="BusinessStatus" code={s.status} /><StatusBadge type="VerificationStatus" code={s.verificationStatus} /><AvailabilityBadge status={s.availabilityStatus} /></span>}
        actions={<>
          <Button variant="outlined" component={Link} to={`/b/${s.slug}`} target="_blank" endIcon={<OpenInNewRounded fontSize="small" />}>Public profile</Button>
          {s.status !== 'Active' && <Button variant="contained" color="success" onClick={() => setConfirm({ title: 'Approve and publish this listing?', body: { status: 'Active' } })}>Approve</Button>}
          {s.status === 'Active' && <Button variant="outlined" color="error" onClick={() => setConfirm({ title: 'Suspend this listing?', body: { status: 'Suspended' }, destructive: true })}>Suspend</Button>}
          {s.verificationStatus !== 'Verified' && <Button variant="contained" onClick={() => setConfirm({ title: 'Mark documents as verified?', body: { verificationStatus: 'Verified' } })}>Verify</Button>}
          {s.verificationStatus === 'Pending' && <Button color="error" onClick={() => setConfirm({ title: 'Reject verification?', body: { verificationStatus: 'Rejected' }, destructive: true })}>Reject</Button>}
        </>} />

      <div className="mb-6 grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-6">
        {[
          ['Profile views (30d)', number(data.totals.profileViews30)], ['Leads', number(data.totals.leads)],
          ['Converted', `${number(data.totals.convertedLeads)} (${data.totals.leads ? Math.round(data.totals.convertedLeads / data.totals.leads * 100) : 0}%)`],
          ['Bookings', number(data.totals.bookings)], ['Booking value', moneyExact(data.totals.bookingRevenue)], ['Paid to platform', moneyExact(data.totals.platformRevenue)],
        ].map(([l, v]) => <div key={l} className="card p-4"><div className="text-xs text-muted">{l}</div><div className="mt-1 text-lg font-bold">{v}</div></div>)}
      </div>

      <Tabs value={tab} onChange={(_, v) => setTab(v)} variant="scrollable" allowScrollButtonsMobile sx={{ borderBottom: '1px solid var(--cb-line)', mb: 3 }}>
        <Tab label="Overview" /><Tab label={`Leads (${data.recentLeads.length})`} /><Tab label="Bookings" /><Tab label="Reviews" /><Tab label="Ads & plan" />
      </Tabs>

      {tab === 0 && (
        <div className="grid gap-6 xl:grid-cols-3">
          <div className="space-y-6 xl:col-span-2">
            <Panel title="Profile">
              {p.tagline && <p className="font-semibold">{p.tagline}</p>}
              <p className="mt-1 text-sm leading-6 text-ink-2">{p.description}</p>
              <dl className="mt-4 grid gap-4 text-sm sm:grid-cols-3">
                {[['Phone', p.phoneNumber], ['WhatsApp', p.whatsAppNumber], ['Email', p.email], ['Website', p.website], ['Address', `${p.addressLine ?? ''}, ${s.area}, ${s.city} ${p.pincode ?? ''}`], ['Landmark', p.landmark],
                  ['Established', p.yearEstablished?.toString()], ['Team size', p.teamSize?.toString()], ['Languages', p.languages]].filter(([, v]) => v).map(([l, v]) => (
                  <div key={l}><dt className="text-xs text-muted">{l}</dt><dd className="mt-0.5 break-words font-medium">{v}</dd></div>
                ))}
              </dl>
            </Panel>
            <Panel title="Services" noPad>
              <ul className="divide-y divide-line">{data.services.map((x) => <li key={x.id} className="flex justify-between gap-3 px-5 py-3 text-sm"><span>{x.name}</span><span className="font-semibold">{money(x.price)} <span className="font-normal text-muted">{x.price > 0 ? x.priceUnit : ''}</span></span></li>)}</ul>
            </Panel>
            <Panel title="Images">
              <div className="grid grid-cols-2 gap-3 md:grid-cols-4">{data.images.map((i) => <Img key={i.id} src={i.thumbnailUrl ?? i.imageUrl} alt={i.altText ?? ''} aspect="4/3" className="w-full" />)}</div>
            </Panel>
          </div>
          <div className="space-y-6">
            <Panel title="Owner">
              <div className="text-sm"><div className="font-semibold">{s.ownerName}</div><div className="text-muted">{s.ownerEmail}</div><div className="text-muted">{p.ownerPhone}</div>
                <div className="mt-2 text-xs text-muted">Last sign-in {p.ownerLastLogin ? ago(p.ownerLastLogin) : 'never'}</div></div>
            </Panel>
            <Panel title="Listing controls">
              <FormControlLabel control={<Switch checked={s.isFeatured} onChange={(e) => update.mutate({ isFeatured: e.target.checked })} />} label="Featured on homepage & category pages" />
              <TextField label="Note to owner (sent with status changes)" multiline minRows={2} value={note} onChange={(e) => setNote(e.target.value)} sx={{ mt: 2 }} />
            </Panel>
            <Panel title="Working hours">
              <ul className="space-y-1.5 text-sm">{data.hours.map((h) => <li key={h.dayOfWeek} className="flex justify-between"><span>{h.day}</span><span className="text-muted">{h.isClosed ? 'Closed' : `${hhmm(h.open)} - ${hhmm(h.close)}`}</span></li>)}</ul>
            </Panel>
          </div>
        </div>
      )}

      {tab === 1 && (
        <Panel noPad>{!data.recentLeads.length ? <EmptyState title="No leads yet" /> : (
          <ul className="divide-y divide-line">{data.recentLeads.map((l) => (
            <li key={l.id} className="px-5 py-3.5 text-sm">
              <div className="flex flex-wrap items-center gap-2"><span className="font-semibold">{l.customerName}</span><StatusBadge type="EnquiryType" code={l.enquiryType} /><StatusBadge type="EnquiryStatus" code={l.status} /><span className="ml-auto text-xs text-muted">{dateTime(l.createdOn)}</span></div>
              <p className="mt-1 text-muted">{l.message}</p>
            </li>
          ))}</ul>
        )}</Panel>
      )}

      {tab === 2 && (
        <Panel noPad>{!data.recentBookings.length ? <EmptyState title="No bookings yet" /> : (
          <ul className="divide-y divide-line">{data.recentBookings.map((b) => (
            <li key={b.id} className="flex flex-wrap items-center gap-3 px-5 py-3.5 text-sm">
              <span className="w-44 font-medium">{dateTime(b.scheduledStart)}</span><span className="flex-1">{b.serviceName} · {b.customerName}</span>
              <StatusBadge type="BookingStatus" code={b.status} /><span className="w-24 text-right font-semibold">{money(b.amount)}</span>
            </li>
          ))}</ul>
        )}</Panel>
      )}

      {tab === 3 && (
        <Panel noPad>{!data.recentReviews.length ? <EmptyState title="No reviews yet" /> : (
          <ul className="divide-y divide-line">{data.recentReviews.map((r) => (
            <li key={r.id} className="px-5 py-4 text-sm">
              <div className="flex items-center gap-2"><Stars value={r.rating} /><span className="font-semibold">{r.title}</span><StatusBadge type="ReviewStatus" code={r.status} /><span className="ml-auto text-xs text-muted">{ago(r.createdOn)}</span></div>
              <p className="mt-1 text-ink-2">{r.comment}</p><p className="mt-1 text-xs text-muted">{r.customerName}</p>
            </li>
          ))}</ul>
        )}</Panel>
      )}

      {tab === 4 && (
        <div className="grid gap-6 xl:grid-cols-2">
          <Panel title="Advertising campaigns" noPad>{!data.advertisements.length ? <EmptyState title="No campaigns" /> : (
            <ul className="divide-y divide-line">{data.advertisements.map((a) => (
              <li key={a.id} className="px-5 py-3.5 text-sm">
                <div className="flex items-center gap-2"><span className="font-semibold">{a.title}</span><span className="ml-auto"><StatusBadge type="AdStatus" code={a.status} /></span></div>
                <div className="mt-1 text-xs text-muted">{a.campaignCode} · {date(a.startDate)} - {date(a.endDate)} · {moneyExact(a.budget)} · {number(a.impressions)} impressions · {a.ctr}% CTR</div>
              </li>
            ))}</ul>
          )}</Panel>
          <Panel title="Subscription history" noPad>
            <ul className="divide-y divide-line">{data.subscriptions.map((x) => (
              <li key={x.subscriptionNumber} className="flex items-center gap-3 px-5 py-3 text-sm">
                <span className="flex-1 font-medium">{x.planName} <span className="text-xs text-muted">· {x.billingCycle}</span></span>
                <span className="text-xs text-muted">{date(x.startDate)} - {date(x.endDate)}</span>
                <StatusBadge type="SubscriptionStatus" code={x.status} />
              </li>
            ))}</ul>
          </Panel>
        </div>
      )}

      <ConfirmDialog open={!!confirm} title={confirm?.title ?? ''} destructive={confirm?.destructive} loading={update.isPending}
        message="The business owner will be notified of this change." onConfirm={() => confirm && update.mutate(confirm.body)} onClose={() => setConfirm(null)} />
    </>
  );
}
