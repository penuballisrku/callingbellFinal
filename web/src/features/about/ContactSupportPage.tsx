import { Link } from 'react-router';
import { Button } from '@mui/material';
import { useDocumentTitle } from '@/lib/hooks';
import type { ContentBlock } from '@/lib/types';
import { ErrorState, SectionHeader } from '@/components/ui';
import { isOwner, useAuth } from '@/stores/auth';
import {
  BlockIcon, ClosingCta, ContentPageSkeleton, DividedList, FaqList, FeaturePanel, IconCardGrid, PageHero, paragraphs, useMarketingPage } from '@/features/content/ContentBlocks';

/** "Contact support": every word, topic and contact channel comes from MarketingContent (PageKey "ContactSupport"). */
export default function ContactSupportPage() {
  useDocumentTitle('Contact support');
  const user = useAuth((st) => st.user);
  const { data, isLoading, isError, refetch } = useMarketingPage('ContactSupport');

  if (isError) return <div className="container-page py-16"><ErrorState onRetry={() => refetch()} /></div>;
  if (isLoading || !data) return <ContentPageSkeleton />;

  const section = (key: string) => data.blocks.filter((b) => b.section === key);
  const hero = section('Hero')[0];
  const business = section('Business')[0];
  const cta = section('Cta')[0];

  return (
    <>
      {hero && <PageHero block={hero} crumb="Contact support" secondary={{ label: 'Trust & safety', to: '/trust-and-safety' }} />}

      <div className="container-page space-y-20 py-16 md:space-y-24 md:py-20">
        <IconCardGrid items={section('Topic')} id="topics-h" title="Browse help topics" columns={3}
          subtitle="Most questions can be answered in a few clicks from your account or the pages below." />
        <Channels items={section('Channel')} />
        <DividedList items={section('Prepare')} id="prepare-h" title="Before you contact us"
          subtitle="Including these details helps our team understand your request and resolve it faster." />
        {business && (
          <FeaturePanel block={business} id="business-h"
            action={business.linkUrl && (
              <Button component={Link} to={user && isOwner(user) ? business.linkUrl : '/login'} variant="contained">
                {user && isOwner(user) ? (business.ctaText ?? 'Go to business dashboard') : 'Business sign in'}
              </Button>
            )} />
        )}
        <FaqList items={section('Faq')} subtitle="Quick answers to the questions we hear most often." />
        {cta && <ClosingCta block={cta} />}
      </div>
    </>
  );
}

/** Support email, phone and office. Shown only when channels are configured in the database. */
function Channels({ items }: { items: ContentBlock[] }) {
  if (!items.length) return null;
  return (
    <section aria-labelledby="channels-h">
      <SectionHeader id="channels-h" title="Get in touch" subtitle="Reach our support team directly." />
      <div className="grid gap-4 md:grid-cols-3">
        {items.map((c) => (
          <article key={c.code} className="card flex flex-col p-6">
            <span className="flex h-10 w-10 items-center justify-center rounded-lg bg-accent-soft text-accent-ink"><BlockIcon name={c.iconKey} /></span>
            {c.eyebrow && <p className="mt-4 text-sm font-semibold text-accent-ink">{c.eyebrow}</p>}
            <h3 className="mt-1 font-semibold">{c.title}</h3>
            {c.subtitle && <p className="mt-1 break-words text-[15px] font-medium text-ink-2">{c.subtitle}</p>}
            {paragraphs(c.body).map((p) => <p key={p.slice(0, 32)} className="mt-1.5 text-sm leading-6 text-muted">{p}</p>)}
            {c.linkUrl && (
              <div className="mt-auto pt-5"><Button href={c.linkUrl} variant="outlined">{c.ctaText ?? 'Contact'}</Button></div>
            )}
          </article>
        ))}
      </div>
    </section>
  );
}
