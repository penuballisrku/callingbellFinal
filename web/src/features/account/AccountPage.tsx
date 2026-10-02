import { useState } from 'react';
import { Link } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Pagination, Skeleton, Tab, Tabs } from '@mui/material';
import { useSnackbar } from 'notistack';
import { api, errorMessage } from '@/lib/api';
import { ago, dateTime, money } from '@/lib/format';
import { useDocumentTitle } from '@/lib/hooks';
import { useAuth } from '@/stores/auth';
import type { BusinessCard as Card, MyBooking, MyEnquiry } from '@/lib/types';
import { BusinessCard } from '@/components/BusinessCard';
import { ConfirmDialog, EmptyState, ErrorState, Img, PageHeader, StatusBadge } from '@/components/ui';
import { ReviewDialog } from '@/features/business/Dialogs';

export default function AccountPage() {
  useDocumentTitle('My account');
  const user = useAuth((s) => s.user);
  const [tab, setTab] = useState(0);
  return (
    <div className="container-page py-8">
      <PageHeader title={`Hi, ${user?.displayName.split(' ')[0]}`} subtitle="Your bookings, saved businesses and requests in one place." crumbs={[{ label: 'Home', to: '/' }, { label: 'My account' }]} />
      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ borderBottom: '1px solid var(--cb-line)', mb: 3 }} variant="scrollable" allowScrollButtonsMobile>
        <Tab label="Upcoming bookings" /><Tab label="Past bookings" /><Tab label="Saved" /><Tab label="My requests" />
      </Tabs>
      {tab === 0 && <Bookings scope="upcoming" />}
      {tab === 1 && <Bookings scope="past" />}
      {tab === 2 && <Favorites />}
      {tab === 3 && <Enquiries />}
    </div>
  );
}

function Bookings({ scope }: { scope: 'upcoming' | 'past' }) {
  const [page, setPage] = useState(1);
  const [cancel, setCancel] = useState<MyBooking | null>(null);
  const [review, setReview] = useState<MyBooking | null>(null);
  const { enqueueSnackbar } = useSnackbar();
  const queryClient = useQueryClient();
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['me', 'bookings', scope, page],
    queryFn: () => api.paged<MyBooking>('/api/me/bookings', { scope, page, pageSize: 8 }),
    placeholderData: keepPreviousData,
  });
  const cancelMutation = useMutation({
    mutationFn: (id: string) => api.post(`/api/me/bookings/${id}/cancel`, { reason: 'Change of plans' }),
    onSuccess: () => { enqueueSnackbar('Booking cancelled', { variant: 'success' }); setCancel(null); void queryClient.invalidateQueries({ queryKey: ['me', 'bookings'] }); },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });

  if (isError) return <ErrorState onRetry={() => refetch()} />;
  if (isLoading) return <div className="space-y-3">{[0, 1, 2].map((i) => <Skeleton key={i} variant="rounded" height={110} />)}</div>;
  if (!data?.items.length) {
    return <div className="card"><EmptyState title={scope === 'upcoming' ? 'No upcoming bookings' : 'No past bookings yet'}
      message="Find a trusted professional and book in under a minute." action={<Button component={Link} to="/search?onlineBooking=true" variant="contained">Find services</Button>} /></div>;
  }

  return (
    <>
      <div className="space-y-3">
        {data.items.map((b) => (
          <article key={b.id} className="card flex flex-col gap-4 p-4 sm:flex-row sm:items-center">
            <Img src={b.businessLogoUrl} alt="" className="h-14 w-14 shrink-0" fallbackText={b.businessName} />
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-2">
                <Link to={`/b/${b.businessSlug}`} className="font-semibold hover:underline">{b.businessName}</Link>
                <StatusBadge type="BookingStatus" code={b.status} />
              </div>
              <p className="text-sm text-ink-2">{b.serviceName} · {dateTime(b.scheduledStart)}</p>
              <p className="text-xs text-muted">{b.bookingNumber}{b.serviceAddress && ` · ${b.serviceAddress}`}</p>
            </div>
            <div className="flex items-center gap-3 sm:flex-col sm:items-end">
              <span className="font-bold">{money(b.amount)}</span>
              <div className="flex gap-2">
                {b.businessPhone && scope === 'upcoming' && <Button size="small" variant="outlined" href={`tel:${b.businessPhone.replace(/\s/g, '')}`}>Call</Button>}
                {b.canCancel && <Button size="small" color="error" onClick={() => setCancel(b)}>Cancel</Button>}
                {b.canReview && <Button size="small" variant="contained" onClick={() => setReview(b)}>Rate & review</Button>}
              </div>
            </div>
          </article>
        ))}
      </div>
      {data.pagination.totalPages > 1 && <div className="mt-6 flex justify-center"><Pagination count={data.pagination.totalPages} page={page} onChange={(_, p) => setPage(p)} shape="rounded" /></div>}
      <ConfirmDialog open={!!cancel} title="Cancel this booking?" destructive confirmLabel="Cancel booking" loading={cancelMutation.isPending}
        message={cancel ? `${cancel.serviceName} with ${cancel.businessName} on ${dateTime(cancel.scheduledStart)}. Paid bookings are refunded to the original payment method.` : ''}
        onConfirm={() => cancel && cancelMutation.mutate(cancel.id)} onClose={() => setCancel(null)} />
      {review && <ReviewDialogForBooking booking={review} onClose={() => setReview(null)} />}
    </>
  );
}

