import { useState } from 'react';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Drawer, IconButton, InputAdornment, MenuItem, Pagination, Skeleton, Tab, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Tabs, TextField,
} from '@mui/material';
import SearchRounded from '@mui/icons-material/SearchRounded';
import CloseRounded from '@mui/icons-material/CloseRounded';
import CallRounded from '@mui/icons-material/CallRounded';
import WhatsApp from '@mui/icons-material/WhatsApp';
import { useSnackbar } from 'notistack';
import { api, errorMessage } from '@/lib/api';
import { ago, date, dateTime, money, number } from '@/lib/format';
import { useDebounced, useDocumentTitle, useLookup } from '@/lib/hooks';
import type { OwnerBooking, OwnerLead, OwnerList } from '@/lib/types';
import { ConfirmDialog, EmptyState, ErrorState, PageHeader, StatusBadge } from '@/components/ui';
import { useBusiness } from './OwnerPortal';


export function OwnerLeads() {
  useDocumentTitle('Leads');
  const business = useBusiness();
  const types = useLookup('EnquiryType');
  const [status, setStatus] = useState('');
  const [type, setType] = useState('');
  const [q, setQ] = useState('');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<OwnerLead | null>(null);
  const search = useDebounced(q);

  const { data, isLoading, isError, isFetching, refetch } = useQuery({
    queryKey: ['owner', 'leads', business.id, status, type, search, page],
    queryFn: () => api.get<OwnerList<OwnerLead>>(`/api/owner/businesses/${business.id}/leads`, { status, type, q: search, page, pageSize: 15 }),
    placeholderData: keepPreviousData,
  });
  const total = Object.values(data?.statusCounts ?? {}).reduce((a, n) => a + n, 0);
  // Lead status tabs come from the EnquiryStatus lookup table.
  const leadStatuses = [...useLookup('EnquiryStatus')].sort((a, b) => a.sortOrder - b.sortOrder);

  return (
    <>
      <PageHeader title="Leads" subtitle="Enquiries, quotation and callback requests from customers. Respond quickly - fast replies win more jobs." />
      <Tabs value={status} onChange={(_, v) => { setStatus(v); setPage(1); }} variant="scrollable" allowScrollButtonsMobile sx={{ borderBottom: '1px solid var(--cb-line)', mb: 2 }}>
        <Tab value="" label={`All (${number(total)})`} />
        {leadStatuses.map((s) => <Tab key={s.code} value={s.code} label={`${s.name} (${number(data?.statusCounts[s.code] ?? 0)})`} />)}
      </Tabs>
      <div className="mb-4 flex flex-col gap-3 sm:flex-row">
        <TextField placeholder="Search by name, phone, message or lead number" value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }}
          slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRounded fontSize="small" /></InputAdornment> } }} />
        <TextField select value={type} onChange={(e) => { setType(e.target.value); setPage(1); }} sx={{ minWidth: 200 }} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Request type' } }}>
          <MenuItem value="">All request types</MenuItem>
          {types.map((t) => <MenuItem key={t.code} value={t.code}>{t.name}</MenuItem>)}
        </TextField>
        {(status || type || q) && <Button onClick={() => { setStatus(''); setType(''); setQ(''); setPage(1); }}>Reset</Button>}
      </div>

      <div className="card overflow-hidden">
        {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <div className="p-5"><Skeleton height={320} /></div> : !data!.page.items.length ? (
          <EmptyState title="No leads match these filters" message="Try another status or clear the search." />
        ) : (
          <TableContainer className={isFetching ? 'opacity-60' : ''}>
            <Table size="small" className="table-stack">
              <TableHead>
                <TableRow><TableCell>Customer</TableCell><TableCell>Request</TableCell><TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}>Service</TableCell><TableCell>Status</TableCell><TableCell sx={{ display: { xs: 'none', sm: 'table-cell' } }}>Received</TableCell></TableRow>
              </TableHead>
              <TableBody>
                {data!.page.items.map((l) => (
                  <TableRow key={l.id} hover onClick={() => setSelected(l)} sx={{ cursor: 'pointer' }} tabIndex={0} onKeyDown={(e) => e.key === 'Enter' && setSelected(l)}>
                    <TableCell data-primary><div className="font-semibold">{l.customerName}</div><div className="text-xs text-muted">{l.customerPhone}</div></TableCell>
                    <TableCell data-label="Request" sx={{ maxWidth: 360 }}><StatusBadge type="EnquiryType" code={l.enquiryType} /><div className="mt-1 truncate text-[13px] text-muted">{l.message}</div></TableCell>
                    <TableCell data-label="Service" sx={{ display: { xs: 'none', md: 'table-cell' } }}>{l.serviceName ?? 'General'}</TableCell>
                    <TableCell data-label="Status"><StatusBadge type="EnquiryStatus" code={l.status} /></TableCell>
                    <TableCell data-label="Received" sx={{ display: { xs: 'none', sm: 'table-cell' }, whiteSpace: 'nowrap' }}>{ago(l.createdOn)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </div>
      {data && data.page.meta.totalPages > 1 && <div className="mt-6 flex justify-center"><Pagination count={data.page.meta.totalPages} page={page} onChange={(_, p) => setPage(p)} shape="rounded" /></div>}
      <LeadDrawer lead={selected} onClose={() => setSelected(null)} />
    </>
  );
}

function LeadDrawer({ lead, onClose }: { lead: OwnerLead | null; onClose: () => void }) {
  const business = useBusiness();
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const [quote, setQuote] = useState('');
  const update = useMutation({
    mutationFn: (body: { status: string; quotedAmount?: number }) => api.patch<OwnerLead>(`/api/owner/businesses/${business.id}/leads/${lead!.id}`, body),
    onSuccess: (res) => {
      enqueueSnackbar(`Lead marked as ${res.data.status.toLowerCase()}`, { variant: 'success' });
      void queryClient.invalidateQueries({ queryKey: ['owner'] });
      onClose();
    },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });

  return (
    <Drawer anchor="right" open={!!lead} onClose={onClose} slotProps={{ paper: { sx: { width: 440, maxWidth: '100vw' } } }}>
      {lead && (
        <div className="flex h-full flex-col">
          <div className="flex items-start justify-between border-b border-line p-5">
            <div>
              <div className="text-xs text-muted">{lead.enquiryNumber}</div>
              <h2 className="text-lg font-bold">{lead.customerName}</h2>
              <div className="mt-1 flex gap-2"><StatusBadge type="EnquiryType" code={lead.enquiryType} /><StatusBadge type="EnquiryStatus" code={lead.status} /></div>
            </div>
            <IconButton onClick={onClose} aria-label="Close"><CloseRounded /></IconButton>
          </div>
          <div className="flex-1 space-y-5 overflow-y-auto p-5 text-sm">
            <div className="grid grid-cols-2 gap-2">
              <Button variant="outlined" startIcon={<CallRounded />} href={`tel:${lead.customerPhone.replace(/\s/g, '')}`}>Call</Button>
              <Button variant="outlined" startIcon={<WhatsApp />} href={`https://wa.me/${lead.customerPhone.replace(/\D/g, '')}`} target="_blank" rel="noopener">WhatsApp</Button>
            </div>
            <div className="rounded-lg bg-canvas p-4 leading-6">{lead.message}</div>
            <dl className="grid grid-cols-2 gap-4">
              <Info label="Phone" value={lead.customerPhone} />
              <Info label="Email" value={lead.customerEmail ?? 'Not shared'} />
              <Info label="Service" value={lead.serviceName ?? 'General enquiry'} />
              <Info label="Source" value={lead.source} />
              <Info label="Received" value={dateTime(lead.createdOn)} />
              <Info label="Preferred date" value={lead.preferredDate ? date(lead.preferredDate) : 'Flexible'} />
              {lead.budget != null && <Info label="Budget" value={money(lead.budget)} />}
              {lead.quotedAmount != null && <Info label="Your quote" value={money(lead.quotedAmount)} />}
            </dl>
            <div className="border-t border-line pt-5">
              <h3 className="mb-2 font-semibold">Send a quote</h3>
              <div className="flex gap-2">
                <TextField type="number" placeholder="Amount in ₹" value={quote} onChange={(e) => setQuote(e.target.value)} slotProps={{ htmlInput: { min: 1, 'aria-label': 'Quote amount' } }} />
                <Button variant="contained" disabled={!Number(quote) || update.isPending} onClick={() => update.mutate({ status: 'Quoted', quotedAmount: Number(quote) })}>Quote</Button>
              </div>
            </div>
          </div>
          <div className="grid grid-cols-3 gap-2 border-t border-line p-4">
            <Button variant="outlined" disabled={update.isPending || lead.status === 'Contacted'} onClick={() => update.mutate({ status: 'Contacted' })}>Contacted</Button>
            <Button variant="contained" color="success" disabled={update.isPending || lead.status === 'Converted'} onClick={() => update.mutate({ status: 'Converted' })}>Won</Button>
            <Button variant="outlined" color="error" disabled={update.isPending || lead.status === 'Lost'} onClick={() => update.mutate({ status: 'Lost' })}>Lost</Button>
          </div>
        </div>
      )}
    </Drawer>
  );
}

const Info = ({ label, value }: { label: string; value: string }) => (
  <div><dt className="text-xs text-muted">{label}</dt><dd className="mt-0.5 font-medium">{value}</dd></div>
);

const bookingActions: Record<string, { status: string; label: string; color?: 'success' | 'error' | 'primary' }[]> = {
  Pending: [{ status: 'Confirmed', label: 'Confirm', color: 'primary' }, { status: 'Cancelled', label: 'Decline', color: 'error' }],
  Confirmed: [{ status: 'Completed', label: 'Complete', color: 'success' }, { status: 'NoShow', label: 'No-show' }, { status: 'Cancelled', label: 'Cancel', color: 'error' }],
};

export function OwnerBookings() {
  useDocumentTitle('Bookings');
  const business = useBusiness();
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const statuses = useLookup('BookingStatus');
  const [scope, setScope] = useState('upcoming');
  const [status, setStatus] = useState('');
  const [q, setQ] = useState('');
  const [page, setPage] = useState(1);
  const [action, setAction] = useState<{ booking: OwnerBooking; status: string; label: string } | null>(null);
  const search = useDebounced(q);

  const { data, isLoading, isError, isFetching, refetch } = useQuery({
    queryKey: ['owner', 'bookings', business.id, scope, status, search, page],
    queryFn: () => api.get<OwnerList<OwnerBooking>>(`/api/owner/businesses/${business.id}/bookings`, { scope, status, q: search, page, pageSize: 15 }),
    placeholderData: keepPreviousData,
  });

  const update = useMutation({
    mutationFn: (a: { id: string; status: string }) => api.patch<OwnerBooking>(`/api/owner/businesses/${business.id}/bookings/${a.id}`, { status: a.status }),
    onSuccess: (res) => { enqueueSnackbar(res.message, { variant: 'success' }); setAction(null); void queryClient.invalidateQueries({ queryKey: ['owner'] }); },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });

  return (
    <>
      <PageHeader title="Bookings" subtitle="Confirm new requests, mark visits complete and keep your calendar accurate."
        actions={statuses.filter((s) => ['Pending', 'Confirmed'].includes(s.code)).map((s) => (
          <span key={s.code} className="rounded-lg border border-line bg-surface px-3 py-1.5 text-sm"><span className="text-muted">{s.name}:</span> <strong>{number(data?.statusCounts[s.code] ?? 0)}</strong></span>
        ))} />
      <Tabs value={scope} onChange={(_, v) => { setScope(v); setPage(1); }} sx={{ borderBottom: '1px solid var(--cb-line)', mb: 2 }} variant="scrollable" allowScrollButtonsMobile>
        <Tab value="upcoming" label="Upcoming" /><Tab value="today" label="Today" /><Tab value="past" label="Past" /><Tab value="all" label="All" />
      </Tabs>
      <div className="mb-4 flex flex-col gap-3 sm:flex-row">
        <TextField placeholder="Search customer, phone, service or booking number" value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }}
          slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRounded fontSize="small" /></InputAdornment> } }} />
        <TextField select value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }} sx={{ minWidth: 180 }} slotProps={{ select: { displayEmpty: true }, htmlInput: { 'aria-label': 'Status' } }}>
          <MenuItem value="">All statuses</MenuItem>
          {statuses.map((s) => <MenuItem key={s.code} value={s.code}>{s.name} ({data?.statusCounts[s.code] ?? 0})</MenuItem>)}
        </TextField>
      </div>

      <div className="card overflow-hidden">
        {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <div className="p-5"><Skeleton height={320} /></div> : !data!.page.items.length ? (
          <EmptyState title="No bookings here" message={scope === 'today' ? 'Nothing scheduled for today.' : 'Try a different tab or filter.'} />
        ) : (
          <TableContainer className={isFetching ? 'opacity-60' : ''}>
            <Table size="small" className="table-stack">
              <TableHead>
                <TableRow><TableCell>When</TableCell><TableCell>Customer</TableCell><TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}>Service</TableCell><TableCell align="right">Amount</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell></TableRow>
              </TableHead>
              <TableBody>
                {data!.page.items.map((b) => (
                  <TableRow key={b.id} hover>
                    <TableCell data-label="When" sx={{ whiteSpace: 'nowrap' }}><div className="font-semibold">{date(b.scheduledStart)}</div><div className="text-xs text-muted">{new Date(b.scheduledStart).toLocaleTimeString('en-IN', { hour: 'numeric', minute: '2-digit' })}</div></TableCell>
                    <TableCell data-label="Customer"><div className="font-medium">{b.customerName}</div><div className="text-xs text-muted">{b.customerPhone}</div></TableCell>
                    <TableCell data-label="Service" sx={{ display: { xs: 'none', md: 'table-cell' }, maxWidth: 280 }}><div>{b.serviceName}</div>{b.serviceAddress && <div className="truncate text-xs text-muted">{b.serviceAddress}</div>}</TableCell>
                    <TableCell data-label="Amount" align="right"><div className="font-semibold">{money(b.amount)}</div><div className="text-xs text-muted">{b.paymentStatus}</div></TableCell>
                    <TableCell data-label="Status"><StatusBadge type="BookingStatus" code={b.status} /></TableCell>
                    <TableCell data-label="" align="right" sx={{ whiteSpace: 'nowrap' }}>
                      {(bookingActions[b.status] ?? []).map((a) => (
                        <Button key={a.status} size="small" color={a.color ?? 'inherit'} variant={a.color === 'primary' || a.color === 'success' ? 'contained' : 'text'} sx={{ ml: 0.5 }}
                          onClick={() => setAction({ booking: b, status: a.status, label: a.label })}>{a.label}</Button>
                      ))}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </div>
      {data && data.page.meta.totalPages > 1 && <div className="mt-6 flex justify-center"><Pagination count={data.page.meta.totalPages} page={page} onChange={(_, p) => setPage(p)} shape="rounded" /></div>}

      <ConfirmDialog open={!!action} loading={update.isPending} title={`${action?.label} booking?`} confirmLabel={action?.label}
        destructive={action?.status === 'Cancelled' || action?.status === 'NoShow'}
        message={action ? `${action.booking.serviceName} for ${action.booking.customerName} on ${dateTime(action.booking.scheduledStart)}. The customer will be notified.` : ''}
        onConfirm={() => action && update.mutate({ id: action.booking.id, status: action.status })} onClose={() => setAction(null)} />
    </>
  );
}
