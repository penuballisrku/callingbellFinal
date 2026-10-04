import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { compactNumber, number } from '@/lib/format';
import { useDocumentTitle } from '@/lib/hooks';
import type { MarketingPage } from '@/lib/types';
import { ErrorState } from '@/components/ui';
import { ClosingCta, ContentPageSkeleton, DividedList, FeaturePanel, IconCardGrid, IntroSection, PageHero, StatsStrip } from '@/features/content/ContentBlocks';

/** "About Calling Bell": every word comes from MarketingContent (PageKey "About"); the numbers are live platform stats. */
export default function AboutPage() {
  useDocumentTitle('About Calling Bell');
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['content', 'About'],
    queryFn: () => api.get<MarketingPage>('/api/content/pages/About'),
    staleTime: 300_000,
  });

  if (isError) return <div className="container-page py-16"><ErrorState onRetry={() => refetch()} /></div>;
  if (isLoading || !data) return <ContentPageSkeleton />;

  const section = (key: string) => data.blocks.filter((b) => b.section === key);
  const hero = section('Hero')[0];
  const story = section('Story')[0];
  const company = section('Company')[0];
  const cta = section('Cta')[0];
  const s = data.stats;

  return (
    <>
      {hero && <PageHero block={hero} crumb="About" secondary={{ label: 'List your business', to: '/list-your-business' }} />}
      <StatsStrip items={[
        ['Active businesses', number(s.activeBusinesses)],
        ['Verified businesses', number(s.verifiedBusinesses)],
        ['Cities', number(s.cities)],
        ['Service categories', number(s.categories)],
        ['Bookings completed', compactNumber(s.bookingsCompleted)],
        ['Average customer rating', `${s.averageRating.toFixed(1)} / 5`],
      ]} />

      <div className="container-page space-y-20 py-16 md:space-y-24 md:py-20">
        {story && <IntroSection block={story} id="story-h" />}
        <IconCardGrid items={section('Purpose')} id="Mission and vision" columns={2} />
        <IconCardGrid items={section('Pillar')} id="pillars-h" title="What we do" numbered
          subtitle="One platform for the whole journey, from the first search to a completed job." />
        <IconCardGrid items={section('Audience')} id="audience-h" title="Who we serve" columns={3} />
        <DividedList items={section('Value')} id="values-h" title="What we stand for" />
        {company && <FeaturePanel block={company} id="company-h" />}
        {cta && <ClosingCta block={cta} />}
      </div>
    </>
  );
}
