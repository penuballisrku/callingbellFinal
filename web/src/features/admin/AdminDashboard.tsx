import { useState } from 'react';
import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Button, Skeleton, ToggleButton, ToggleButtonGroup } from '@mui/material';
import { api } from '@/lib/api';
import { moneyShort, number } from '@/lib/format';
import { useDocumentTitle } from '@/lib/hooks';
import type { AdminDashboard as Dashboard, MonthlyPoint } from '@/lib/types';
import { BarsChart, RankedBars, TrendChart } from '@/components/charts';
import { ErrorState, KpiCard, KpiSkeletons, PageHeader, Panel } from '@/components/ui';

const flatten = (points: MonthlyPoint[]) => points.map((p) => ({ month: p.month, ...p.values }));

export default function AdminDashboard() {
  useDocumentTitle('Admin dashboard');
  const [usersRange, setUsersRange] = useState<30 | 90>(90);
  const { data, isLoading, isError, refetch } = useQuery({ queryKey: ['admin', 'dashboard'], queryFn: () => api.get<Dashboard>('/api/admin/dashboard') });
  if (isError) return <ErrorState onRetry={() => refetch()} />;

  const k = (key: string) => data?.kpis.find((x) => x.key === key);
  const row1 = ['totalBusinesses', 'activeBusinesses', 'newBusinesses', 'customers', 'revenue'];
  const row2 = ['leads', 'newEnquiries', 'bookings', 'pendingBookings', 'activeSubscriptions'];
  const row3 = ['advertisements', 'reviews', 'averageRating', 'dau', 'mau'];
  const a = data?.attention;
  const dau = data?.activeUsers.slice(-usersRange).map((d) => ({ ...d, label: new Date(d.date).toLocaleDateString('en-IN', { day: 'numeric', month: 'short' }) })) ?? [];

  return (
    <>
      <PageHeader title="Platform overview" subtitle="Live metrics across businesses, customers, leads, bookings and revenue." />

      {a && (a.pendingApprovals + a.flaggedReviews + a.pendingAds + a.pendingVerifications) > 0 && (
        <div className="mb-6 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          {[
            ['Businesses awaiting approval', a.pendingApprovals, '/admin/businesses?status=PendingApproval'],
            ['Pending verifications', a.pendingVerifications, '/admin/businesses?verification=Pending'],
            ['Reported reviews', a.flaggedReviews, '/admin/reviews'],
            ['Campaigns to approve', a.pendingAds, '/admin/advertisements'],
          ].map(([label, n, to]) => (
            <Link key={label as string} to={to as string} className="flex items-center justify-between rounded-xl border border-warning-line bg-warning-soft px-4 py-3 text-sm hover:border-warning">
              <span className="text-warning-ink">{label}</span><span className="text-lg font-bold text-warning-ink">{n as number}</span>
            </Link>
          ))}
        </div>
      )}

      {[row1, row2, row3].map((row, i) => (
        <div key={i} className="mb-4 grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-5">
          {isLoading ? <KpiSkeletons count={5} /> : row.map((key) => k(key) && <KpiCard key={key} kpi={k(key)!} hint={key === 'customers' ? 'growth in 30 days' : key === 'dau' ? 'vs 30 days ago' : undefined} />)}
        </div>
      ))}

      <div className="mt-6 grid gap-6 xl:grid-cols-2">
        <Panel title="Revenue" subtitle="Monthly, by stream (excl. GST)">
          {isLoading ? <Skeleton variant="rounded" height={280} /> : (
            <BarsChart data={flatten(data!.revenue)} x="month" money stacked height={280} bars={[
              { key: 'Subscription', label: 'Subscriptions' }, { key: 'Advertisement', label: 'Advertising' },
              { key: 'BookingCommission', label: 'Booking commission' }, { key: 'LeadCredits', label: 'Lead credits' },
            ]} />
          )}
        </Panel>
        <Panel title="Business & customer registrations" subtitle="New sign-ups per month">
          {isLoading ? <Skeleton variant="rounded" height={280} /> : (
            <BarsChart data={flatten(data!.registrations)} x="month" height={280} bars={[{ key: 'businesses', label: 'Businesses' }, { key: 'customers', label: 'Customers' }]} />
          )}
        </Panel>
        <Panel title="Leads" subtitle="Monthly enquiries and conversions">
          {isLoading ? <Skeleton variant="rounded" height={260} /> : <TrendChart data={flatten(data!.leads)} x="month" lines={[{ key: 'total', label: 'Leads' }, { key: 'converted', label: 'Converted' }]} />}
        </Panel>
        <Panel title="Bookings" subtitle="Monthly bookings by outcome">
          {isLoading ? <Skeleton variant="rounded" height={260} /> : <TrendChart data={flatten(data!.bookings)} x="month" lines={[{ key: 'total', label: 'All bookings' }, { key: 'completed', label: 'Completed' }, { key: 'cancelled', label: 'Cancelled' }]} />}
        </Panel>
      </div>

      <Panel title="Active users" subtitle="Daily active users (DAU) and rolling 30-day monthly active users (MAU)" className="mt-6"
        action={<ToggleButtonGroup size="small" exclusive value={usersRange} onChange={(_, v) => v && setUsersRange(v)}><ToggleButton value={30}>30d</ToggleButton><ToggleButton value={90}>90d</ToggleButton></ToggleButtonGroup>}>
        {isLoading ? <Skeleton variant="rounded" height={260} /> : (
          <div className="grid gap-6 lg:grid-cols-2">
            <div><div className="mb-2 text-sm font-semibold">Daily active users</div><TrendChart data={dau} x="label" area lines={[{ key: 'dau', label: 'DAU' }]} /></div>
            <div><div className="mb-2 text-sm font-semibold">Monthly active users</div><TrendChart data={dau} x="label" area lines={[{ key: 'mau', label: 'MAU', color: 1 }]} /></div>
          </div>
        )}
      </Panel>

      <div className="mt-6 grid gap-6 xl:grid-cols-3">
        <Panel title="Businesses by category" subtitle="Active listings">
          {isLoading ? <Skeleton variant="rounded" height={320} /> : <RankedBars rows={data!.categories.map((c) => ({ key: c.key, label: c.name, value: c.count }))} valueLabel="Businesses by category" />}
        </Panel>
        <Panel title="Businesses by city" subtitle="Active listings">
          {isLoading ? <Skeleton variant="rounded" height={320} /> : <RankedBars rows={data!.cities.map((c) => ({ key: c.key, label: c.name, value: c.count }))} valueLabel="Businesses by city" tone={2} />}
        </Panel>
        <Panel title="Subscriptions" subtitle="Current subscribers and monthly recurring revenue" action={<Button size="small" component={Link} to="/admin/subscriptions">Details</Button>}>
          {isLoading ? <Skeleton variant="rounded" height={320} /> : (
            <>
              <RankedBars rows={data!.subscriptions.map((s) => ({ key: s.key, label: s.name, value: s.count, sub: s.value ? `· ${moneyShort(s.value)} MRR` : undefined }))} valueLabel="Subscriptions by plan" tone={6} />
              <div className="mt-5 flex items-center justify-between border-t border-line pt-4 text-sm">
                <span className="text-muted">Total MRR</span>
                <span className="text-lg font-bold">{moneyShort(data!.subscriptions.reduce((s, x) => s + (x.value ?? 0), 0))}</span>
              </div>
              <div className="flex items-center justify-between text-sm"><span className="text-muted">Paying subscribers</span><span className="font-semibold">{number(data!.subscriptions.filter((s) => s.key !== 'FREE').reduce((s, x) => s + x.count, 0))}</span></div>
            </>
          )}
        </Panel>
      </div>
    </>
  );
}
