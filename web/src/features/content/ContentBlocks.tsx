import { useMemo, type ComponentType, type ReactNode } from 'react';
import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Button, Skeleton, type SvgIconProps } from '@mui/material';
import ArrowForwardRounded from '@mui/icons-material/ArrowForwardRounded';
import ExpandMoreRounded from '@mui/icons-material/ExpandMoreRounded';
import { api } from '@/lib/api';
import type { ContentBlock, MarketingPage } from '@/lib/types';
import { SectionHeader } from '@/components/ui';
import { useBrowsingCountryCode, useVisitorCountry, useVisitorCountryName, withCountry } from '@/components/VisitorCountry';
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

/** Icon keys stored in MarketingContent.IconKey. Icons are UI chrome; the words and media come from the database. */
const icons: Record<string, ComponentType<SvgIconProps>> = {
  search: SearchRounded, event: EventAvailableOutlined, verified: VerifiedOutlined, trending: TrendingUpRounded, storefront: StorefrontOutlined,
  inbox: InboxOutlined, star: StarOutlineRounded, bolt: BoltRounded, calendar: CalendarMonthOutlined, chat: ChatBubbleOutlineRounded,
  videocam: VideocamOutlined, campaign: CampaignOutlined, analytics: InsightsRounded, groups: GroupsOutlined, wallet: AccountBalanceWalletOutlined,
  notifications: NotificationsActiveOutlined, shield: ShieldOutlined, dashboard: SpaceDashboardOutlined, place: PlaceOutlined,
  person_add: PersonAddAltOutlined, done: CheckCircleRounded, progress: AutorenewRounded, planned: ScheduleRounded,
};

export function BlockIcon({ name, size = 22 }: { name?: string | null; size?: number }) {
  const Icon = (name && icons[name]) || CircleOutlined;
  return <Icon sx={{ fontSize: size }} aria-hidden />;
}

/**
 * A content page (GET /api/content/pages/{pageKey}) for the country being browsed: its photos where the page has them (a Montreal
 * storefront for Canada), and its name filled into the copy ("{country's} real-time network" reads "Canada's real-time network").
 */
export function useMarketingPage(pageKey: string, staleTime = 300_000) {
  const code = useBrowsingCountryCode();
  // Waits for the country (a quick request) so the default photo isn't shown first and then swapped; loads without it if detection fails.
  const detecting = useVisitorCountry().isPending;
  const query = useQuery({
    queryKey: ['content', pageKey, code],
    queryFn: () => api.get<MarketingPage>(`/api/content/pages/${pageKey}`, { country: code }),
    enabled: !!code || !detecting,
    staleTime,
  });
  const country = useVisitorCountryName();
  const data = useMemo((): MarketingPage | undefined => query.data && {
    ...query.data,
    blocks: query.data.blocks.map((b) => ({
      ...b, title: withCountry(b.title, country), subtitle: b.subtitle && withCountry(b.subtitle, country),
      body: b.body && withCountry(b.body, country), eyebrow: b.eyebrow && withCountry(b.eyebrow, country),
    })),
  }, [query.data, country]);
  return { ...query, data };
}

/** MarketingContent.Body paragraphs (stored with line breaks). */
export const paragraphs = (body?: string | null) => (body ?? '').split('\n').map((p) => p.trim()).filter(Boolean);

export function Eyebrow({ children, onDark }: { children: ReactNode; onDark?: boolean }) {
  return <p className={`text-xs font-semibold uppercase tracking-[0.12em] ${onDark ? 'text-accent' : 'text-accent-ink'}`}>{children}</p>;
}

/* ---------------------------------------------------------------------------------------------- */
/* Sections shared by the database-driven content pages (About, Trust & safety).                  */

const outlinedOnDark = {
  color: '#fff', borderColor: 'rgba(255,255,255,.25)', bgcolor: 'transparent', boxShadow: 'none',
  '&:hover': { borderColor: 'rgba(255,255,255,.5)', bgcolor: 'rgba(255,255,255,.06)' },
};

/** Navy page hero with a breadcrumb; the block's CTA is the primary button. */
export function PageHero({ block, crumb, secondary }: { block: ContentBlock; crumb: string; secondary?: { label: string; to: string } }) {
  return (
    <section className="bg-navy text-white">
      <div className="container-page py-14 md:py-20">
        <nav aria-label="Breadcrumb" className="mb-6 text-xs text-on-navy-faint">
          <Link to="/" className="hover:text-white">Home</Link><span className="mx-1.5" aria-hidden>/</span><span className="text-on-navy-muted">{crumb}</span>
        </nav>
        <div className="max-w-3xl">
          {block.eyebrow && <Eyebrow onDark>{block.eyebrow}</Eyebrow>}
          <h1 className="mt-3 text-3xl font-bold leading-[1.12] tracking-tight sm:text-4xl lg:text-[52px]">{block.title}</h1>
          {block.subtitle && <p className="mt-5 max-w-2xl text-base leading-7 text-on-navy-muted md:text-lg md:leading-8">{block.subtitle}</p>}
          <div className="mt-8 flex flex-col gap-3 sm:flex-row">
            {block.linkUrl && (
              <Button component={Link} to={block.linkUrl} variant="contained" color="secondary" size="large" endIcon={<ArrowForwardRounded />}>
                {block.ctaText ?? 'Get started'}
              </Button>
            )}
            {secondary && <Button component={Link} to={secondary.to} size="large" variant="outlined" sx={outlinedOnDark}>{secondary.label}</Button>}
          </div>
        </div>
      </div>
    </section>
  );
}

