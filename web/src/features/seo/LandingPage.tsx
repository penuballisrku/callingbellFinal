import { useEffect } from 'react';
import { Link, useNavigate } from 'react-router';
import { Button, Skeleton } from '@mui/material';
import { BusinessCard } from '@/components/BusinessCard';
import { EmptyState, ErrorState } from '@/components/ui';
import { Breadcrumbs, FaqSection, LinkGroups, useSeoPage } from './seo';

/**
 * A category page (/category/{slug}) or location page (/location/{country}/{state}/{city}/{area}/{category}): the businesses listed
 * there, a factual summary, related places and categories, and questions answered from the data. Everything comes from the server's
 * SEO service (the same content it renders into the HTML for crawlers).
 */
export default function LandingPage() {
  const navigate = useNavigate();
  const { data, isLoading, isError, refetch } = useSeoPage();
  // A city under the wrong state (or a similar non-canonical address) moves to its canonical page.
  useEffect(() => { if (data?.redirectTo) navigate(data.redirectTo, { replace: true }); }, [data?.redirectTo, navigate]);

  if (isError) return <div className="container-page py-16"><ErrorState onRetry={() => refetch()} /></div>;
  if (isLoading || !data || data.redirectTo) return <LandingSkeleton />;
  const page = data.landing;
  if (!page) {
    return (
      <div className="container-page py-20">
        <EmptyState title="Page not found" message="We couldn't find that place or category. It may have been renamed."
          action={<Button component={Link} to="/categories" variant="contained">Browse categories</Button>} />
      </div>
    );
  }

  return (
    <div className="container-page py-8 md:py-10">
      <Breadcrumbs items={page.breadcrumbs} className="mb-4" />
      <header className="max-w-3xl">
        <h1 className="text-2xl font-bold tracking-tight md:text-3xl">{page.heading}</h1>
        <p className="mt-2 leading-7 text-ink-2">{page.summary}</p>
      </header>

      {page.businesses.length > 0 ? (
        <section aria-labelledby="listed-h" className="mt-8">
          <h2 id="listed-h" className="sr-only">{page.categoryName ?? 'Businesses'}{page.place ? ` in ${page.place}` : ''}</h2>
          <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
            {page.businesses.map((b) => <li key={b.id}><BusinessCard b={b} /></li>)}
          </ul>
          {page.businessCount > page.businesses.length && (
            <p className="mt-4 text-sm text-muted">
              Showing {page.businesses.length} of {page.businessCount}.{' '}
              <Link to="/search" className="font-semibold text-ink hover:underline">Search all listings</Link>
            </p>
          )}
        </section>
      ) : (
        <div className="mt-8">
          <EmptyState title="Nothing listed here yet" message="Businesses appear here once they are listed on Calling Bell."
            action={<Button component={Link} to="/list-your-business" variant="outlined">List your business</Button>} />
        </div>
      )}

      <LinkGroups groups={page.links} />
      <FaqSection items={page.faq} />
    </div>
  );
}

function LandingSkeleton() {
  return (
    <div className="container-page py-8 md:py-10" aria-busy="true">
      <Skeleton width={260} height={20} />
      <Skeleton width="55%" height={44} sx={{ mt: 1 }} />
      <Skeleton width="80%" />
      <div className="mt-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        {Array.from({ length: 8 }, (_, i) => <Skeleton key={i} variant="rounded" height={260} sx={{ borderRadius: '16px' }} />)}
      </div>
    </div>
  );
}
