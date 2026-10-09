import { useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Button, LinearProgress, Skeleton, Tab, Tabs } from '@mui/material';
import InboxOutlined from '@mui/icons-material/InboxOutlined';
import EventNoteOutlined from '@mui/icons-material/EventNoteOutlined';
import ChatBubbleOutlineRounded from '@mui/icons-material/ChatBubbleOutlineRounded';
import TaskAltRounded from '@mui/icons-material/TaskAltRounded';
import { api } from '@/lib/api';
import { ago, dateTime, money, moneyPrecise, number } from '@/lib/format';
import { useBusinessPlans, useDocumentTitle } from '@/lib/hooks';
import type { ChatUnread, OwnerDashboard as Dashboard, OwnerOverview } from '@/lib/types';
import { TrendChart, BarsChart, RankedBars } from '@/components/charts';
import { EmptyState, ErrorState, KpiCard, KpiSkeletons, Panel, StatusBadge } from '@/components/ui';
import { useBusiness } from './OwnerPortal';
import { useAuth } from '@/stores/auth';
import { ActivityPanel, BusinessHero, CompletionPanel, MediaPanel, PlanPanel, WelcomeBanner, type AttentionItem } from './OwnerOverviewPanels';
import { PlanCheckoutDialog } from './PlanCheckoutDialog';