/** Live platform numbers under a page hero. */
export function StatsStrip({ items }: { items: [label: string, value: string][] }) {
  return (
    <section aria-label="Calling Bell in numbers" className="border-b border-line bg-surface">
      <dl className={`container-page grid grid-cols-2 py-6 sm:grid-cols-3 lg:divide-x lg:divide-line ${items.length > 4 ? 'lg:grid-cols-6' : 'lg:grid-cols-4'}`}>
        {items.map(([label, value]) => (
          <div key={label} className="px-2 py-3 text-center lg:px-4">
            <dd className="tabular text-2xl font-bold tracking-tight md:text-[28px]">{value}</dd>
            <dt className="mt-1 text-xs text-muted md:text-[13px]">{label}</dt>
          </div>
        ))}
      </dl>
    </section>
  );
}

/** Heading on the left, body paragraphs on the right. */
export function IntroSection({ block, id }: { block: ContentBlock; id: string }) {
  return (
    <section aria-labelledby={id} className="grid gap-8 lg:grid-cols-[1fr_1.4fr] lg:gap-16">
      <div>
        {block.eyebrow && <Eyebrow>{block.eyebrow}</Eyebrow>}
        <h2 id={id} className="mt-2 text-2xl font-bold tracking-[-0.02em] md:text-[34px] md:leading-tight">{block.title}</h2>
        {block.subtitle && <p className="mt-3 text-lg font-semibold text-ink-2">{block.subtitle}</p>}
      </div>
      <div className="space-y-4 text-[15px] leading-7 text-ink-2 md:text-base md:leading-8">
        {paragraphs(block.body).map((p) => <p key={p.slice(0, 32)}>{p}</p>)}
      </div>
    </section>
  );
}

/** Cards with an icon, optional accent label (eyebrow), title, text and link (LinkUrl / CtaText). `numbered` adds 01, 02… in the corner. */
export function IconCardGrid({ items, id, title, subtitle, columns = 4, numbered }: {
  items: ContentBlock[]; id: string; title?: string; subtitle?: string; columns?: 2 | 3 | 4; numbered?: boolean;
}) {
  if (!items.length) return null;
  const cols = { 2: 'md:grid-cols-2', 3: 'md:grid-cols-3', 4: 'sm:grid-cols-2 lg:grid-cols-4' }[columns];
  const List = numbered ? 'ol' : 'div';
  const Item = numbered ? 'li' : 'article';
  return (
    <section aria-labelledby={title ? id : undefined} aria-label={title ? undefined : id}>
      {title && <SectionHeader id={id} title={title} subtitle={subtitle} />}
      <List className={`grid gap-4 ${cols}`}>
        {items.map((b, i) => (
          <Item key={b.code} className="card flex flex-col p-5 md:p-6">
            <div className="flex items-center justify-between">
              <span className="flex h-10 w-10 items-center justify-center rounded-lg bg-accent-soft text-accent-ink"><BlockIcon name={b.iconKey} /></span>
              {numbered && <span className="tabular text-xs font-semibold text-faint" aria-hidden>{String(i + 1).padStart(2, '0')}</span>}
            </div>
            {b.eyebrow && <p className="mt-4 text-sm font-semibold text-accent-ink">{b.eyebrow}</p>}
            <h3 className={`${b.eyebrow ? 'mt-1' : 'mt-4'} font-semibold`}>{b.title}</h3>
            {b.subtitle && <p className="mt-1.5 text-sm leading-6 text-muted">{b.subtitle}</p>}
            {b.linkUrl && (
              <Link to={b.linkUrl} className="mt-auto inline-flex items-center gap-1 pt-4 text-sm font-semibold hover:underline">
                {b.ctaText ?? 'Learn more'} <ArrowForwardRounded sx={{ fontSize: 16 }} />
              </Link>
            )}
          </Item>
        ))}
      </List>
    </section>
  );
}

