import { useState, type ComponentType, type ReactNode } from 'react';
import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Button, Skeleton } from '@mui/material';
import type { SvgIconProps } from '@mui/material';
import ArrowForwardRounded from '@mui/icons-material/ArrowForwardRounded';
import PlayArrowRounded from '@mui/icons-material/PlayArrowRounded';
import FormatQuoteRounded from '@mui/icons-material/FormatQuoteRounded';
import ExpandMoreRounded from '@mui/icons-material/ExpandMoreRounded';
import SearchRounded from '@mui/icons-material/SearchRounded';
import EventAvailableOutlined from '@mui/icons-material/EventAvailableOutlined';
import VerifiedOutlined from '@mui/icons-material/VerifiedOutlined';
import TrendingUpRounded from '@mui/icons-material/TrendingUpRounded';
import StorefrontOutlined from '@mui/icons-material/StorefrontOutlined';
import InboxOutlined from '@mui/icons-material/InboxOutlined';
import StarOutlineRounded from '@mui/icons-material/StarOutlineRounded';
import BoltRounded from '@mui/icons-material/BoltRounded';
import CalendarMonthOutlined from '@mui/icons-material/CalendarMonthOutlined';
import ChatBubbleOutlineRounded from '@mui/icons-material/ChatBubbleOutlineRounded';
import VideocamOutlined from '@mui/icons-material/VideocamOutlined';
import CampaignOutlined from '@mui/icons-material/CampaignOutlined';
import InsightsRounded from '@mui/icons-material/InsightsRounded';
import GroupsOutlined from '@mui/icons-material/GroupsOutlined';
import AccountBalanceWalletOutlined from '@mui/icons-material/AccountBalanceWalletOutlined';
import NotificationsActiveOutlined from '@mui/icons-material/NotificationsActiveOutlined';
import ShieldOutlined from '@mui/icons-material/ShieldOutlined';
import SpaceDashboardOutlined from '@mui/icons-material/SpaceDashboardOutlined';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import PersonAddAltOutlined from '@mui/icons-material/PersonAddAltOutlined';
import CheckCircleRounded from '@mui/icons-material/CheckCircleRounded';
import AutorenewRounded from '@mui/icons-material/AutorenewRounded';
import ScheduleRounded from '@mui/icons-material/ScheduleRounded';
import CircleOutlined from '@mui/icons-material/CircleOutlined';
import { api } from '@/lib/api';
import { compactNumber, moneyExact, number } from '@/lib/format';
import { useDocumentTitle } from '@/lib/hooks';
import { homeFor, isOwner, useAuth } from '@/stores/auth';
import type { ContentBlock, MarketingPage, Plan } from '@/lib/types';
import { ErrorState, Img, SectionHeader, VerifiedMark } from '@/components/ui';

/** Icon keys stored in MarketingContent.IconKey. Icons are UI chrome; the words and media come from the database. */
const icons: Record<string, ComponentType<SvgIconProps>> = {
  search: SearchRounded, event: EventAvailableOutlined, verified: VerifiedOutlined, trending: TrendingUpRounded, storefront: StorefrontOutlined,
  inbox: InboxOutlined, star: StarOutlineRounded, bolt: BoltRounded, calendar: CalendarMonthOutlined, chat: ChatBubbleOutlineRounded,
  videocam: VideocamOutlined, campaign: CampaignOutlined, analytics: InsightsRounded, groups: GroupsOutlined, wallet: AccountBalanceWalletOutlined,
  notifications: NotificationsActiveOutlined, shield: ShieldOutlined, dashboard: SpaceDashboardOutlined, place: PlaceOutlined,
  person_add: PersonAddAltOutlined, done: CheckCircleRounded, progress: AutorenewRounded, planned: ScheduleRounded,
};
function BlockIcon({ name, size = 22 }: { name?: string | null; size?: number }) {
  const Icon = (name && icons[name]) || CircleOutlined;
  return <Icon sx={{ fontSize: size }} aria-hidden />;
}

const paragraphs = (body?: string | null) => (body ?? '').split('\n').map((p) => p.trim()).filter(Boolean);

