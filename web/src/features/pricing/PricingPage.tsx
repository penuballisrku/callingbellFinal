import { useState } from 'react';
import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Button, Skeleton, ToggleButton, ToggleButtonGroup } from '@mui/material';
import CheckRounded from '@mui/icons-material/CheckRounded';
import { api } from '@/lib/api';
import { moneyExact, number } from '@/lib/format';
import { useDocumentTitle } from '@/lib/hooks';
import type { Plan } from '@/lib/types';
import { ErrorState, Img } from '@/components/ui';

export function PlanGrid({ plans, cycle, currentCode, action }: { plans: Plan[]; cycle: 'monthly' | 'annual'; currentCode?: string | null; action?: (p: Plan) => React.ReactNode }) {
  return (
    <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5">
      {plans.map((p) => {
        const price = cycle === 'annual' ? p.annualPrice : p.monthlyPrice;
        const current = currentCode === p.code;
        return (
          <article key={p.code} className={`card relative flex flex-col p-5 ${p.isPopular ? 'border-2 !border-accent' : ''} ${current ? 'ring-2 ring-inverse' : ''}`}>
            {p.isPopular && <span className="absolute -top-3 left-5 rounded-full bg-accent px-2.5 py-0.5 text-xs font-bold text-on-accent">Most popular</span>}
            <div className="flex items-center gap-3">
              <Img src={p.imageUrl} alt="" className="h-10 w-10" fit="contain" rounded="rounded-lg" fallbackText={p.name} />
              <div><h3 className="text-lg font-bold">{p.name}</h3>{current && <span className="text-xs font-semibold text-success">Current plan</span>}</div>
            </div>
            <p className="mt-2 min-h-[40px] text-sm text-muted">{p.tagline}</p>
            <div className="mt-3">
              <span className="text-3xl font-bold tracking-tight">{price === 0 ? 'Free' : moneyExact(price)}</span>
              {price > 0 && <span className="text-sm text-muted"> /{cycle === 'annual' ? 'year' : 'month'}</span>}
              <div className="text-xs text-muted">{price > 0 ? '+ 18% GST' : 'Forever free'}{cycle === 'annual' && p.monthlyPrice > 0 && ` · save ${moneyExact(p.monthlyPrice * 12 - p.annualPrice)}`}</div>
            </div>
            <ul className="mt-4 flex-1 space-y-2 text-sm">
              {p.features.map((f) => <li key={f} className="flex gap-2"><CheckRounded sx={{ fontSize: 18, color: '#12B76A' }} /><span>{f}</span></li>)}
            </ul>
            <div className="mt-5">{action?.(p)}</div>
          </article>
        );
      })}
    </div>
  );
}

export default function PricingPage() {
  useDocumentTitle('Plans for businesses');
  const [cycle, setCycle] = useState<'monthly' | 'annual'>('monthly');
  const { data, isLoading, isError, refetch } = useQuery({ queryKey: ['plans'], queryFn: () => api.get<Plan[]>('/api/plans'), staleTime: 600_000 });

  return (
    <div className="container-page py-12">
      <div className="mx-auto max-w-2xl text-center">
        <p className="text-sm font-semibold uppercase tracking-[0.12em] text-accent-ink">For businesses</p>
        <h1 className="mt-2 text-3xl font-bold tracking-tight md:text-4xl">Get discovered. Get leads. Grow.</h1>
        <p className="mt-3 text-muted">List free in minutes. Upgrade for more leads, real-time availability, online bookings and top placement.</p>
        <ToggleButtonGroup exclusive size="small" value={cycle} onChange={(_, v) => v && setCycle(v)} sx={{ mt: 4 }} aria-label="Billing cycle">
          <ToggleButton value="monthly" sx={{ px: 3 }}>Monthly</ToggleButton>
          <ToggleButton value="annual" sx={{ px: 3 }}>Annual · 2 months free</ToggleButton>
        </ToggleButtonGroup>
      </div>
      <div className="mt-10">
        {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? (
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5">{[0, 1, 2, 3, 4].map((i) => <Skeleton key={i} variant="rounded" height={460} />)}</div>
        ) : (
          <PlanGrid plans={data!} cycle={cycle} action={(p) => (
            <Button fullWidth variant={p.isPopular ? 'contained' : 'outlined'} component={Link} to="/register?type=business">
              {p.monthlyPrice === 0 ? 'List for free' : `Start with ${p.name}`}
            </Button>
          )} />
        )}
      </div>
      {data && <p className="mt-8 text-center text-sm text-muted">Every plan includes a verified business profile and customer reviews. Up to {number(Math.max(...data.map((p) => p.leadCredits)))} lead credits a month.</p>}
    </div>
  );
}
