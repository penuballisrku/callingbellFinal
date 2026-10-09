import { Link } from 'react-router';
import { Button, IconButton, LinearProgress, Skeleton } from '@mui/material';
import CheckCircleRounded from '@mui/icons-material/CheckCircleRounded';
import RadioButtonUncheckedRounded from '@mui/icons-material/RadioButtonUncheckedRounded';
import ChevronRightRounded from '@mui/icons-material/ChevronRightRounded';
import CloseRounded from '@mui/icons-material/CloseRounded';
import CelebrationOutlined from '@mui/icons-material/CelebrationOutlined';
import InboxOutlined from '@mui/icons-material/InboxOutlined';
import EventNoteOutlined from '@mui/icons-material/EventNoteOutlined';
import StarOutlineRounded from '@mui/icons-material/StarOutlineRounded';
import PhotoOutlined from '@mui/icons-material/PhotoOutlined';
import VideocamOutlined from '@mui/icons-material/VideocamOutlined';
import WorkspacePremiumOutlined from '@mui/icons-material/WorkspacePremiumOutlined';
import PaymentsOutlined from '@mui/icons-material/PaymentsOutlined';
import StorefrontOutlined from '@mui/icons-material/StorefrontOutlined';
import EditOutlined from '@mui/icons-material/EditOutlined';
import OpenInNewRounded from '@mui/icons-material/OpenInNewRounded';
import { ago, date, moneyExact, number, pluralize } from '@/lib/format';
import type { Activity, ActivePlan, OwnerOverview } from '@/lib/types';
import { MediaTile } from '@/components/media';
import { EmptyState, Img, Panel, StatusBadge } from '@/components/ui';
import { useLookup } from '@/lib/hooks';

export function WelcomeBanner({ name, onDismiss }: { name: string; onDismiss: () => void }) {
  return (
    <div className="mb-6 flex items-start gap-4 rounded-xl border border-success/30 bg-success-soft p-4 md:p-5" role="status">
      <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-success text-white"><CelebrationOutlined /></span>
      <div className="min-w-0 flex-1">
        <div className="font-semibold text-ink">Welcome to Calling Bell! {name} has been created.</div>
        <p className="mt-0.5 text-sm text-ink-2">Our team will review your listing, usually within one working day. Meanwhile, complete your profile below so you're ready for your first customers.</p>
      </div>
      <IconButton size="small" onClick={onDismiss} aria-label="Dismiss"><CloseRounded fontSize="small" /></IconButton>
    </div>
  );
}

/** Lookup-backed status chip readable on the navy hero (StatusBadge assumes a light surface). */
function HeroChip({ type, code }: { type: string; code: string }) {
  const item = useLookup(type).find((l) => l.code === code);
  return (
    <span className="inline-flex items-center gap-1.5 rounded-full border border-white/15 bg-white/6 px-2.5 py-0.5 text-xs font-semibold text-on-navy" title={item?.description ?? undefined}>
      <span className="h-1.5 w-1.5 rounded-full" style={{ backgroundColor: item?.colorHex ?? 'var(--cb-on-navy-faint)' }} aria-hidden />
      {item?.name ?? code}
    </span>
  );
}

export interface AttentionItem {
  key: string; label: string; value?: number | string; hint: string; to: string; icon: React.ReactNode;
  /** Highlights the tile in brand orange (something is waiting). */
  urgent?: boolean;
}

/**
 * The top of the dashboard, in the brand's navy and orange: who you are, how customers see you, and the things waiting on you.
 * Navy surfaces stay dark in both themes, so text uses the on-navy tokens.
 */
