import { Link } from 'react-router';
import { Button, IconButton, LinearProgress, Tooltip } from '@mui/material';
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
import PhoneOutlined from '@mui/icons-material/PhoneOutlined';
import MailOutlineRounded from '@mui/icons-material/MailOutlineRounded';
import LanguageRounded from '@mui/icons-material/LanguageRounded';
import { ago, date, moneyExact, number, pluralize } from '@/lib/format';
import type { Activity, ActivePlan, OwnerOverview } from '@/lib/types';
import { MediaTile } from '@/components/media';
import { EmptyState, Img, Panel, StatusBadge } from '@/components/ui';

/** Circular progress ring (SVG) for profile completion. */
function Ring({ value, size = 72 }: { value: number; size?: number }) {
  const r = (size - 8) / 2;
  const c = 2 * Math.PI * r;
  const tone = value >= 80 ? 'var(--cb-success)' : 'var(--cb-accent)';
  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} role="img" aria-label={`Profile ${value}% complete`}>
      <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--cb-subtle)" strokeWidth={7} />
      <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke={tone} strokeWidth={7} strokeLinecap="round"
        strokeDasharray={c} strokeDashoffset={c * (1 - value / 100)} transform={`rotate(-90 ${size / 2} ${size / 2})`} />
      <text x="50%" y="50%" dominantBaseline="central" textAnchor="middle" fontSize={size / 4.2} fontWeight={700} fill="var(--cb-ink)">{value}%</text>
    </svg>
  );
}

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

export function BusinessOverviewCard({ data }: { data: OwnerOverview }) {
  const b = data.business;
  return (
    <section className="card mb-6 overflow-hidden" aria-label="Business overview">
      <div className="relative h-24 bg-subtle md:h-28">
        {b.coverImageUrl && <Img src={b.coverImageUrl} alt="" className="h-full w-full" rounded="rounded-none" />}
      </div>
      <div className="flex flex-col gap-4 px-5 pb-5 md:flex-row md:items-start md:px-6">
        <div className="-mt-10 shrink-0 self-start rounded-2xl border-4 border-surface bg-surface">
          {b.logoUrl ? <Img src={b.logoUrl} alt={`${b.name} logo`} className="h-20 w-20" rounded="rounded-xl" fit="contain" fallbackText={b.name} />
            : <div className="flex h-20 w-20 items-center justify-center rounded-xl bg-subtle text-faint"><StorefrontOutlined fontSize="large" /></div>}
        </div>
        <div className="min-w-0 flex-1 md:pt-3">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="truncate text-xl font-bold tracking-tight">{b.name}</h2>
            <StatusBadge type="BusinessStatus" code={b.status} />
            <StatusBadge type="VerificationStatus" code={b.verificationStatus} />
          </div>
          <p className="mt-0.5 text-sm text-muted">{[b.subCategoryName ?? b.categoryName, b.area, b.city].filter(Boolean).join(' · ')} · Listed {date(b.createdOn)}</p>
          {b.tagline && <p className="mt-1 text-sm text-ink-2">{b.tagline}</p>}
          <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-[13px] text-muted">
            {b.phoneNumber && <span className="inline-flex items-center gap-1"><PhoneOutlined sx={{ fontSize: 16 }} />{b.phoneNumber}</span>}
            {b.email && <span className="inline-flex items-center gap-1"><MailOutlineRounded sx={{ fontSize: 16 }} />{b.email}</span>}
            {b.website && <span className="inline-flex items-center gap-1"><LanguageRounded sx={{ fontSize: 16 }} />{b.website.replace(/^https?:\/\//, '')}</span>}
            <span>{pluralize(b.serviceCount, 'service')} · {pluralize(data.photoCount, 'photo')} · {pluralize(data.videos.length, 'video')}</span>
          </div>
        </div>
        <div className="flex shrink-0 items-center gap-4 md:pt-3">
          <Tooltip title={`${data.completion.completed} of ${data.completion.total} profile steps done`}><span><Ring value={data.completion.percent} /></span></Tooltip>
          <div className="flex flex-col gap-2">
            <Button size="small" variant="outlined" startIcon={<EditOutlined fontSize="small" />} component={Link} to="/business/profile">Edit profile</Button>
            {b.status === 'Active' && <Button size="small" endIcon={<OpenInNewRounded fontSize="small" />} component={Link} to={`/b/${b.slug}`} target="_blank">Public page</Button>}
          </div>
        </div>
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
    <Panel title="Your plan"><EmptyState icon={<WorkspacePremiumOutlined />} title="No active plan" action={<Button variant="contained" component={Link} to="/business/plan">Choose a plan</Button>} /></Panel>
  );
  const trial = plan.status === 'Trial';
  const used = Math.min(100, (plan.leadsThisMonth / Math.max(1, plan.leadCredits)) * 100);
  const unlimited = (n: number) => (n >= 999 ? 'Unlimited' : number(n));
  return (
    <Panel title="Active plan" action={<Button size="small" component={Link} to="/business/plan">Manage</Button>}>
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="text-2xl font-bold tracking-tight">{plan.name}</div>
          <div className="text-sm text-muted">{plan.billingCycle} · {plan.code === 'FREE' ? 'Free forever' : trial ? `Free trial until ${date(plan.endDate)}` : `Renews ${date(plan.endDate)} · ${moneyExact(plan.amount)} + GST`}</div>
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
      action={<Button size="small" component={Link} to="/business/media">{empty ? 'Upload' : 'Manage'}</Button>}>
      {empty ? (
        <EmptyState icon={<PhotoOutlined />} title="No photos or videos yet" message="Profiles with photos get far more enquiries."
          action={<Button variant="contained" component={Link} to="/business/media">Add photos & videos</Button>} />
      ) : (
        <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 lg:grid-cols-6">
          {data.videos.slice(0, 2).map((v) => (
            <Link key={v.id} to="/business/media" aria-label={`Video: ${v.title}`}><MediaTile src={v.thumbnailUrl} kind="video" durationSeconds={v.durationSeconds} title={v.title} /></Link>
          ))}
          {data.recentPhotos.slice(0, data.videos.length ? 10 : 12).map((p) => (
            <Link key={p.id} to="/business/media" aria-label={p.title ?? 'Photo'}><MediaTile src={p.thumbnailUrl ?? p.url} kind="photo" primary={p.isPrimary} title={p.title} /></Link>
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
