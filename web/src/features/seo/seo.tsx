import { useEffect } from 'react';
import { Link, useLocation } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api';
import type { SeoDocument, SeoFaq, SeoLink, SeoLinkGroup, SeoPage } from '@/lib/types';

/** Search pages are described by their query (and stay noindex); every other page by its path alone. */
const seoQuery = (pathname: string, search: string) => (pathname === '/search' ? search : '');

/**
 * The current page's SEO from the server (GET /api/seo): head metadata, and the visible facts - summary, questions and links - which the
 * business and landing pages show. The same service renders them into the HTML crawlers receive, so the rules live in one place.
 */
export function useSeoPage(pathname?: string) {
  const location = useLocation();
  const path = pathname ?? location.pathname;
  const query = seoQuery(path, location.search);
  return useQuery({
    queryKey: ['seo', path, query],
    queryFn: ({ signal }) => api.get<SeoPage>('/api/seo', { path, query: query || null }, signal),
    staleTime: 60_000,
    retry: 1,
  });
}

/**
 * Keeps the document head in step with the page: title, description, robots, canonical, Open Graph / Twitter and JSON-LD. On the first
 * page the server already rendered these (marked data-seo); they are replaced with the same values, then updated on each navigation.
 * If the SEO request fails the page simply keeps its current head.
 */
export function SeoHead() {
  const { data } = useSeoPage();
  useEffect(() => {
    document.documentElement.dataset.seoManaged = '1';
    return () => { delete document.documentElement.dataset.seoManaged; };
  }, []);
  useEffect(() => { if (data) applyHead(data.document); }, [data]);
  return null;
}

function applyHead(d: SeoDocument) {
  const head = document.head;
  head.querySelectorAll('[data-seo], meta[name="description"]').forEach((el) => el.remove());
  document.title = d.title;
  document.documentElement.lang = d.language;
  const meta = (attr: 'name' | 'property', key: string, content?: string | null) => {
    if (!content) return;
    const el = document.createElement('meta');
    el.setAttribute(attr, key);
    el.content = content;
    el.dataset.seo = '';
    head.appendChild(el);
  };
  meta('name', 'description', d.description);
  meta('name', 'robots', d.robots);
  const canonical = document.createElement('link');
  canonical.rel = 'canonical';
  canonical.href = d.canonicalUrl;
  canonical.dataset.seo = '';
  head.appendChild(canonical);
  meta('property', 'og:type', d.ogType);
  meta('property', 'og:site_name', 'Calling Bell');
  meta('property', 'og:title', d.title);
  meta('property', 'og:description', d.description);
  meta('property', 'og:url', d.canonicalUrl);
  meta('property', 'og:locale', d.language.replace('-', '_'));
  meta('property', 'og:image', d.imageUrl);
  meta('property', 'og:image:alt', d.imageUrl ? d.imageAlt : null);
  meta('name', 'twitter:card', d.imageUrl ? 'summary_large_image' : 'summary');
  meta('name', 'twitter:title', d.title);
  meta('name', 'twitter:description', d.description);
  meta('name', 'twitter:image', d.imageUrl);
  for (const json of d.jsonLd) {
    const script = document.createElement('script');
    script.type = 'application/ld+json';
    script.dataset.seo = '';
    script.text = json;
    head.appendChild(script);
  }
}

/** Breadcrumb trail (Home / Country / State / City / …) as an ordered list of links. */
export function Breadcrumbs({ items, className = '' }: { items: SeoLink[]; className?: string }) {
  if (items.length < 2) return null;
  return (
    <nav aria-label="Breadcrumb" className={`text-xs text-muted ${className}`}>
      <ol className="flex flex-wrap items-center gap-x-1.5 gap-y-1">
        {items.map((c, i) => (
          <li key={c.url} className="flex items-center gap-1.5">
            {i > 0 && <span aria-hidden className="text-faint">/</span>}
            {i === items.length - 1 ? <span aria-current="page" className="text-ink-2">{c.name}</span> : <Link to={c.url} className="hover:underline">{c.name}</Link>}
          </li>
        ))}
      </ol>
    </nav>
  );
}

/** Groups of internal links (nearby places, related categories) as plain, crawlable anchors. */
export function LinkGroups({ groups }: { groups: SeoLinkGroup[] }) {
  return (
    <>
      {groups.filter((g) => g.links.length > 0).map((g) => (
        <nav key={g.title} aria-label={g.title} className="mt-8">
          <h2 className="text-lg font-semibold">{g.title}</h2>
          <ul className="mt-3 flex flex-wrap gap-2">
            {g.links.map((l) => (
              <li key={l.url}>
                <Link to={l.url} className="inline-flex items-center gap-1.5 rounded-full border border-line px-3 py-1.5 text-sm text-ink-2 transition-colors hover:border-line-strong hover:bg-subtle">
                  {l.name}{l.count != null && <span className="text-xs text-muted tabular">{l.count}</span>}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
      ))}
    </>
  );
}

/** Questions and answers drawn from the page's data; the FAQPage structured data describes exactly these. */
export function FaqSection({ items, className = 'mt-8' }: { items: SeoFaq[]; className?: string }) {
  if (items.length === 0) return null;
  return (
    <section aria-labelledby="faq-h" className={className}>
      <h2 id="faq-h" className="text-lg font-semibold">Frequently asked questions</h2>
      <dl className="mt-3 divide-y divide-line rounded-2xl border border-line bg-surface">
        {items.map((f) => (
          <div key={f.question} className="p-4">
            <dt className="font-semibold text-ink">{f.question}</dt>
            <dd className="mt-1 text-sm leading-6 text-ink-2">{f.answer}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