export default function OwnerDashboard() {
  const business = useBusiness();
  const firstName = useAuth((s) => s.user?.displayName ?? '').replace(/^(Dr|Adv|Ar|Prof)\.?\s+/i, '').split(' ')[0];
  useDocumentTitle('Business dashboard');
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['owner', 'dashboard', business.id],
    queryFn: () => api.get<Dashboard>(`/api/owner/businesses/${business.id}/dashboard`),
  });
  const overview = useQuery({
    queryKey: ['owner', 'overview', business.id],
    queryFn: () => api.get<OwnerOverview>(`/api/owner/businesses/${business.id}/overview`),
  });
  // Same query (and cache entry) as the sidebar's Messages badge.
  const unread = useQuery({ queryKey: ['chat', 'unread'], queryFn: () => api.get<ChatUnread>('/api/chat/unread'), refetchInterval: 120_000 });
  const [params, setParams] = useSearchParams();
  // Priced for this business's country, like its checkout.
  const plans = useBusinessPlans(business.id);
  const [payOpen, setPayOpen] = useState(false);
  const [chart, setChart] = useState<'views' | 'pipeline'>('views');
  const pending = overview.data?.pendingPayment;
  const pendingPlan = plans.data?.find((p) => p.code === pending?.planCode) ?? null;

  if (isError) return <ErrorState onRetry={() => refetch()} />;
  const kpi = (key: string) => data?.kpis.find((k) => k.key === key);
  const performance = data?.kpis.filter((k) => ['views', 'leads', 'bookings', 'revenue', 'conversion', 'contacts'].includes(k.key)) ?? [];
  const leadTotal = data?.leadStatuses.reduce((a, s) => a + s.count, 0) ?? 0;
  const completion = overview.data?.completion;
  const nextStep = completion?.items.find((i) => !i.done);
  const newLeads = kpi('newLeads')?.value;
  const toConfirm = kpi('pendingBookings')?.value;
  const unreadCount = unread.data?.asBusiness;

  const attention: AttentionItem[] = [
    { key: 'leads', label: 'New leads', value: newLeads, icon: <InboxOutlined />, to: '/owner/leads?status=New', urgent: !!newLeads,
      hint: newLeads ? 'Reply quickly. Fast replies win more jobs.' : 'You are all caught up.' },
    { key: 'bookings', label: 'Bookings to confirm', value: toConfirm, icon: <EventNoteOutlined />, to: '/owner/bookings?scope=all&status=Pending', urgent: !!toConfirm,
      hint: toConfirm ? 'Customers are waiting for your confirmation.' : 'Nothing waiting for confirmation.' },
    { key: 'messages', label: 'Unread messages', value: unreadCount, icon: <ChatBubbleOutlineRounded />, to: '/owner/messages', urgent: !!unreadCount,
      hint: unreadCount ? 'Customers are waiting for a reply.' : 'No unread chats.' },
    { key: 'profile', label: 'Profile complete', value: completion ? `${completion.percent}%` : undefined, icon: <TaskAltRounded />,
      to: nextStep?.linkUrl ?? '/owner/profile', urgent: !!completion && completion.percent < 80,
      hint: nextStep ? `Next: ${nextStep.label}` : 'Every profile step is done.' },
  ];

  return (
    <>
      {params.get('welcome') && <WelcomeBanner name={business.name} onDismiss={() => setParams({}, { replace: true })} />}

      <BusinessHero data={overview.data} greeting={`Good ${greeting()}${firstName ? `, ${firstName}` : ''}`} attention={attention} />

      {pending && pendingPlan && (
        <div className="mb-6 flex flex-col gap-3 rounded-xl border border-accent/40 bg-accent-soft p-4 sm:flex-row sm:items-center" role="status">
          <div className="min-w-0 flex-1 text-sm">
            <div className="font-semibold text-ink">Complete your {pending.planName} payment</div>
            <div className="text-ink-2">{pending.status === 'Failed' && pending.failureReason ? `Your last attempt failed: ${pending.failureReason} ` : 'Your payment was not completed. '}
              You are on the Free plan until you pay {moneyPrecise(pending.total, pending.currency)} ({pending.billingCycle.toLowerCase()}).</div>
          </div>
          <Button variant="contained" color="secondary" onClick={() => setPayOpen(true)}>Pay {moneyPrecise(pending.total, pending.currency)}</Button>
        </div>
      )}
      <PlanCheckoutDialog businessId={business.id} plan={pendingPlan} cycle={pending?.billingCycle === 'Annual' ? 'Annual' : 'Monthly'} open={payOpen}
        onClose={() => setPayOpen(false)} />

      {business.status !== 'Active' && (
        <div className="mb-6 rounded-xl border border-warning-line bg-warning-soft px-4 py-3 text-sm">
          Your listing is <strong>{business.status === 'PendingApproval' ? 'awaiting approval' : business.status.toLowerCase()}</strong>. It isn't visible to customers yet.
        </div>
      )}

      <section aria-labelledby="perf-h">
        <div className="mb-3 flex items-baseline justify-between gap-3">
          <h2 id="perf-h" className="text-lg font-bold tracking-tight">Last 30 days</h2>
          <span className="text-xs text-muted">Compared with the previous 30 days</span>
        </div>
        <div className="grid grid-cols-2 gap-4 md:grid-cols-3 2xl:grid-cols-6">
          {isLoading ? <KpiSkeletons count={6} /> : performance.map((k) => <KpiCard key={k.key} kpi={k} hint={k.key === 'conversion' ? 'last 90 days' : undefined} />)}
        </div>
      </section>

      <div className="mt-6 grid gap-6 xl:grid-cols-3">
        <div className="min-w-0 space-y-6 xl:col-span-2">
          <Panel title="Performance" subtitle="Weekly, last 12 weeks" noPad>
            <Tabs value={chart} onChange={(_, v) => setChart(v)} aria-label="Chart" variant="scrollable" scrollButtons={false}
              sx={{ px: 2, minHeight: 40, borderBottom: '1px solid var(--cb-line)', '& .MuiTab-root': { minHeight: 40, px: 1.5, fontSize: 13 } }}>
              <Tab value="views" label="Profile views" />
              <Tab value="pipeline" label="Leads & bookings" />
            </Tabs>
            <div className="p-5">
              {isLoading ? <Skeleton variant="rounded" height={260} /> : chart === 'views'
                ? <TrendChart data={data!.weekly} x="week" area lines={[{ key: 'views', label: 'Profile views' }]} />
                : <BarsChart data={data!.weekly} x="week" bars={[{ key: 'leads', label: 'Leads' }, { key: 'bookings', label: 'Bookings' }]} />}
            </div>
          </Panel>

          <div className="grid gap-6 lg:grid-cols-2">
            <Panel title="Recent leads" action={<Button size="small" component={Link} to="/owner/leads">View all</Button>} noPad>
              {isLoading ? <div className="p-5"><Skeleton height={200} /></div> : !data!.recentLeads.length ? <EmptyState title="No leads yet" message="Leads from your profile and search will appear here." /> : (
                <ul className="divide-y divide-line">
                  {data!.recentLeads.map((l) => (
                    <li key={l.id}>
                      <Link to={`/owner/leads?lead=${l.id}`} className="block px-5 py-3.5 transition-colors hover:bg-subtle">
                        <div className="flex items-center justify-between gap-2">
                          <span className="truncate text-sm font-semibold">{l.customerName}</span>
                          <span className="shrink-0 text-xs text-muted">{ago(l.createdOn)}</span>
                        </div>
                        <p className="truncate text-[13px] text-muted">{l.message}</p>
                        <div className="mt-1.5 flex gap-2"><StatusBadge type="EnquiryType" code={l.enquiryType} /><StatusBadge type="EnquiryStatus" code={l.status} /></div>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </Panel>
            <Panel title="Upcoming bookings" action={<Button size="small" component={Link} to="/owner/bookings">View all</Button>} noPad>
              {isLoading ? <div className="p-5"><Skeleton height={200} /></div> : !data!.upcomingBookings.length ? <EmptyState title="No upcoming bookings" message="New bookings will show up here instantly." /> : (
                <ul className="divide-y divide-line">
                  {data!.upcomingBookings.map((b) => (
                    <li key={b.id} className="flex items-center gap-3 px-5 py-3.5">
                      <div className="w-14 shrink-0 rounded-lg bg-navy py-1.5 text-center text-white">
                        <div className="text-[11px] font-semibold uppercase text-accent">{new Date(b.scheduledStart).toLocaleDateString('en-IN', { month: 'short' })}</div>
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

          <div className="grid gap-6 lg:grid-cols-2">
            <Panel title="Lead pipeline" subtitle="Last 90 days">
              {isLoading ? <Skeleton variant="rounded" height={220} /> : (
                <ul className="space-y-3">
                  {['New', 'Contacted', 'Quoted', 'Converted', 'Lost'].map((s) => {
                    const n = data!.leadStatuses.find((x) => x.name === s)?.count ?? 0;
                    return (
                      <li key={s} className="flex items-center gap-3 text-sm">
                        <span className="w-28"><StatusBadge type="EnquiryStatus" code={s} /></span>
                        <LinearProgress variant="determinate" value={leadTotal ? (n / leadTotal) * 100 : 0} aria-label={`${s}: ${n}`}
                          sx={{ flex: 1, height: 8, borderRadius: 4, bgcolor: 'var(--cb-subtle)', '& .MuiLinearProgress-bar': { bgcolor: 'var(--cb-accent)', borderRadius: 4 } }} />
                        <span className="w-10 text-right font-semibold tabular-nums">{number(n)}</span>
                      </li>
                    );
                  })}
                </ul>
              )}
            </Panel>
            <Panel title="Lead sources" subtitle="Last 90 days">
              {isLoading ? <Skeleton variant="rounded" height={220} /> : data!.leadSources.length === 0 ? <EmptyState title="No leads yet" /> : (
                <RankedBars rows={data!.leadSources.map((s) => ({ key: s.name, label: s.name, value: s.count }))} valueLabel="Leads by source" />
              )}
            </Panel>
          </div>
        </div>

        <aside className="min-w-0 space-y-6" aria-label="Your profile and plan">
          {overview.isLoading || !overview.data ? [0, 1, 2].map((i) => <Skeleton key={i} variant="rounded" height={320} />) : (
            <>
              <CompletionPanel data={overview.data.completion} />
              <PlanPanel plan={overview.data.plan} />
              <ActivityPanel items={overview.data.activities} />
            </>
          )}
        </aside>
      </div>

      {overview.data && <div className="mt-6"><MediaPanel data={overview.data} /></div>}
    </>
  );
}

function greeting() {
  const h = new Date().getHours();
  return h < 12 ? 'morning' : h < 17 ? 'afternoon' : 'evening';
}