/** Desktop image with a lighter mobile source; falls back gracefully (never a broken image). */
function Photo({ block, className, aspect, eager, rounded = 'rounded-none' }: { block: ContentBlock; className?: string; aspect?: string; eager?: boolean; rounded?: string }) {
  return (
    <picture className="contents">
      {block.mobileImageUrl && <source media="(max-width: 767px)" srcSet={block.mobileImageUrl} />}
      <Img src={block.desktopImageUrl ?? block.imageUrl} alt={block.altText ?? block.title} className={className} aspect={aspect} rounded={rounded}
        eager={eager} fallbackText={block.title} />
    </picture>
  );
}

function Credit({ block, className = 'text-muted' }: { block: ContentBlock; className?: string }) {
  if (!block.mediaCredit) return null;
  return block.mediaCreditUrl
    ? <a href={block.mediaCreditUrl} target="_blank" rel="noopener noreferrer" className={`text-[11px] hover:underline ${className}`}>{block.mediaCredit}</a>
    : <span className={`text-[11px] ${className}`}>{block.mediaCredit}</span>;
}

function Eyebrow({ children, onDark }: { children: ReactNode; onDark?: boolean }) {
  return <p className={`text-xs font-semibold uppercase tracking-[0.12em] ${onDark ? 'text-accent' : 'text-accent-ink'}`}>{children}</p>;
}

export default function ListBusinessPage() {
  useDocumentTitle('List your business');
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['content', 'ListYourBusiness'],
    queryFn: () => api.get<MarketingPage>('/api/content/pages/ListYourBusiness'),
    staleTime: 300_000,
  });
  const plans = useQuery({ queryKey: ['plans'], queryFn: () => api.get<Plan[]>('/api/plans'), staleTime: 600_000 });

  if (isError) return <div className="container-page py-16"><ErrorState onRetry={() => refetch()} /></div>;
  if (isLoading || !data) return <PageSkeleton />;

  const section = (key: string) => data.blocks.filter((b) => b.section === key);
  const first = (key: string) => section(key)[0];
  const hero = first('Hero');
  const overview = first('Overview');
  const cta = first('Cta');
  const s = data.stats;

  return (
    <>
      {hero && <Hero block={hero} stats={s} />}

      {/* Live proof points */}
      <section aria-label="Calling Bell in numbers" className="border-b border-line bg-surface">
        <dl className="container-page grid grid-cols-2 divide-line py-6 sm:grid-cols-3 lg:grid-cols-6 lg:divide-x">
          {[
            ['Active businesses', number(s.activeBusinesses)],
            ['Cities', number(s.cities)],
            ['Service categories', number(s.categories)],
            ['Leads in the last 30 days', compactNumber(s.leadsLast30Days)],
            ['Bookings completed', compactNumber(s.bookingsCompleted)],
            ['Average customer rating', `${s.averageRating.toFixed(1)} / 5`],
          ].map(([label, value]) => (
            <div key={label} className="px-2 py-3 text-center lg:px-4">
              <dd className="tabular text-2xl font-bold tracking-tight md:text-[28px]">{value}</dd>
              <dt className="mt-1 text-xs text-muted md:text-[13px]">{label}</dt>
            </div>
          ))}
        </dl>
      </section>

      <div className="container-page space-y-20 py-16 md:space-y-24 md:py-20">
        {overview && <Overview block={overview} objectives={section('Objective')} />}
        <Offerings items={section('Offering')} />
      </div>

      <Highlights items={section('Highlight')} />

      <div className="container-page space-y-20 py-16 md:space-y-24 md:py-20">
        <Gallery items={section('Gallery')} />
        <Videos items={section('Video')} />
        <Steps items={section('Step')} />
        <Testimonials items={section('Testimonial')} />
        <PlansTeaser plans={plans.data} loading={plans.isLoading} />
        <Roadmap items={section('Roadmap')} />
        <Faqs items={section('Faq')} />
        {cta && <ClosingCta block={cta} />}
      </div>
    </>
  );
}

/* ---------------------------------------------------------------------------------------------- */

/** Owners who are already signed in go straight to their dashboard instead of the sign-up form. */
function PrimaryCta({ block, size = 'large' }: { block: ContentBlock; size?: 'medium' | 'large' }) {
  const user = useAuth((st) => st.user);
  const owner = !!user && isOwner(user);
  return (
    <Button component={Link} to={owner ? homeFor(user) : (block.linkUrl ?? '/register?type=business')} variant="contained" color="secondary" size={size}
      endIcon={<ArrowForwardRounded />}>
      {owner ? 'Go to your business dashboard' : (block.ctaText ?? 'List your business free')}
    </Button>
  );
}

