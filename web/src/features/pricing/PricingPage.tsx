import { useState } from 'react';
import { Link } from 'react-router';
import { Button, Skeleton, ToggleButton, ToggleButtonGroup } from '@mui/material';
import CheckRounded from '@mui/icons-material/CheckRounded';
import RemoveRounded from '@mui/icons-material/RemoveRounded';
import { moneyExact } from '@/lib/format';
import { useDocumentTitle, usePlans } from '@/lib/hooks';
import { taxNote } from '@/lib/payments';
import type { Plan } from '@/lib/types';
import { PolicyList } from '@/lib/legal';
import { ErrorState, Img } from '@/components/ui';

/** Plan cards side by side: a few plans are centred at a comfortable width; up to five fill the row. */
export const planGridClass = (count: number) =>
  count <= 1 ? 'mx-auto grid max-w-md gap-4' : count === 2 ? 'mx-auto grid max-w-4xl gap-4 md:grid-cols-2'
    : count === 3 ? 'mx-auto grid max-w-6xl gap-4 md:grid-cols-3' : 'grid gap-4 md:grid-cols-2 xl:grid-cols-5';

export function PlanGrid({ plans, cycle, currentCode, action }: { plans: Plan[]; cycle: 'monthly' | 'annual'; currentCode?: string | null; action?: (p: Plan) => React.ReactNode }) {
  return (
    <div className={planGridClass(plans.length)}>
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
              <span className="text-3xl font-bold tracking-tight">{price === 0 ? 'Free' : moneyExact(price, p)}</span>
              {price > 0 && <span className="text-sm text-muted"> /{cycle === 'annual' ? 'year' : 'month'}</span>}
              <div className="min-h-4 text-xs text-muted">{[price > 0 ? taxNote(p) : 'Forever free', cycle === 'annual' && p.monthlyPrice > 0 && p.monthlyPrice * 12 > p.annualPrice ? `save ${moneyExact(p.monthlyPrice * 12 - p.annualPrice, p)}` : ''].filter(Boolean).join(' · ')}</div>
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

/**
 * Plans side by side, feature by feature (dbo.PlanFeatureValues); the Price row comes from the plans in the visitor's currency.
 * "Yes" and "No" read as a tick and a dash; other values ("Basic", "Limited", "Enhanced") are shown as written.
 */
export function PlanComparisonTable({ plans, cycle }: { plans: Plan[]; cycle: 'monthly' | 'annual' }) {
  const rows = new Map<string, { label: string; sortOrder: number }>();
  plans.forEach((p) => p.comparison?.forEach((v) => { if (!rows.has(v.key)) rows.set(v.key, { label: v.label, sortOrder: v.sortOrder }); }));
  if (!rows.size) return null;
  const ordered = [...rows.entries()].sort((a, b) => a[1].sortOrder - b[1].sortOrder);
  const cell = (value?: string) => {
    if (!value) return <span className="text-faint">-</span>;
    if (/^yes$/i.test(value)) return <span className="inline-flex items-center justify-end gap-1 font-medium text-ink"><CheckRounded sx={{ fontSize: 18, color: 'var(--cb-success)' }} aria-hidden />Yes</span>;
    if (/^no$/i.test(value)) return <span className="inline-flex items-center justify-end gap-1 text-muted"><RemoveRounded sx={{ fontSize: 16 }} aria-hidden />No</span>;
    return <span className="text-ink">{value}</span>;
  };
  return (
    <div className="card overflow-hidden">
      <table className="w-full text-sm">
        <caption className="sr-only">Plan comparison</caption>
        <thead>
          <tr className="border-b border-line bg-subtle/60">
            <th scope="col" className="px-4 py-3 text-left font-semibold sm:px-5">Feature</th>
            {plans.map((p) => (
              <th key={p.code} scope="col" className="px-3 py-3 text-right font-semibold sm:px-5">
                {p.name}
                {p.isPopular && <span className="ml-1.5 hidden rounded-full bg-accent px-2 py-0.5 align-middle text-[10px] font-bold text-on-accent sm:inline">Popular</span>}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          <tr>
            <th scope="row" className="px-4 py-3 text-left font-normal text-ink-2 sm:px-5">Price</th>
            {plans.map((p) => {
              const price = cycle === 'annual' ? p.annualPrice : p.monthlyPrice;
              return <td key={p.code} className="whitespace-nowrap px-3 py-3 text-right font-semibold tabular-nums sm:px-5">{moneyExact(price, p)}/{cycle === 'annual' ? 'yr' : 'mo'}</td>;
            })}
          </tr>
          {ordered.map(([key, row]) => (
            <tr key={key}>
              <th scope="row" className="px-4 py-3 text-left font-normal text-ink-2 sm:px-5">{row.label}</th>
              {plans.map((p) => <td key={p.code} className="px-3 py-3 text-right sm:px-5">{cell(p.comparison?.find((v) => v.key === key)?.value)}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default function PricingPage() {
  useDocumentTitle('Plans for businesses');
  const [cycle, setCycle] = useState<'monthly' | 'annual'>('monthly');
  // Priced for the visitor's country: its currency at a fair local price.
  // isPending (not isLoading): the plans wait for the visitor's country, and until then there is no data to show.
  const { data, isPending, isError, refetch } = usePlans();

  return (
    <div className="container-page py-12">
      <div className="mx-auto max-w-2xl text-center">
        <p className="text-sm font-semibold uppercase tracking-[0.12em] text-accent-ink">For businesses</p>
        <h1 className="mt-2 text-3xl font-bold tracking-tight md:text-4xl">Get discovered. Get leads. Grow.</h1>
        <p className="mt-3 text-muted">List your business in minutes. Choose a plan for more leads, real-time availability, online bookings and top placement.</p>
        <ToggleButtonGroup exclusive size="small" value={cycle} onChange={(_, v) => v && setCycle(v)} sx={{ mt: 4 }} aria-label="Billing cycle">
          <ToggleButton value="monthly" sx={{ px: 3 }}>Monthly</ToggleButton>
          <ToggleButton value="annual" sx={{ px: 3 }}>Annual · 2 months free</ToggleButton>
        </ToggleButtonGroup>
      </div>
      <div className="mt-10">
        {isError ? <ErrorState onRetry={() => refetch()} /> : isPending ? (
          <div className={planGridClass(2)}>{[0, 1].map((i) => <Skeleton key={i} variant="rounded" height={460} />)}</div>
        ) : (
          <PlanGrid plans={data!} cycle={cycle} action={(p) => (
            <Button fullWidth variant={p.isPopular ? 'contained' : 'outlined'} component={Link} to="/register?type=business">
              {p.monthlyPrice === 0 ? 'List for free' : `Start with ${p.name}`}
            </Button>
          )} />
        )}
      </div>
      {data && data.some((p) => p.comparison?.length) && (
        <section className="mx-auto mt-12 max-w-4xl" aria-labelledby="compare-h">
          <h2 id="compare-h" className="mb-4 text-xl font-bold tracking-tight">Compare plans</h2>
          <PlanComparisonTable plans={data} cycle={cycle} />
        </section>
      )}
      {data && <p className="mt-8 text-center text-sm text-muted">Every plan includes a verified business profile and customer reviews.</p>}
      {/* What payment gateways and the e-commerce rules expect next to prices: renewal, cancellation and refund terms, one click away. */}
      {data && (
        <p className="mx-auto mt-3 max-w-2xl text-center text-xs leading-5 text-muted">
          Prices are in your local currency{data.some((p) => p.taxRate > 0) ? ', with tax added at checkout' : ''}. Paid plans don&apos;t renew
          automatically. Payments are processed securely by Razorpay. See <PolicyList policies={['terms', 'cancellation', 'refund']} />.
        </p>
      )}
    </div>
  );
}
