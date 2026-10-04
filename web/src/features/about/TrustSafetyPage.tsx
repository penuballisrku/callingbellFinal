import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Button } from '@mui/material';
import { api } from '@/lib/api';
import { number } from '@/lib/format';
import { useDocumentTitle } from '@/lib/hooks';
import type { MarketingPage } from '@/lib/types';
import { ErrorState } from '@/components/ui';
import {
  ClosingCta, ContentPageSkeleton, DividedList, FaqList, FeaturePanel, IconCardGrid, IntroSection, NumberedSteps, PageHero, StatsStrip,
} from '@/features/content/ContentBlocks';

/** "Trust & safety": every word comes from MarketingContent (PageKey "TrustSafety"); the numbers are live platform stats. */
export default function TrustSafetyPage() {
  useDocumentTitle('Trust & safety');
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['content', 'TrustSafety'],
    queryFn: () => api.get<MarketingPage>('/api/content/pages/TrustSafety'),
    staleTime: 300_000,
  });

  if (isError) return <div className="container-page py-16"><ErrorState onRetry={() => refetch()} /></div>;
  if (isLoading || !data) return <ContentPageSkeleton />;

  const section = (key: string) => data.blocks.filter((b) => b.section === key);
  const hero = section('Hero')[0];
  const story = section('Story')[0];
  const report = section('Report')[0];
  const cta = section('Cta')[0];
  const s = data.stats;

  return (
    <>
      {hero && <PageHero block={hero} crumb="Trust & safety" secondary={{ label: 'About Calling Bell', to: '/about' }} />}
      <StatsStrip items={[
        ['Verified businesses', number(s.verifiedBusinesses)],
        ['Active businesses', number(s.activeBusinesses)],
        ['Cities', number(s.cities)],
        ['Average customer rating', `${s.averageRating.toFixed(1)} / 5`],
      ]} />

      <div className="container-page space-y-20 py-16 md:space-y-24 md:py-20">
        {story && <IntroSection block={story} id="commitment-h" />}
        <IconCardGrid items={section('Pillar')} id="pillars-h" title="How we keep Calling Bell safe" />
        <NumberedSteps items={section('Step')} id="verify-h" title="How business verification works"
          subtitle="Every listing is reviewed before customers can find it." />
        <DividedList items={section('Standard')} id="standards-h" title="Our review standards"
          subtitle="Ratings are only useful when they are honest. These rules apply to every review." />
        <div className="grid gap-10 lg:grid-cols-2 lg:gap-8">
          <IconCardGrid items={section('TipCustomer')} id="tips-customers-h" title="Safety tips for customers" columns={2} />
          <IconCardGrid items={section('TipBusiness')} id="tips-business-h" title="Safety tips for businesses" columns={2} />
        </div>
        {report && <FeaturePanel block={report} id="report-h" action={<Button component={Link} to="/support" variant="contained">Contact support</Button>} />}
        <FaqList items={section('Faq')} subtitle="Common questions about verification, reviews and your data." />
        {cta && <ClosingCta block={cta} />}
      </div>
    </>
  );
}