function ReviewDialogForBooking({ booking, onClose }: { booking: MyBooking; onClose: () => void }) {
  const { data } = useQuery({ queryKey: ['business', booking.businessSlug], queryFn: () => api.get<{ card: Card }>(`/api/businesses/${booking.businessSlug}`) });
  if (!data) return null;
  return <ReviewDialog business={{ id: data.card.id, name: data.card.name, slug: booking.businessSlug }} open onClose={onClose} />;
}

function Favorites() {
  const { data, isLoading, isError, refetch } = useQuery({ queryKey: ['me', 'favorites'], queryFn: () => api.get<Card[]>('/api/me/favorites') });
  if (isError) return <ErrorState onRetry={() => refetch()} />;
  if (isLoading) return <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">{[0, 1, 2, 3].map((i) => <Skeleton key={i} variant="rounded" height={260} />)}</div>;
  if (!data?.length) return <div className="card"><EmptyState title="Nothing saved yet" message="Tap the heart on any business to save it here." action={<Button component={Link} to="/search" variant="contained">Explore businesses</Button>} /></div>;
  return <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">{data.map((b) => <BusinessCard key={b.id} b={b} />)}</div>;
}

function Enquiries() {
  const [page, setPage] = useState(1);
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['me', 'enquiries', page], queryFn: () => api.paged<MyEnquiry>('/api/me/enquiries', { page, pageSize: 10 }), placeholderData: keepPreviousData,
  });
  if (isError) return <ErrorState onRetry={() => refetch()} />;
  if (isLoading) return <Skeleton variant="rounded" height={300} />;
  if (!data?.items.length) return <div className="card"><EmptyState title="No requests yet" message="Quotes, enquiries and callback requests you send will appear here." /></div>;
  return (
    <>
      <div className="card divide-y divide-line">
        {data.items.map((e) => (
          <div key={e.id} className="flex flex-col gap-2 p-4 sm:flex-row sm:items-start">
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-2">
                <Link to={`/b/${e.businessSlug}`} className="font-semibold hover:underline">{e.businessName}</Link>
                <StatusBadge type="EnquiryType" code={e.enquiryType} />
                <StatusBadge type="EnquiryStatus" code={e.status} />
              </div>
              <p className="mt-1 line-clamp-2 text-sm text-ink-2">{e.message}</p>
              <p className="mt-1 text-xs text-muted">{e.enquiryNumber}{e.serviceName && ` · ${e.serviceName}`} · sent {ago(e.createdOn)}{e.respondedOn && ` · responded ${ago(e.respondedOn)}`}</p>
            </div>
            {e.quotedAmount && <div className="text-right text-sm"><span className="text-muted">Quoted</span><div className="text-lg font-bold">{money(e.quotedAmount)}</div></div>}
          </div>
        ))}
      </div>
      {data.pagination.totalPages > 1 && <div className="mt-6 flex justify-center"><Pagination count={data.pagination.totalPages} page={page} onChange={(_, p) => setPage(p)} shape="rounded" /></div>}
    </>
  );
}
