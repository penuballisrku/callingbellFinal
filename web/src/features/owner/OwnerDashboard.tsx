import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Button, LinearProgress, Skeleton } from '@mui/material';
import { api } from '@/lib/api';
import { ago, date, dateTime, money, number } from '@/lib/format';
import { useDocumentTitle } from '@/lib/hooks';
import type { OwnerDashboard as Dashboard } from '@/lib/types';
import { TrendChart, BarsChart, RankedBars } from '@/components/charts';
import { EmptyState, ErrorState, KpiCard, KpiSkeletons, PageHeader, Panel, StatusBadge } from '@/components/ui';
import { useBusiness } from './OwnerPortal';
import { useAuth } from '@/stores/auth';

export default function OwnerDashboard() {
  const business = useBusiness();
  const firstName = useAuth((s) => s.user?.displayName ?? '').replace(/^(Dr|Adv|Ar|Prof)\.?\s+/i, '').split(' ')[0];
  useDocumentTitle('Business dashboard');
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['owner', 'dashboard', business.id],
    queryFn: () => api.get<Dashboard>(`/api/owner/businesses/${business.id}/dashboard`),
  });

  if (isError) return <ErrorState onRetry={() => refetch()} />;
  const primary = data?.kpis.filter((k) => ['views', 'leads', 'bookings', 'revenue'].includes(k.key)) ?? [];
  const secondary = data?.kpis.filter((k) => ['conversion', 'contacts', 'newLeads', 'pendingBookings'].includes(k.key)) ?? [];
  const leadTotal = data?.leadStatuses.reduce((a, s) => a + s.count, 0) ?? 0;

  return (
    <>
      <PageHeader title={`Good ${greeting()}, ${firstName}`} subtitle="Here's how your business performed in the last 30 days."
        actions={<><Button variant="outlined" component={Link} to="/business/leads">View leads</Button><Button variant="contained" component={Link} to="/business/bookings">Manage bookings</Button></>} />

      {business.status !== 'Active' && (
        <div className="mb-6 rounded-xl border border-warning-line bg-warning-soft px-4 py-3 text-sm">
          Your listing is <strong>{business.status === 'PendingApproval' ? 'awaiting approval' : business.status.toLowerCase()}</strong>. It isn't visible to customers yet.
        </div>
      )}

      <div className="grid grid-cols-2 gap-4 xl:grid-cols-4">{isLoading ? <KpiSkeletons /> : primary.map((k) => <KpiCard key={k.key} kpi={k} />)}</div>
      <div className="mt-4 grid grid-cols-2 gap-4 xl:grid-cols-4">
        {isLoading ? <KpiSkeletons /> : secondary.map((k) => <KpiCard key={k.key} kpi={k} hint={k.key === 'conversion' ? 'last 90 days' : k.key === 'contacts' ? undefined : 'needs action'} />)}
      </div>

      <div className="mt-6 grid gap-6 xl:grid-cols-3">
        <Panel title="Profile views" subtitle="Weekly, last 12 weeks" className="xl:col-span-2">
          {isLoading ? <Skeleton variant="rounded" height={260} /> : <TrendChart data={data!.weekly} x="week" area lines={[{ key: 'views', label: 'Profile views' }]} />}
        </Panel>
        <Panel title="Lead sources" subtitle="Last 90 days">
          {isLoading ? <Skeleton variant="rounded" height={220} /> : data!.leadSources.length === 0 ? <EmptyState title="No leads yet" /> : (
            <RankedBars rows={data!.leadSources.map((s) => ({ key: s.name, label: s.name, value: s.count }))} valueLabel="Leads by source" />
          )}
        </Panel>
      </div>

      <div className="mt-6 grid gap-6 xl:grid-cols-3">
        <Panel title="Leads & bookings" subtitle="Weekly, last 12 weeks" className="xl:col-span-2">
          {isLoading ? <Skeleton variant="rounded" height={260} /> : <BarsChart data={data!.weekly} x="week" bars={[{ key: 'leads', label: 'Leads' }, { key: 'bookings', label: 'Bookings' }]} />}
        </Panel>
        <Panel title="Lead pipeline" subtitle="Last 90 days">
          {isLoading ? <Skeleton variant="rounded" height={220} /> : (
            <ul className="space-y-3">
              {['New', 'Contacted', 'Quoted', 'Converted', 'Lost'].map((s) => {
                const n = data!.leadStatuses.find((x) => x.name === s)?.count ?? 0;
                return (
                  <li key={s} className="flex items-center gap-3 text-sm">
                    <span className="w-28"><StatusBadge type="EnquiryStatus" code={s} /></span>
                    <LinearProgress variant="determinate" value={leadTotal ? (n / leadTotal) * 100 : 0} sx={{ flex: 1, height: 8, borderRadius: 4, bgcolor: 'var(--cb-subtle)', '& .MuiLinearProgress-bar': { bgcolor: '#2a78d6', borderRadius: 4 } }} />
                    <span className="w-10 text-right font-semibold tabular-nums">{number(n)}</span>
                  </li>
                );
              })}
            </ul>
          )}
        </Panel>
      </div>

      <div className="mt-6 grid gap-6 xl:grid-cols-2">
        <Panel title="Recent leads" action={<Button size="small" component={Link} to="/business/leads">View all</Button>} noPad>
          {isLoading ? <div className="p-5"><Skeleton height={200} /></div> : !data!.recentLeads.length ? <EmptyState title="No leads yet" message="Leads from your profile and search will appear here." /> : (
            <ul className="divide-y divide-line">
              {data!.recentLeads.map((l) => (
                <li key={l.id} className="px-5 py-3.5">
                  <div className="flex items-center justify-between gap-2">
                    <span className="truncate text-sm font-semibold">{l.customerName}</span>
                    <span className="shrink-0 text-xs text-muted">{ago(l.createdOn)}</span>
                  </div>
                  <p className="truncate text-[13px] text-muted">{l.message}</p>
                  <div className="mt-1.5 flex gap-2"><StatusBadge type="EnquiryType" code={l.enquiryType} /><StatusBadge type="EnquiryStatus" code={l.status} /></div>
                </li>
              ))}
            </ul>
          )}
        </Panel>
        <Panel title="Upcoming bookings" action={<Button size="small" component={Link} to="/business/bookings">View all</Button>} noPad>
          {isLoading ? <div className="p-5"><Skeleton height={200} /></div> : !data!.upcomingBookings.length ? <EmptyState title="No upcoming bookings" message="New bookings will show up here instantly." /> : (
            <ul className="divide-y divide-line">
              {data!.upcomingBookings.map((b) => (
                <li key={b.id} className="flex items-center gap-3 px-5 py-3.5">
                  <div className="w-14 shrink-0 rounded-lg bg-canvas py-1.5 text-center">
                    <div className="text-[11px] font-semibold uppercase text-muted">{new Date(b.scheduledStart).toLocaleDateString('en-IN', { month: 'short' })}</div>
                    <div className="text-lg font-bold leading-5">{new Date(b.scheduledStart).getDate()}</div>
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="truncate text-sm font-semibold">{b.serviceName}</div>
                    <div className="truncate text-[13px] text-muted">{b.customerName} · {dateTime(b.scheduledStart)}</div>
                  </div>
                  <div className="text-right"><StatusBadge type="BookingStatus" code={b.status} /><div className="mt-1 text-sm font-semibold">{money(b.amount)}</div></div>
                </li>
              ))}
            </ul>
          )}
        </Panel>
      </div>

      {data?.plan && (
        <Panel title="Your plan" className="mt-6">
          <div className="flex flex-col gap-4 sm:flex-row sm:items-center">
            <div className="flex-1">
              <div className="text-lg font-bold">{data.plan.name} <span className="text-sm font-normal text-muted">· {data.plan.billingCycle}</span></div>
              <div className="text-sm text-muted">{data.plan.renewsOn ? `Renews on ${date(data.plan.renewsOn)}` : 'Free forever'}</div>
            </div>
            <div className="flex-1">
              <div className="mb-1 flex justify-between text-sm"><span>Lead credits used this month</span><span className="font-semibold">{data.plan.leadsThisMonth} / {data.plan.leadCredits}</span></div>
              <LinearProgress variant="determinate" value={Math.min(100, (data.plan.leadsThisMonth / Math.max(1, data.plan.leadCredits)) * 100)}
                sx={{ height: 8, borderRadius: 4, bgcolor: 'var(--cb-subtle)', '& .MuiLinearProgress-bar': { bgcolor: data.plan.leadsThisMonth > data.plan.leadCredits ? '#D92D20' : '#F4A62C', borderRadius: 4 } }} />
            </div>
            <Button variant="outlined" component={Link} to="/business/plan">Manage plan</Button>
          </div>
        </Panel>
      )}
    </>
  );
}

function greeting() {
  const h = new Date().getHours();
  return h < 12 ? 'morning' : h < 17 ? 'afternoon' : 'evening';
}