function Hero({ block, stats }: { block: ContentBlock; stats: MarketingPage['stats'] }) {
  return (
    <section className="bg-navy text-white">
      <div className="container-page grid gap-10 py-12 md:py-16 lg:grid-cols-[1.1fr_1fr] lg:items-center lg:gap-14">
        <div>
          {block.eyebrow && <Eyebrow onDark>{block.eyebrow}</Eyebrow>}
          <h1 className="mt-3 text-3xl font-bold leading-[1.12] tracking-tight sm:text-4xl lg:text-[52px]">{block.title}</h1>
          {block.subtitle && <p className="mt-5 max-w-xl text-base leading-7 text-on-navy-muted md:text-lg md:leading-8">{block.subtitle}</p>}
          <div className="mt-8 flex flex-col gap-3 sm:flex-row">
            <PrimaryCta block={block} />
            <Button component={Link} to="/pricing" size="large" variant="outlined"
              sx={{ color: '#fff', borderColor: 'rgba(255,255,255,.25)', bgcolor: 'transparent', boxShadow: 'none', '&:hover': { borderColor: 'rgba(255,255,255,.5)', bgcolor: 'rgba(255,255,255,.06)' } }}>
              Compare plans
            </Button>
          </div>
          <ul className="mt-8 flex flex-wrap gap-x-6 gap-y-2 text-sm text-on-navy-muted">
            <li className="inline-flex items-center gap-1.5"><CheckCircleRounded sx={{ fontSize: 18 }} className="text-accent" />Free plan, no time limit</li>
            <li className="inline-flex items-center gap-1.5"><CheckCircleRounded sx={{ fontSize: 18 }} className="text-accent" />{number(stats.verifiedBusinesses)} verified businesses</li>
            <li className="inline-flex items-center gap-1.5"><CheckCircleRounded sx={{ fontSize: 18 }} className="text-accent" />Live in {number(stats.cities)} cities</li>
          </ul>
        </div>

        <figure className="relative">
          <div className="overflow-hidden rounded-2xl border border-white/10">
            <Photo block={block} aspect="3/2" className="w-full" eager />
          </div>
          <div className="mt-3 flex items-center gap-3 rounded-xl border border-line bg-surface p-3 text-ink shadow-lg sm:absolute sm:-bottom-5 sm:right-6 sm:mt-0 sm:w-72">
            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-success-soft text-success"><TrendingUpRounded /></span>
            <div className="min-w-0">
              <div className="tabular text-lg font-bold leading-tight">{number(stats.leadsLast30Days)} leads</div>
              <div className="text-xs text-muted">sent to local businesses in the last 30 days</div>
            </div>
          </div>
          <figcaption className="mt-2"><Credit block={block} className="text-on-navy-faint" /></figcaption>
        </figure>
      </div>
    </section>
  );
}

function Overview({ block, objectives }: { block: ContentBlock; objectives: ContentBlock[] }) {
  return (
    <section aria-labelledby="overview-h">
      <div className="grid gap-10 lg:grid-cols-2 lg:items-center lg:gap-16">
        <figure className="order-last lg:order-first">
          <div className="overflow-hidden rounded-2xl border border-line">
            <Photo block={block} aspect="16/10" className="w-full" />
          </div>
          <figcaption className="mt-2"><Credit block={block} /></figcaption>
        </figure>
        <div>
          {block.eyebrow && <Eyebrow>{block.eyebrow}</Eyebrow>}
          <h2 id="overview-h" className="mt-2 text-2xl font-bold tracking-[-0.02em] md:text-[34px] md:leading-tight">{block.title}</h2>
          {block.subtitle && <p className="mt-3 text-lg font-semibold text-ink-2">{block.subtitle}</p>}
          <div className="mt-4 space-y-4 text-[15px] leading-7 text-ink-2">
            {paragraphs(block.body).map((p) => <p key={p.slice(0, 32)}>{p}</p>)}
          </div>
        </div>
      </div>

      {objectives.length > 0 && (
        <div className="mt-14">
          <h3 className="eyebrow mb-4">What we help you achieve</h3>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {objectives.map((o) => (
              <article key={o.code} className="card p-5">
                <span className="flex h-10 w-10 items-center justify-center rounded-lg bg-accent-soft text-accent-ink"><BlockIcon name={o.iconKey} /></span>
                <h4 className="mt-4 font-semibold">{o.title}</h4>
                {o.subtitle && <p className="mt-1.5 text-sm leading-6 text-muted">{o.subtitle}</p>}
              </article>
            ))}
          </div>
        </div>
      )}
    </section>
  );
}