export function BusinessHero({ data, greeting, attention }: { data?: OwnerOverview; greeting: string; attention: AttentionItem[] }) {
  const b = data?.business;
  return (
    <section className="relative mb-6 overflow-hidden rounded-2xl bg-navy text-white ring-1 ring-inset ring-white/8" aria-label="Business overview">
      {/* Signal rings from the Calling Bell mark, radiating behind the content. */}
      <svg aria-hidden className="pointer-events-none absolute -right-24 -top-24 h-[420px] w-[420px] text-accent" viewBox="0 0 420 420" fill="none">
        {[60, 110, 160, 210].map((r, i) => <circle key={r} cx="210" cy="210" r={r} stroke="currentColor" strokeWidth="1.5" opacity={0.28 - i * 0.06} />)}
      </svg>
      <div aria-hidden className="pointer-events-none absolute -left-32 -top-40 h-80 w-80 rounded-full bg-accent/15 blur-3xl" />

      <div className="relative p-5 md:p-7">
        <div className="flex flex-col gap-5 md:flex-row md:items-start">
          <div className="flex min-w-0 flex-1 items-start gap-4">
            <div className="shrink-0 rounded-2xl bg-white/6 p-1 ring-1 ring-white/10">
              {b?.logoUrl ? <Img src={b.logoUrl} alt={`${b.name} logo`} className="h-16 w-16 md:h-[72px] md:w-[72px]" rounded="rounded-xl" fit="contain" fallbackText={b.name} />
                : <div className="flex h-16 w-16 items-center justify-center rounded-xl bg-accent text-on-accent md:h-[72px] md:w-[72px]"><StorefrontOutlined fontSize="large" /></div>}
            </div>
            <div className="min-w-0">
              <p className="text-sm text-on-navy-muted">{greeting}</p>
              {b ? (
                <>
                  <h1 className="mt-0.5 truncate text-2xl font-bold tracking-tight md:text-3xl">{b.name}</h1>
                  <p className="mt-1 text-sm text-on-navy-muted">{[b.subCategoryName ?? b.categoryName, b.area, b.city].filter(Boolean).join(' · ')}</p>
                  <div className="mt-2.5 flex flex-wrap items-center gap-2">
                    <HeroChip type="BusinessStatus" code={b.status} />
                    <HeroChip type="VerificationStatus" code={b.verificationStatus} />
                    <span className="text-xs text-on-navy-faint">{pluralize(b.serviceCount, 'service')} · {pluralize(data!.photoCount, 'photo')} · {pluralize(data!.videos.length, 'video')} · listed {date(b.createdOn)}</span>
                  </div>
                </>
              ) : <Skeleton width={260} height={44} sx={{ bgcolor: 'rgba(255,255,255,.1)' }} />}
            </div>
          </div>
          {b && (
            <div className="flex shrink-0 gap-2">
              <Button size="small" variant="outlined" startIcon={<EditOutlined fontSize="small" />} component={Link} to="/owner/profile"
                sx={{ color: 'var(--cb-on-navy)', borderColor: 'rgba(255,255,255,.22)', bgcolor: 'transparent', boxShadow: 'none',
                  '&:hover': { borderColor: 'rgba(255,255,255,.4)', bgcolor: 'rgba(255,255,255,.06)' } }}>Edit profile</Button>
              {b.status === 'Active' && (
                <Button size="small" variant="contained" color="secondary" endIcon={<OpenInNewRounded fontSize="small" />} component={Link} to={`/business/${b.slug}`} target="_blank">Public page</Button>
              )}
            </div>
          )}
        </div>

        <h2 className="mt-7 text-xs font-semibold uppercase tracking-[0.12em] text-accent">Needs your attention</h2>
        <ul className="mt-3 grid grid-cols-2 gap-3 lg:grid-cols-4">
          {attention.map((a) => (
            <li key={a.key}>
              <Link to={a.to}
                className={`group flex h-full flex-col rounded-xl border p-3.5 outline-offset-2 transition-colors focus-visible:outline-2 focus-visible:outline-accent md:p-4 ${
                  a.urgent ? 'border-accent/50 bg-accent/10 hover:bg-accent/15' : 'border-white/10 bg-white/4 hover:bg-white/8'}`}>
                <span className="flex items-center justify-between">
                  <span className={`grid h-9 w-9 place-items-center rounded-lg [&_svg]:text-[20px] ${a.urgent ? 'bg-accent text-on-accent' : 'bg-white/8 text-accent'}`}>{a.icon}</span>
                  <ChevronRightRounded className="text-on-navy-faint transition-transform group-hover:translate-x-0.5 group-hover:text-white" />
                </span>
                <span className="tabular mt-3 text-2xl font-bold leading-none md:text-[28px]">
                  {a.value === undefined ? <Skeleton width={40} sx={{ bgcolor: 'rgba(255,255,255,.12)' }} /> : a.value}
                </span>
                <span className="mt-1.5 text-sm font-semibold text-white">{a.label}</span>
                <span className="mt-0.5 line-clamp-2 text-xs text-on-navy-muted">{a.hint}</span>
              </Link>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}

export function CompletionPanel({ data }: { data: OwnerOverview['completion'] }) {
  const pending = data.items.filter((i) => !i.done);
  return (
    <Panel title="Profile completion" subtitle={`${data.completed} of ${data.total} done · complete profiles get more leads`} noPad>
      <div className="px-5 pt-4">
        <LinearProgress variant="determinate" value={data.percent} aria-label={`${data.percent}% complete`}
          sx={{ height: 8, borderRadius: 4, '& .MuiLinearProgress-bar': { bgcolor: data.percent >= 80 ? 'var(--cb-success)' : 'var(--cb-accent)', borderRadius: 4 } }} />
      </div>
      <ul className="mt-2 divide-y divide-line">
        {[...pending, ...data.items.filter((i) => i.done)].slice(0, 7).map((i) => (
          <li key={i.key}>
            <Link to={i.linkUrl} className={`flex items-start gap-3 px-5 py-3 transition-colors hover:bg-subtle ${i.done ? 'opacity-70' : ''}`}>
              {i.done ? <CheckCircleRounded sx={{ fontSize: 20 }} className="mt-0.5 text-success" /> : <RadioButtonUncheckedRounded sx={{ fontSize: 20 }} className="mt-0.5 text-faint" />}
              <span className="min-w-0 flex-1">
                <span className={`block text-sm ${i.done ? 'text-muted line-through' : 'font-semibold'}`}>{i.label}</span>
                {!i.done && <span className="block text-xs text-muted">{i.hint}</span>}
              </span>
              {!i.done && <ChevronRightRounded className="text-faint" />}
            </Link>
          </li>
        ))}
      </ul>
    </Panel>
  );
}

export function PlanPanel({ plan }: { plan?: ActivePlan | null }) {
  if (!plan) return (
    <Panel title="Your plan"><EmptyState icon={<WorkspacePremiumOutlined />} title="No active plan" action={<Button variant="contained" component={Link} to="/owner/plan">Choose a plan</Button>} /></Panel>
  );
  const trial = plan.status === 'Trial';
  const used = Math.min(100, (plan.leadsThisMonth / Math.max(1, plan.leadCredits)) * 100);
  const unlimited = (n: number) => (n >= 999 ? 'Unlimited' : number(n));
  return (
    <Panel title="Active plan" action={<Button size="small" component={Link} to="/owner/plan">Manage</Button>}>
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="text-2xl font-bold tracking-tight">{plan.name}</div>
          <div className="text-sm text-muted">{plan.billingCycle} · {plan.code === 'FREE' ? 'Free forever' : trial ? `Free trial until ${date(plan.endDate)}` : `Renews ${date(plan.endDate)} · ${moneyExact(plan.amount, plan.currency)}${plan.currency === 'INR' ? ' + GST' : ''}`}</div>
        </div>
        <StatusBadge type="SubscriptionStatus" code={plan.status} />
      </div>
      {trial && plan.trialDaysLeft != null && (
        <div className="mt-4 rounded-lg border border-accent/40 bg-accent-soft px-3 py-2.5 text-sm text-accent-ink">
          <strong>{plan.trialDaysLeft} day{plan.trialDaysLeft === 1 ? '' : 's'} left</strong> in your trial. Our team will call you to activate {plan.name} before it ends.
        </div>
      )}
      <div className="mt-4">
        <div className="mb-1 flex justify-between text-sm"><span className="text-muted">Lead credits this month</span><span className="tabular font-semibold">{number(plan.leadsThisMonth)} / {number(plan.leadCredits)}</span></div>
        <LinearProgress variant="determinate" value={used} sx={{ height: 8, borderRadius: 4, '& .MuiLinearProgress-bar': { bgcolor: used >= 100 ? 'var(--cb-danger)' : 'var(--cb-accent)', borderRadius: 4 } }} />
      </div>
      <dl className="mt-4 grid grid-cols-2 gap-2 text-center">
        <div className="rounded-lg bg-subtle p-2"><dd className="font-bold">{unlimited(plan.maxServices)}</dd><dt className="text-[11px] text-muted">services</dt></div>
        <div className="rounded-lg bg-subtle p-2"><dd className="font-bold">{unlimited(plan.maxImages)}</dd><dt className="text-[11px] text-muted">photos</dt></div>
      </dl>
      <ul className="mt-4 space-y-1.5 text-sm">
        {plan.features.filter((f) => !/^Everything in/i.test(f)).slice(0, 5).map((f) => (
          <li key={f} className="flex gap-2"><CheckCircleRounded sx={{ fontSize: 16 }} className="mt-0.5 text-success" /><span>{f}</span></li>
        ))}
      </ul>
    </Panel>
  );
}

export function MediaPanel({ data }: { data: OwnerOverview }) {
  const empty = data.photoCount === 0 && data.videos.length === 0;
  return (
    <Panel title="Photos & videos" subtitle={`${pluralize(data.photoCount, 'photo')} · ${pluralize(data.videos.length, 'video')}`}
      action={<Button size="small" component={Link} to="/owner/media">{empty ? 'Upload' : 'Manage'}</Button>}>
      {empty ? (
        <EmptyState icon={<PhotoOutlined />} title="No photos or videos yet" message="Profiles with photos get far more enquiries."
          action={<Button variant="contained" component={Link} to="/owner/media">Add photos & videos</Button>} />
      ) : (
        <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 lg:grid-cols-6">
          {data.videos.slice(0, 2).map((v) => (
            <Link key={v.id} to="/owner/media" aria-label={`Video: ${v.title}`}><MediaTile src={v.thumbnailUrl} kind="video" durationSeconds={v.durationSeconds} title={v.title} /></Link>
          ))}
          {data.recentPhotos.slice(0, data.videos.length ? 10 : 12).map((p) => (
            <Link key={p.id} to="/owner/media" aria-label={p.title ?? 'Photo'}><MediaTile src={p.thumbnailUrl ?? p.url} kind="photo" primary={p.isPrimary} title={p.title} /></Link>
          ))}
        </div>
      )}
    </Panel>
  );
}

const activityIcon: Record<string, React.ReactNode> = {
  Lead: <InboxOutlined fontSize="small" />, Booking: <EventNoteOutlined fontSize="small" />, Review: <StarOutlineRounded fontSize="small" />,
  Photo: <PhotoOutlined fontSize="small" />, Video: <VideocamOutlined fontSize="small" />, Plan: <WorkspacePremiumOutlined fontSize="small" />,
  BusinessCreated: <StorefrontOutlined fontSize="small" />, ProfileUpdated: <EditOutlined fontSize="small" />, Payment: <PaymentsOutlined fontSize="small" />,
};

export function ActivityPanel({ items }: { items: Activity[] }) {
  return (
    <Panel title="Recent activity" noPad>
      {!items.length ? <EmptyState title="No activity yet" /> : (
        <ol className="relative px-5 py-4">
          {items.map((a, i) => (
            <li key={`${a.type}-${a.occurredOn}-${i}`} className="relative flex gap-3 pb-4 last:pb-0">
              {i < items.length - 1 && <span className="absolute left-[15px] top-8 h-[calc(100%-24px)] w-px bg-line" aria-hidden />}
              <span className="z-[1] flex h-8 w-8 shrink-0 items-center justify-center rounded-full border border-line bg-surface text-muted">{activityIcon[a.type] ?? <StorefrontOutlined fontSize="small" />}</span>
              <div className="min-w-0 flex-1 pt-0.5">
                <div className="flex items-baseline justify-between gap-2">
                  {a.linkUrl ? <Link to={a.linkUrl} className="truncate text-sm font-semibold hover:underline">{a.title}</Link> : <span className="truncate text-sm font-semibold">{a.title}</span>}
                  <time className="shrink-0 text-[11px] text-muted" dateTime={a.occurredOn}>{ago(a.occurredOn)}</time>
                </div>
                <p className="truncate text-[13px] text-muted">{a.description}</p>
              </div>
            </li>
          ))}
        </ol>
      )}
    </Panel>
  );
}