/** One bordered panel split into a two-column list of icon rows. */
export function DividedList({ items, id, title, subtitle }: { items: ContentBlock[]; id: string; title: string; subtitle?: string }) {
  if (!items.length) return null;
  return (
    <section aria-labelledby={id}>
      <SectionHeader id={id} title={title} subtitle={subtitle} />
      <div className="card grid overflow-hidden md:grid-cols-2">
        {items.map((b, i) => (
          <article key={b.code}
            className={`flex gap-4 border-line p-5 md:p-6 ${i > 0 ? 'border-t' : ''} ${i === 1 ? 'md:border-t-0' : ''} ${i % 2 === 1 ? 'md:border-l' : ''}`}>
            <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-line bg-subtle text-ink"><BlockIcon name={b.iconKey} /></span>
            <div className="min-w-0">
              <h3 className="font-semibold">{b.title}</h3>
              {b.subtitle && <p className="mt-1 text-sm leading-6 text-muted">{b.subtitle}</p>}
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}

/** Numbered process steps (1, 2, 3…). */
export function NumberedSteps({ items, id, title, subtitle }: { items: ContentBlock[]; id: string; title: string; subtitle?: string }) {
  if (!items.length) return null;
  return (
    <section aria-labelledby={id}>
      <SectionHeader id={id} title={title} subtitle={subtitle} />
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

/** Accordion of questions; the answer is the block body. */
export function FaqList({ items, subtitle }: { items: ContentBlock[]; subtitle?: string }) {
  if (!items.length) return null;
  return (
    <section aria-labelledby="faq-h" className="grid gap-8 lg:grid-cols-[1fr_2fr] lg:gap-16">
      <div>
        <h2 id="faq-h" className="text-xl font-bold tracking-[-0.02em] md:text-[26px] md:leading-tight">Frequently asked questions</h2>
        {subtitle && <p className="mt-2 text-sm text-muted md:text-[15px]">{subtitle}</p>}
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

/** Highlighted panel with a large icon, accent edge, title and body paragraphs; optional action below. */
export function FeaturePanel({ block, id, action }: { block: ContentBlock; id: string; action?: ReactNode }) {
  return (
    <section aria-labelledby={id} className="card grid gap-6 border-l-4 border-l-accent p-6 md:grid-cols-[auto_1fr] md:gap-8 md:p-10">
      <span className="flex h-14 w-14 items-center justify-center rounded-2xl bg-navy text-accent"><BlockIcon name={block.iconKey} size={28} /></span>
      <div>
        {block.eyebrow && <Eyebrow>{block.eyebrow}</Eyebrow>}
        <h2 id={id} className="mt-2 text-2xl font-bold tracking-[-0.02em] md:text-[30px] md:leading-tight">{block.title}</h2>
        {block.subtitle && <p className="mt-2 font-semibold text-ink-2">{block.subtitle}</p>}
        <div className="mt-4 max-w-3xl space-y-4 text-[15px] leading-7 text-ink-2">
          {paragraphs(block.body).map((p) => <p key={p.slice(0, 32)}>{p}</p>)}
        </div>
        {action && <div className="mt-6">{action}</div>}
      </div>
    </section>
  );
}

/** Navy closing banner: "Find services" plus the block's own CTA. */
export function ClosingCta({ block }: { block: ContentBlock }) {
  return (
    <section className="overflow-hidden rounded-2xl bg-navy px-6 py-10 text-white md:px-12 md:py-14">
      <div className="flex flex-col gap-6 md:flex-row md:items-center md:justify-between">
        <div className="max-w-2xl">
          <h2 className="text-2xl font-bold tracking-[-0.02em] md:text-[32px] md:leading-tight">{block.title}</h2>
          {block.subtitle && <p className="mt-3 text-on-navy-muted md:text-lg">{block.subtitle}</p>}
        </div>
        <div className="flex shrink-0 flex-col gap-3 sm:flex-row">
          <Button component={Link} to="/search" size="large" variant="contained" color="secondary" endIcon={<ArrowForwardRounded />}>Find services</Button>
          {block.linkUrl && <Button component={Link} to={block.linkUrl} size="large" variant="outlined" sx={outlinedOnDark}>{block.ctaText ?? 'List your business'}</Button>}
        </div>
      </div>
    </section>
  );
}

/** Loading state for content pages: hero lines, a stats bar and two cards. */
export function ContentPageSkeleton() {
  return (
    <div aria-busy="true">
      <div className="bg-navy">
        <div className="container-page space-y-4 py-14 md:py-20">
          {[20, 70, 55, 40].map((w, i) => <Skeleton key={i} width={`${w}%`} height={i === 1 ? 56 : 24} sx={{ bgcolor: 'rgba(255,255,255,.08)' }} />)}
        </div>
      </div>
      <div className="container-page space-y-6 py-16">
        <Skeleton variant="rounded" height={88} />
        <div className="grid gap-4 md:grid-cols-2"><Skeleton variant="rounded" height={220} /><Skeleton variant="rounded" height={220} /></div>
      </div>
    </div>
  );
}