function Offerings({ items }: { items: ContentBlock[] }) {
  if (!items.length) return null;
  return (
    <section aria-labelledby="offer-h">
      <SectionHeader title="Services & key offerings" subtitle="Everything you need to win, serve and keep local customers, in one platform."
        action={<Link to="/pricing" className="hidden items-center gap-1 text-sm font-semibold sm:inline-flex">See what each plan includes <ArrowForwardRounded sx={{ fontSize: 18 }} /></Link>} />
      <h2 id="offer-h" className="sr-only">Services and key offerings</h2>
      <div className="card grid overflow-hidden md:grid-cols-2">
        {items.map((o, i) => (
          <article key={o.code}
            className={`flex gap-4 border-line p-5 md:p-6 ${i > 0 ? 'border-t' : ''} ${i === 1 ? 'md:border-t-0' : ''} ${i % 2 === 1 ? 'md:border-l' : ''}`}>
            <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-line bg-subtle text-ink"><BlockIcon name={o.iconKey} /></span>
            <div className="min-w-0">
              <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                <h3 className="font-semibold">{o.title}</h3>
                {o.eyebrow && <span className="rounded-md bg-subtle px-1.5 py-0.5 text-[11px] font-semibold text-muted">{o.eyebrow}</span>}
              </div>
              {o.subtitle && <p className="mt-1 text-sm leading-6 text-muted">{o.subtitle}</p>}
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}

function Highlights({ items }: { items: ContentBlock[] }) {
  if (!items.length) return null;
  const [lead, ...rest] = items;
  return (
    <section aria-labelledby="usp-h" className="bg-navy text-white">
      <div className="container-page py-16 md:py-20">
        <div className="max-w-2xl">
          <Eyebrow onDark>Why Calling Bell</Eyebrow>
          <h2 id="usp-h" className="mt-2 text-2xl font-bold tracking-[-0.02em] md:text-[34px] md:leading-tight">Why businesses choose Calling Bell</h2>
        </div>
        <div className="mt-10 grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          {lead && (
            <article className="rounded-2xl border border-accent/40 bg-accent/10 p-6">
              <span className="flex h-10 w-10 items-center justify-center rounded-lg bg-accent text-on-accent"><BlockIcon name={lead.iconKey} /></span>
              {lead.eyebrow && <p className="mt-4 text-xs font-semibold uppercase tracking-[0.12em] text-accent">{lead.eyebrow}</p>}
              <h3 className="mt-1 text-lg font-bold">{lead.title}</h3>
              {lead.subtitle && <p className="mt-1.5 text-sm leading-6 text-on-navy-muted">{lead.subtitle}</p>}
            </article>
          )}
          {rest.map((h) => (
            <article key={h.code} className="rounded-2xl border border-navy-line bg-white/3 p-6">
              <span className="text-accent"><BlockIcon name={h.iconKey} size={24} /></span>
              <h3 className="mt-3 font-semibold">{h.title}</h3>
              {h.subtitle && <p className="mt-1.5 text-sm leading-6 text-on-navy-muted">{h.subtitle}</p>}
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}

function Gallery({ items }: { items: ContentBlock[] }) {
  if (!items.length) return null;
  return (
    <section aria-labelledby="gallery-h">
      <SectionHeader title="Built for every kind of local business" subtitle="From neighbourhood stores to clinics and home-service experts across India." />
      <h2 id="gallery-h" className="sr-only">Businesses on Calling Bell</h2>
      <div className="grid grid-cols-2 gap-3 md:gap-4 lg:grid-cols-3">
        {items.map((g, i) => (
          <figure key={g.code} className={`group relative overflow-hidden rounded-xl bg-subtle ${i === 0 ? 'lg:col-span-2 lg:row-span-2' : ''}`}>
            <Link to={g.linkUrl ?? '/categories'} className="block h-full" aria-label={`${g.title}: browse businesses`}>
              <Photo block={g} aspect={i === 0 ? undefined : '4/3'} className={`w-full transition-transform duration-500 group-hover:scale-[1.03] ${i === 0 ? 'aspect-[4/3] lg:absolute lg:inset-0 lg:aspect-auto lg:h-full' : ''}`} />
              <div className="pointer-events-none absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/75 via-black/30 to-transparent p-3 pt-12 text-white md:p-4 md:pt-16">
                {g.eyebrow && <div className="text-[11px] font-semibold uppercase tracking-[0.1em] text-accent">{g.eyebrow}</div>}
                <div className="text-sm font-semibold md:text-base">{g.title}{g.subtitle && <span className="font-normal text-white/75"> · {g.subtitle}</span>}</div>
              </div>
            </Link>
            {g.mediaCredit && (
              <figcaption className="absolute right-2 top-2 rounded bg-black/45 px-1.5 py-0.5 opacity-0 transition-opacity group-focus-within:opacity-100 group-hover:opacity-100">
                <Credit block={g} className="text-white" />
              </figcaption>
            )}
          </figure>
        ))}
      </div>
    </section>
  );
}

/** Poster first; the video loads only when the visitor presses play. Falls back to the poster if playback fails. */
function VideoCard({ block, featured }: { block: ContentBlock; featured?: boolean }) {
  const [playing, setPlaying] = useState(false);
  const [failed, setFailed] = useState(false);
  const canPlay = !!block.videoUrl && !failed;
  return (
    <article className="flex min-w-0 flex-col">
      <div className="relative overflow-hidden rounded-xl border border-line bg-navy">
        {playing && canPlay ? (
          <video src={block.videoUrl!} poster={block.imageUrl ?? undefined} controls autoPlay playsInline className="aspect-video w-full bg-black"
            onError={() => { setFailed(true); setPlaying(false); }} aria-label={block.altText ?? block.title} />
        ) : (
          <button type="button" disabled={!canPlay} onClick={() => setPlaying(true)} className="group relative block w-full text-left disabled:cursor-default"
            aria-label={canPlay ? `Play video: ${block.title}` : block.title}>
            <Photo block={block} aspect="16/9" className="w-full" />
            {canPlay ? (
              <span className="absolute inset-0 flex items-center justify-center bg-black/15 transition-colors group-hover:bg-black/25">
                <span className={`flex items-center justify-center rounded-full bg-white/95 text-navy shadow-lg transition-transform group-hover:scale-105 ${featured ? 'h-16 w-16' : 'h-12 w-12'}`}>
                  <PlayArrowRounded sx={{ fontSize: featured ? 36 : 28 }} />
                </span>
              </span>
            ) : failed && <span className="absolute bottom-2 left-2 rounded bg-black/60 px-2 py-1 text-xs text-white">Video unavailable right now</span>}
          </button>
        )}
      </div>
      <div className="mt-3 flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className={`font-semibold ${featured ? 'text-lg' : 'text-[15px]'}`}>{block.title}</h3>
          {block.subtitle && <p className="mt-1 text-sm leading-6 text-muted">{block.subtitle}</p>}
        </div>
        <span className="shrink-0 pt-1"><Credit block={block} className="text-faint" /></span>
      </div>
    </article>
  );
}

function Videos({ items }: { items: ContentBlock[] }) {
  if (!items.length) return null;
  const [main, ...side] = items;
  return (
    <section aria-labelledby="video-h">
      <SectionHeader title={main!.eyebrow ?? 'See Calling Bell in action'} subtitle="How business owners use Calling Bell day to day." />
      <h2 id="video-h" className="sr-only">Videos</h2>
      <div className="grid gap-6 lg:grid-cols-[1.6fr_1fr]">
        <VideoCard block={main!} featured />
        {side.length > 0 && <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-1">{side.map((v) => <VideoCard key={v.code} block={v} />)}</div>}
      </div>
    </section>
  );
}

function Steps({ items }: { items: ContentBlock[] }) {
  if (!items.length) return null;
  return (
    <section aria-labelledby="steps-h">
      <SectionHeader title="How it works" subtitle="Go from sign-up to your first customer enquiry in four simple steps." />
      <h2 id="steps-h" className="sr-only">How it works</h2>
      <ol className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {items.map((st, i) => (
          <li key={st.code} className="card relative p-5">
            <div className="flex items-center justify-between">
              <span className="flex h-10 w-10 items-center justify-center rounded-full bg-inverse text-sm font-bold text-on-inverse">{i + 1}</span>
              <span className="text-faint"><BlockIcon name={st.iconKey} /></span>
            </div>
            <h3 className="mt-4 font-semibold">{st.title}</h3>
            {st.subtitle && <p className="mt-1.5 text-sm leading-6 text-muted">{st.subtitle}</p>}
          </li>
        ))}
      </ol>
    </section>
  );
}

function Testimonials({ items }: { items: ContentBlock[] }) {
  if (!items.length) return null;
  return (
    <section aria-labelledby="tst-h">
      <SectionHeader title="Trusted by local business owners" subtitle="Real businesses on Calling Bell. Figures update live from their dashboards." />
      <h2 id="tst-h" className="sr-only">Testimonials</h2>
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {items.map((t) => {
          const b = t.business;
          return (
            <figure key={t.code} className="card flex flex-col p-5">
              <FormatQuoteRounded className="text-accent" />
              <blockquote className="mt-2 flex-1 text-[15px] leading-7 text-ink-2">{t.body}</blockquote>
              <figcaption className="mt-5 border-t border-line pt-4">
                <div className="font-semibold">{t.title}</div>
                {t.subtitle && <div className="text-xs text-muted">{t.subtitle}{b ? `, ${b.name}` : ''}</div>}
                {b && (
                  <>
                    <Link to={`/b/${b.slug}`} className="mt-3 flex items-center gap-2.5 rounded-lg hover:underline">
                      <Img src={b.logoUrl} alt="" className="h-9 w-9 shrink-0" fallbackText={b.name} />
                      <span className="min-w-0 text-sm">
                        <span className="flex items-center gap-1 font-medium"><span className="truncate">{b.name}</span>{b.isVerified && <VerifiedMark />}</span>
                        <span className="block truncate text-xs text-muted">{b.categoryName} · {b.city}</span>
                      </span>
                    </Link>
                    <dl className="mt-3 grid grid-cols-3 gap-2 rounded-lg bg-subtle p-2.5 text-center">
                      <div><dd className="tabular text-sm font-bold">{b.averageRating.toFixed(1)}★</dd><dt className="text-[11px] text-muted">{number(b.reviewCount)} reviews</dt></div>
                      <div><dd className="tabular text-sm font-bold">{number(b.leads)}</dd><dt className="text-[11px] text-muted">leads</dt></div>
                      <div><dd className="tabular text-sm font-bold">{number(b.bookings)}</dd><dt className="text-[11px] text-muted">bookings</dt></div>
                    </dl>
                    {b.planName && <div className="mt-2 text-[11px] text-muted">On the <span className="font-semibold text-ink-2">{b.planName}</span> plan</div>}
                  </>
                )}
              </figcaption>
            </figure>
          );
        })}
      </div>
    </section>
  );
}

function PlansTeaser({ plans, loading }: { plans?: Plan[]; loading: boolean }) {
  if (!loading && !plans?.length) return null;
  return (
    <section aria-labelledby="plans-h">
      <SectionHeader title="Plans that grow with you" subtitle="Start free and upgrade only when you need more leads and visibility. Prices exclude 18% GST."
        action={<Link to="/pricing" className="hidden items-center gap-1 text-sm font-semibold sm:inline-flex">Compare all features <ArrowForwardRounded sx={{ fontSize: 18 }} /></Link>} />
      <h2 id="plans-h" className="sr-only">Plans</h2>
      <div className="grid grid-cols-2 gap-3 md:grid-cols-3 lg:grid-cols-5">
        {loading ? Array.from({ length: 5 }, (_, i) => <Skeleton key={i} variant="rounded" height={150} />) : plans!.map((p) => (
          <Link key={p.code} to="/pricing" className={`card relative flex flex-col p-4 ${p.isPopular ? '!border-accent' : ''}`}>
            {p.isPopular && <span className="absolute -top-2.5 left-4 rounded-full bg-accent px-2 py-0.5 text-[10px] font-bold text-on-accent">Most popular</span>}
            <span className="font-semibold">{p.name}</span>
            <span className="mt-2 text-xl font-bold tracking-tight">{p.monthlyPrice === 0 ? 'Free' : moneyExact(p.monthlyPrice)}
              {p.monthlyPrice > 0 && <span className="text-xs font-normal text-muted"> /month</span>}</span>
            <span className="mt-1 text-xs text-muted">{number(p.leadCredits)} lead credits a month</span>
            {p.tagline && <span className="mt-2 line-clamp-2 text-xs text-ink-2">{p.tagline}</span>}
          </Link>
        ))}
      </div>
      <Link to="/pricing" className="mt-4 inline-flex items-center gap-1 text-sm font-semibold sm:hidden">Compare all features <ArrowForwardRounded sx={{ fontSize: 18 }} /></Link>
    </section>
  );
}

const phaseTone: Record<string, string> = {
  done: 'bg-success-soft text-success', progress: 'bg-accent-soft text-accent-ink', planned: 'bg-subtle text-muted',
};

function Roadmap({ items }: { items: ContentBlock[] }) {
  if (!items.length) return null;
  return (
    <section aria-labelledby="road-h">
      <SectionHeader title="Where we're headed" subtitle="Our product roadmap, so you know the platform you're building on keeps getting better." />
      <h2 id="road-h" className="sr-only">Roadmap</h2>
      <ol className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {items.map((r, i) => (
          <li key={r.code} className="card p-5">
            <div className="flex items-center justify-between gap-2">
              <span className="text-xs font-semibold text-muted">Phase {i + 1}</span>
              {r.eyebrow && (
                <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-semibold ${phaseTone[r.iconKey ?? ''] ?? phaseTone.planned}`}>
                  <BlockIcon name={r.iconKey} size={13} />{r.eyebrow}
                </span>
              )}
            </div>
            <h3 className="mt-3 font-semibold">{r.title}</h3>
            {r.subtitle && <p className="mt-1.5 text-sm leading-6 text-muted">{r.subtitle}</p>}
          </li>
        ))}
      </ol>
    </section>
  );
}

function Faqs({ items }: { items: ContentBlock[] }) {
  if (!items.length) return null;
  return (
    <section aria-labelledby="faq-h" className="grid gap-8 lg:grid-cols-[1fr_2fr] lg:gap-16">
      <div>
        <h2 id="faq-h" className="text-xl font-bold tracking-[-0.02em] md:text-[26px] md:leading-tight">Frequently asked questions</h2>
        <p className="mt-2 text-sm text-muted md:text-[15px]">Everything you need to know before you list your business.</p>
      </div>
      <div className="card divide-y divide-line">
        {items.map((f, i) => (
          <details key={f.code} className="group" open={i === 0}>
            <summary className="flex cursor-pointer list-none items-center justify-between gap-4 px-5 py-4 font-semibold transition-colors hover:bg-subtle [&::-webkit-details-marker]:hidden">
              {f.title}
              <ExpandMoreRounded className="shrink-0 text-muted transition-transform group-open:rotate-180" />
            </summary>
            <div className="space-y-3 px-5 pb-5 text-sm leading-6 text-ink-2">{paragraphs(f.body).map((p) => <p key={p.slice(0, 32)}>{p}</p>)}</div>
          </details>
        ))}
      </div>
    </section>
  );
}

function ClosingCta({ block }: { block: ContentBlock }) {
  return (
    <section className="overflow-hidden rounded-2xl bg-navy px-6 py-10 text-white md:px-12 md:py-14">
      <div className="flex flex-col gap-6 md:flex-row md:items-center md:justify-between">
        <div className="max-w-2xl">
          <h2 className="text-2xl font-bold tracking-[-0.02em] md:text-[32px] md:leading-tight">{block.title}</h2>
          {block.subtitle && <p className="mt-3 text-on-navy-muted md:text-lg">{block.subtitle}</p>}
        </div>
        <div className="flex shrink-0 flex-col gap-3 sm:flex-row">
          <PrimaryCta block={block} />
          <Button component={Link} to="/login" size="large" sx={{ color: '#fff', '&:hover': { bgcolor: 'rgba(255,255,255,.08)' } }}>Business sign in</Button>
        </div>
      </div>
    </section>
  );
}

function PageSkeleton() {
  return (
    <div aria-busy="true">
      <div className="bg-navy">
        <div className="container-page grid gap-10 py-12 md:py-16 lg:grid-cols-[1.1fr_1fr]">
          <div className="space-y-4">
            {[40, 90, 70, 60].map((w, i) => <Skeleton key={i} width={`${w}%`} height={i === 1 ? 56 : 24} sx={{ bgcolor: 'rgba(255,255,255,.08)' }} />)}
          </div>
          <Skeleton variant="rounded" sx={{ aspectRatio: '3/2', height: 'auto', bgcolor: 'rgba(255,255,255,.06)' }} />
        </div>
      </div>
      <div className="container-page space-y-6 py-16">
        <Skeleton variant="rounded" height={88} />
        <div className="grid gap-4 md:grid-cols-2"><Skeleton variant="rounded" height={320} /><Skeleton variant="rounded" height={320} /></div>
      </div>
    </div>
  );
}
