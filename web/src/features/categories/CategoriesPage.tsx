import { Link, useParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Skeleton } from '@mui/material';
import { api } from '@/lib/api';
import { useDocumentTitle } from '@/lib/hooks';
import { number } from '@/lib/format';
import type { Category } from '@/lib/types';
import { ErrorState, Img, PageHeader } from '@/components/ui';

export default function CategoriesPage() {
  const { slug } = useParams();
  const { data, isLoading, isError, refetch } = useQuery({ queryKey: ['categories'], queryFn: () => api.get<Category[]>('/api/categories'), staleTime: 600_000 });
  const list = slug ? data?.filter((c) => c.slug === slug) : data;
  const single = slug ? list?.[0] : undefined;
  useDocumentTitle(single?.name ?? 'All categories');

  if (isError) return <div className="container-page py-16"><ErrorState onRetry={() => refetch()} /></div>;

  return (
    <div className="container-page py-8">
      {single?.bannerUrl && (
        <div className="relative mb-8 overflow-hidden rounded-2xl bg-navy">
          <Img src={single.bannerUrl} alt={single.altText ?? single.name} aspect="1600/400" rounded="rounded-none" className="w-full min-h-[160px]" />
          <div className="absolute inset-0 flex flex-col justify-center p-6 text-white md:p-10">
            <h1 className="text-2xl font-bold md:text-4xl">{single.name}</h1>
            <p className="mt-2 max-w-xl text-sm text-on-navy-muted md:text-base">{single.description}</p>
          </div>
        </div>
      )}
      {!single && (
        <PageHeader title="Browse categories" subtitle="Every local service you need, from verified professionals." crumbs={[{ label: 'Home', to: '/' }, { label: 'Categories' }]} />
      )}
      {single && <PageHeader title={`${number(single.businessCount)} businesses`} crumbs={[{ label: 'Home', to: '/' }, { label: 'Categories', to: '/categories' }, { label: single.name }]} />}

      <div className="space-y-10">
        {isLoading ? Array.from({ length: 3 }, (_, i) => <Skeleton key={i} variant="rounded" height={260} />) : list?.map((c) => (
          <section key={c.id} aria-labelledby={`cat-${c.slug}`}>
            {!single && (
              <div className="mb-4 flex items-center gap-3">
                <Img src={c.iconUrl} alt="" className="h-10 w-10 p-1.5" rounded="rounded-lg" fit="contain" fallbackText={c.name} />
                <div className="flex-1">
                  <h2 id={`cat-${c.slug}`} className="text-lg font-bold"><Link to={`/categories/${c.slug}`} className="hover:underline">{c.name}</Link></h2>
                  <p className="text-sm text-muted">{c.description}</p>
                </div>
                <span className="hidden text-sm text-muted sm:block">{number(c.businessCount)} businesses</span>
              </div>
            )}
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
              {c.subCategories.map((s) => (
                <Link key={s.id} to={`/search?sub=${s.slug}`} className="group card overflow-hidden hover:border-line-strong">
                  <Img src={s.imageUrl} alt={s.altText ?? s.name} aspect="16/10" rounded="rounded-none" className="w-full" fallbackText={s.name} />
                  <div className="p-3">
                    <div className="text-sm font-semibold group-hover:underline">{s.name}</div>
                    <div className="text-xs text-muted">{s.businessCount === 0 ? 'Coming soon' : `${s.businessCount} ${s.businessCount === 1 ? 'business' : 'businesses'}`}</div>
                  </div>
                </Link>
              ))}
            </div>
          </section>
        ))}
      </div>
    </div>
  );
}
