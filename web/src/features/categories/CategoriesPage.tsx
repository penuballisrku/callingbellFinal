import { useEffect, useId, useMemo, useState, type KeyboardEvent, type ReactNode } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { Button, Skeleton } from '@mui/material';
import SearchRounded from '@mui/icons-material/SearchRounded';
import CloseRounded from '@mui/icons-material/CloseRounded';
import ChevronRightRounded from '@mui/icons-material/ChevronRightRounded';
import ArrowForwardRounded from '@mui/icons-material/ArrowForwardRounded';
import CategoryOutlined from '@mui/icons-material/CategoryOutlined';
import HandymanOutlined from '@mui/icons-material/HandymanOutlined';
import VerifiedOutlined from '@mui/icons-material/VerifiedOutlined';
import TravelExploreRounded from '@mui/icons-material/TravelExploreRounded';
import { useCategories, useDocumentTitle } from '@/lib/hooks';
import { number, pluralize } from '@/lib/format';
import type { Category, SubCategory } from '@/lib/types';
import { EmptyState, ErrorState, Img, PageHeader } from '@/components/ui';
import { Marquee } from '@/components/Marquee';
import { PlaceholderTicker } from '@/components/PlaceholderTicker';

const chip = 'inline-flex shrink-0 items-center gap-1.5 rounded-full border border-line bg-surface py-1 pl-1.5 pr-3 text-[13px] font-medium '
  + 'text-ink-2 outline-offset-2 transition-colors hover:border-line-strong hover:text-ink focus-visible:outline-2 focus-visible:outline-accent';

/**
 * How well a name matches the typed text: 0 = the name starts with it, 1 = a word in the name starts with it ("care" in "Pet Care"),
 * -1 = no match. Mid-word hits ("car" inside "Healthcare") don't count.
 */
function matchRank(name: string, term: string): number {
  const n = name.toLowerCase();
  if (n.startsWith(term)) return 0;
  return n.split(/[\s&/,()-]+/).some((w) => w.startsWith(term)) ? 1 : -1;
}
const matches = (name: string, term: string) => matchRank(name, term) >= 0;

/** Categories whose name matches (with all their sub-categories), or that contain matching sub-categories (only those). */
function filterTree(list: Category[], term: string): Category[] {
  if (!term) return list;
  return list.flatMap((c) => {
    if (matches(c.name, term)) return [c];
    const subs = c.subCategories.filter((s) => matches(s.name, term));
    return subs.length ? [{ ...c, subCategories: subs }] : [];
  });
}

type Suggestion =
  | { kind: 'category'; id: string; label: string; detail: string; iconUrl?: string | null; slug: string }
  | { kind: 'sub'; id: string; label: string; detail: string; iconUrl?: string | null; slug: string };

/** Every match, categories first then services; within each, whole-name matches before word matches, then the most listed. */
function suggest(categories: Category[], subs: SubCategory[], term: string): Suggestion[] {
  if (!term) return [];
  const scored = [
    ...categories.map((c) => ({ rank: matchRank(c.name, term), kind: 0, count: c.businessCount,
      item: { kind: 'category', id: c.id, label: c.name, iconUrl: c.iconUrl, slug: c.slug,
              detail: `Category · ${pluralize(c.subCategories.length, 'service')}` } as Suggestion })),
    ...subs.map((s) => ({ rank: matchRank(s.name, term), kind: 1, count: s.businessCount,
      item: { kind: 'sub', id: s.id, label: s.name, iconUrl: s.iconUrl, slug: s.slug,
              detail: `${s.categoryName} · ${s.businessCount ? pluralize(s.businessCount, 'business', 'businesses') : 'Coming soon'}` } as Suggestion })),
  ];
  return scored.filter((x) => x.rank >= 0)
    .sort((a, b) => a.kind - b.kind || a.rank - b.rank || b.count - a.count)
    .map((x) => x.item);
}

/** Bolds the matched word start (or the first occurrence) of the typed text. */
function Highlight({ text, term }: { text: string; term: string }) {
  const lower = text.toLowerCase();
  // First position where a word starts with the term (same rule as matchRank), else the first occurrence.
  let i = -1;
  for (let p = lower.indexOf(term); p >= 0; p = lower.indexOf(term, p + 1)) {
    if (p === 0 || /[\s&/,()-]/.test(lower[p - 1]!)) { i = p; break; }
  }
  if (i < 0) i = lower.indexOf(term);
  if (!term || i < 0) return <>{text}</>;
  return <>{text.slice(0, i)}<mark className="bg-transparent font-semibold text-ink">{text.slice(i, i + term.length)}</mark>{text.slice(i + term.length)}</>;
}

/**
 * Search box for the categories page: an animated "Search for <name>" placeholder cycling through category and sub-category names,
 * and suggestions while typing (keyboard: up/down, Enter, Esc). Choosing a category scrolls to it; a sub-category opens its results.
 */
function CategorySearch({ value, onChange, categories, subs, names }: {
  value: string; onChange: (v: string) => void; categories: Category[]; subs: SubCategory[]; names: string[];
}) {
  const navigate = useNavigate();
  const id = useId();
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const term = value.trim().toLowerCase();
  const items = useMemo(() => suggest(categories, subs, term), [categories, subs, term]);
  const showPanel = open && term.length > 0;

  const choose = (s: Suggestion) => {
    setOpen(false);
    setActive(-1);
    if (s.kind === 'sub') { navigate(`/search?sub=${s.slug}`); return; }
    onChange(s.label);
    // After the list below filters to this category, bring it into view.
    requestAnimationFrame(() => document.getElementById(`cat-${s.slug}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  };

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Escape') { setOpen(false); setActive(-1); return; }
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      if (!items.length) return;
      e.preventDefault();
      setOpen(true);
      setActive((i) => (e.key === 'ArrowDown' ? (i + 1) % items.length : i <= 0 ? items.length - 1 : i - 1));
      return;
    }
    if (e.key === 'Enter' && showPanel && active >= 0 && items[active]) { e.preventDefault(); choose(items[active]); }
  };

  return (
    <form role="search" className="flex w-full max-w-2xl gap-2"
      onSubmit={(e) => { e.preventDefault(); setOpen(false); setActive(-1); document.getElementById('category-results')?.scrollIntoView({ behavior: 'smooth' }); }}>
      <div className="relative min-w-0 flex-1">
        <div className="flex h-11 items-center gap-2 rounded-lg border border-line bg-surface px-3 transition-[border-color,box-shadow] focus-within:border-accent focus-within:shadow-[0_0_0_3px_var(--cb-ring)]">
          <SearchRounded fontSize="small" className="shrink-0 text-faint" />
          <div className="relative min-w-0 flex-1">
            <input value={value} autoComplete="off" spellCheck={false} aria-label="Search categories and services"
              role="combobox" aria-autocomplete="list" aria-expanded={showPanel} aria-controls={`${id}-list`}
              aria-activedescendant={showPanel && active >= 0 ? `${id}-opt-${active}` : undefined}
              onChange={(e) => { onChange(e.target.value); setOpen(true); setActive(-1); }}
              onFocus={() => setOpen(true)} onBlur={() => { setOpen(false); setActive(-1); }} onKeyDown={onKeyDown}
              className="h-10 w-full bg-transparent text-[15px] text-ink outline-none" />
            {!value && <PlaceholderTicker names={names} />}
          </div>
          {value && (
            <button type="button" aria-label="Clear search" onClick={() => { onChange(''); setActive(-1); }}
              className="grid h-7 w-7 shrink-0 place-items-center rounded-md text-muted hover:bg-subtle">
              <CloseRounded fontSize="small" />
            </button>
          )}
        </div>

        {showPanel && (
          <div className="absolute left-0 right-0 top-full z-40 mt-2 overflow-hidden rounded-xl border border-line bg-surface shadow-[var(--cb-shadow-lg)]"
            onMouseDown={(e) => e.preventDefault() /* keep focus in the input so the click lands */}>
            {items.length === 0 ? (
              <p className="px-4 py-3 text-sm text-muted">No categories match “{value.trim()}”.</p>
            ) : (
              <>
                <ul id={`${id}-list`} role="listbox" aria-label="Suggestions" className="max-h-[min(70vh,560px)] overflow-y-auto py-1.5">
                  {([['category', 'Categories'], ['sub', 'Services']] as const).map(([kind, title]) => {
                    const group = items.map((s, i) => ({ s, i })).filter(({ s }) => s.kind === kind);
                    if (!group.length) return null;
                    return (
                      <li key={kind} role="presentation">
                        <div className="sticky top-0 z-10 flex items-center justify-between bg-surface/95 px-3 pb-1 pt-2 text-[11px] font-semibold uppercase tracking-[0.08em] text-muted backdrop-blur">
                          <span>{title}</span><span className="font-normal normal-case tracking-normal text-faint">{group.length}</span>
                        </div>
                        <ul role="presentation">
                          {group.map(({ s, i }) => (
                            <li key={`${s.kind}-${s.id}`} id={`${id}-opt-${i}`} role="option" aria-selected={i === active}
                              ref={i === active ? (el) => el?.scrollIntoView({ block: 'nearest' }) : undefined}
                              onMouseEnter={() => setActive(i)} onClick={() => choose(s)}
                              className={`flex cursor-pointer items-center gap-3 px-3 py-2 ${i === active ? 'bg-subtle' : ''}`}>
                              <span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-subtle">
                                <Img src={s.iconUrl} alt="" className="icon-boost h-5 w-5" rounded="rounded" fit="contain" fallbackText={s.label} />
                              </span>
                              <span className="min-w-0 flex-1">
                                <span className="block truncate text-sm text-ink-2"><Highlight text={s.label} term={term} /></span>
                                <span className="block truncate text-xs text-muted">{s.detail}</span>
                              </span>
                              <span className="shrink-0 text-[11px] text-faint">{s.kind === 'category' ? 'Jump to' : 'View results'}</span>
                            </li>
                          ))}
                        </ul>
                      </li>
                    );
                  })}
                </ul>
                <div className="flex items-center justify-between gap-3 border-t border-line bg-subtle/60 px-3 py-2 text-xs text-muted">
                  <span>{pluralize(items.length, 'result')} for “{value.trim()}”</span>
                  <span className="hidden sm:inline">↑ ↓ to move · Enter to open · Esc to close</span>
                </div>
              </>
            )}
          </div>
        )}
      </div>
      <Button type="submit" variant="contained" color="secondary" startIcon={<SearchRounded />} sx={{ flexShrink: 0, px: 2.5, height: 44 }}>Search</Button>
    </form>
  );
}

/** A service as a compact tile: tinted icon, name, how many businesses (or "Coming soon"), arrow. */
function ServiceTile({ s }: { s: SubCategory }) {
  const tint = `${s.colorHex ?? '#667085'}1a`;
  return (
    <Link to={`/search?sub=${s.slug}`}
      className="group flex items-center gap-3 rounded-xl border border-line bg-surface p-3 outline-offset-2 transition-[border-color,box-shadow] duration-200 hover:border-line-strong hover:shadow-[var(--cb-shadow-md)] focus-visible:outline-2 focus-visible:outline-accent">
      <span className="grid h-10 w-10 shrink-0 place-items-center rounded-lg" style={{ background: tint }}>
        <Img src={s.iconUrl} alt="" className="icon-boost h-5 w-5" rounded="rounded" fit="contain" fallbackText={s.name} />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-sm font-semibold text-ink">{s.name}</span>
        {s.businessCount > 0
          ? <span className="block text-xs text-muted">{pluralize(s.businessCount, 'business', 'businesses')}</span>
          : <span className="mt-0.5 inline-block rounded bg-subtle px-1.5 py-px text-[11px] font-medium text-muted">Coming soon</span>}
      </span>
      <ChevronRightRounded fontSize="small" className="shrink-0 text-faint transition-colors group-hover:text-accent-ink" />
    </Link>
  );
}

/** Highlights the category section currently in view (for the sidebar). */
function useActiveSection(slugs: string[]) {
  const [active, setActive] = useState<string | null>(null);
  useEffect(() => {
    if (!slugs.length) return;
    const observer = new IntersectionObserver((entries) => {
      const visible = entries.filter((e) => e.isIntersecting).sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top);
      if (visible[0]) setActive(visible[0].target.id.replace(/^cat-/, ''));
    }, { rootMargin: '-20% 0px -65% 0px' });
    slugs.forEach((slug) => { const el = document.getElementById(`cat-${slug}`); if (el) observer.observe(el); });
    return () => observer.disconnect();
  }, [slugs]);
  return active;
}

export default function CategoriesPage() {
  const { slug } = useParams();
  const { data, isLoading, isError, refetch } = useCategories();
  const [q, setQ] = useState('');
  const term = q.trim().toLowerCase();
  const single = slug ? data?.find((c) => c.slug === slug) : undefined;
  // Below the search: every category, or while searching only the related ones (matching categories, or matching sub-categories).
  const list = useMemo(() => (slug ? (single ? [single] : []) : filterTree(data ?? [], term)), [data, slug, single, term]);
  // Second marquee row: sub-categories with listings first (most listed first), then the rest.
  const subs = useMemo(() => (data ?? []).flatMap((c) => c.subCategories)
    .sort((a, b) => Number(b.businessCount > 0) - Number(a.businessCount > 0) || b.businessCount - a.businessCount), [data]);
  // Placeholder ticker: categories interleaved with the most listed sub-categories.
  const tickerNames = useMemo(() => {
    const cats = (data ?? []).map((c) => c.name);
    const top = subs.map((s) => s.name);
    return Array.from({ length: Math.max(cats.length, top.length) }, (_, i) => [cats[i], top[i]]).flat().filter((n): n is string => !!n);
  }, [data, subs]);
  const matchedCats = useMemo(() => (data ?? []).filter((c) => matches(c.name, term)), [data, term]);
  const matchedSubs = useMemo(() => subs.filter((s) => matches(s.name, term)), [subs, term]);
  const totals = useMemo(() => ({
    services: data?.reduce((n, c) => n + c.subCategories.length, 0) ?? 0,
    businesses: data?.reduce((n, c) => n + c.businessCount, 0) ?? 0,
  }), [data]);
  const sectionSlugs = useMemo(() => (slug ? [] : list.map((c) => c.slug)), [list, slug]);
  const activeSlug = useActiveSection(sectionSlugs);
  useDocumentTitle(single?.name ?? 'All categories');

  if (isError) return <div className="container-page py-16"><ErrorState onRetry={() => refetch()} /></div>;
  if (slug && data && !single) {
    return (
      <div className="container-page py-16">
        <EmptyState title="Category not found" message="This category may have been renamed or retired."
          action={<Button component={Link} to="/categories" variant="contained">Browse all categories</Button>} />
      </div>
    );
  }

  // ---------- Single category: banner + image cards ----------
  if (slug) {
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
        {single && <PageHeader headingLevel="h2" title={pluralize(single.businessCount, 'business', 'businesses')} crumbs={[{ label: 'Home', to: '/' }, { label: 'Categories', to: '/categories' }, { label: single.name }]}
          actions={<Button component={Link} to={`/nearby?category=${single.slug}`} variant="outlined" startIcon={<TravelExploreRounded />}>Explore nearby on Google Maps</Button>} />}
        {isLoading || !single ? <Skeleton variant="rounded" height={260} /> : (
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
            {single.subCategories.map((s) => (
              <Link key={s.id} to={`/search?sub=${s.slug}`} className="group card overflow-hidden">
                <Img src={s.imageUrl} alt={s.altText ?? s.name} aspect="16/10" rounded="rounded-none" className="w-full" fallbackText={s.name} />
                <div className="p-3">
                  <div className="text-sm font-semibold group-hover:underline">{s.name}</div>
                  <div className="text-xs text-muted">{s.businessCount === 0 ? 'Coming soon' : pluralize(s.businessCount, 'business', 'businesses')}</div>
                </div>
              </Link>
            ))}
          </div>
        )}
      </div>
    );
  }

  // ---------- All categories ----------
  const stats: [ReactNode, string, number][] = [
    [<CategoryOutlined key="c" fontSize="small" />, 'categories', data?.length ?? 0],
    [<HandymanOutlined key="s" fontSize="small" />, 'services', totals.services],
    [<VerifiedOutlined key="b" fontSize="small" />, 'verified businesses', totals.businesses],
  ];

  return (
    <div className="container-page py-6 md:py-8">
      {/* Header panel: breadcrumb, title, search, headline numbers */}
      {/* Not overflow-hidden itself, so the search suggestions can extend below the panel; only the decorative glow is clipped. */}
      <section className="relative z-[35] mb-6 rounded-2xl border border-line bg-surface px-5 py-7 md:px-10 md:py-10">
        <span aria-hidden className="pointer-events-none absolute inset-0 overflow-hidden rounded-2xl">
          <span className="absolute -right-24 -top-24 h-72 w-72 rounded-full bg-accent/10 blur-3xl" />
        </span>
        <nav aria-label="Breadcrumb" className="relative text-[13px] text-muted">
          <Link to="/" className="hover:text-ink">Home</Link><span className="mx-1.5 text-faint">/</span><span className="text-ink-2">Categories</span>
        </nav>
        <h1 className="relative mt-2 text-3xl font-bold tracking-[-0.02em] md:text-4xl">Browse categories</h1>
        <p className="relative mt-2 max-w-2xl text-[15px] text-muted md:text-base">
          Every local service you need, from verified professionals near you. Search by name or explore by category.
        </p>
        <div className="relative mt-6"><CategorySearch value={q} onChange={setQ} categories={data ?? []} subs={subs} names={tickerNames} /></div>
        <ul aria-label="At a glance" className="relative mt-6 flex flex-wrap gap-2">
          {stats.map(([icon, label, value]) => (
            <li key={label} className="inline-flex items-center gap-2 rounded-full border border-line bg-canvas px-3 py-1.5 text-sm">
              <span className="text-accent-ink">{icon}</span>
              <span className="font-semibold tabular text-ink">{data ? number(value) : '–'}</span>
              <span className="text-muted">{label}</span>
            </li>
          ))}
        </ul>
      </section>

      {data && (
        // Marquee: categories (jump to a section) and popular services (open results). Sticky on phones and tablets, where it is the
        // navigation; on desktop the sidebar takes that role.
        <div className="sticky top-[72px] z-30 -mx-4 mb-8 space-y-2 border-y border-line bg-canvas/95 px-4 py-3 backdrop-blur md:-mx-6 md:px-6 lg:static lg:mx-0 lg:rounded-xl lg:border lg:bg-surface lg:px-4">
          {term ? (
            // Searching: matching categories (jump to section) and sub-categories (open results), standing still.
            <div role="region" aria-label="Matching categories" aria-live="polite" className="flex max-h-40 flex-wrap gap-2 overflow-y-auto">
              {matchedCats.map((c) => (
                <a key={c.id} href={`#cat-${c.slug}`} className={chip}>
                  <Img src={c.iconUrl} alt="" className="icon-boost h-5 w-5 p-0.5" rounded="rounded-full" fit="contain" fallbackText={c.name} />
                  {c.name}
                </a>
              ))}
              {matchedSubs.map((s) => (
                <Link key={s.id} to={`/search?sub=${s.slug}`} className={chip}>
                  <span aria-hidden className="h-1.5 w-1.5 shrink-0 rounded-full" style={{ background: s.colorHex ?? 'currentColor' }} />
                  {s.name}
                </Link>
              ))}
              {matchedCats.length + matchedSubs.length === 0 && (
                <p className="py-1 text-sm text-muted">
                  No categories match “{q.trim()}”. <button type="button" onClick={() => setQ('')} className="font-semibold text-ink-2 hover:text-accent-ink">Show all</button>
                </p>
              )}
            </div>
          ) : (
            <>
              <Marquee label="Jump to a category" itemCount={data.length} secondsPerItem={3}>
                {data.map((c) => (
                  <a key={c.id} href={`#cat-${c.slug}`} className={chip}>
                    <Img src={c.iconUrl} alt="" className="icon-boost h-5 w-5 p-0.5" rounded="rounded-full" fit="contain" fallbackText={c.name} />
                    {c.name}
                  </a>
                ))}
              </Marquee>
              <Marquee label="Popular services" itemCount={subs.length} secondsPerItem={2.2} reverse>
                {subs.map((s) => (
                  <Link key={s.id} to={`/search?sub=${s.slug}`} className={chip}>
                    <span aria-hidden className="h-1.5 w-1.5 shrink-0 rounded-full" style={{ background: s.colorHex ?? 'currentColor' }} />
                    {s.name}
                  </Link>
                ))}
              </Marquee>
            </>
          )}
        </div>
      )}

      <div className="grid grid-cols-1 gap-8 lg:grid-cols-[248px_minmax(0,1fr)]">
        {/* Desktop sidebar: every (matching) category, highlighting the one in view */}
        <aside className="hidden lg:block">
          <nav aria-label="Categories" className="sticky top-24 max-h-[calc(100vh-7rem)] overflow-y-auto rounded-xl border border-line bg-surface p-2 [scrollbar-width:thin]">
            <p className="px-2.5 pb-1.5 pt-1 text-[11px] font-semibold uppercase tracking-[0.08em] text-muted">
              {term ? `${list.length} matching` : 'All categories'}
            </p>
            {isLoading ? Array.from({ length: 10 }, (_, i) => <Skeleton key={i} variant="rounded" height={34} sx={{ my: 0.5 }} />) : list.map((c) => {
              const current = c.slug === activeSlug;
              return (
                <a key={c.id} href={`#cat-${c.slug}`} aria-current={current ? 'true' : undefined}
                  className={`flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-[13px] transition-colors ${current ? 'bg-subtle font-semibold text-ink' : 'text-ink-2 hover:bg-subtle hover:text-ink'}`}>
                  <span className="grid h-6 w-6 shrink-0 place-items-center rounded-md" style={{ background: `${c.colorHex ?? '#667085'}1a` }}>
                    <Img src={c.iconUrl} alt="" className="icon-boost h-4 w-4" rounded="rounded" fit="contain" fallbackText={c.name} />
                  </span>
                  <span className="min-w-0 flex-1 truncate">{c.name}</span>
                  <span className="shrink-0 text-xs tabular text-faint">{c.subCategories.length}</span>
                </a>
              );
            })}
          </nav>
        </aside>

        <div id="category-results" className="min-w-0 scroll-mt-44">
          {term && list.length > 0 && (
            <p className="mb-5 text-sm text-muted" aria-live="polite">Showing {pluralize(list.length, 'category', 'categories')} related to “{q.trim()}”</p>
          )}
          <div className="space-y-6">
            {isLoading ? Array.from({ length: 3 }, (_, i) => <Skeleton key={i} variant="rounded" height={220} sx={{ borderRadius: '16px' }} />) : list.length === 0 ? (
              <EmptyState title={`No categories match “${q.trim()}”`} message="Try a broader word such as repair, doctor or classes."
                action={<Button variant="outlined" onClick={() => setQ('')}>Show all categories</Button>} />
            ) : list.map((c) => (
              <section key={c.id} id={`cat-${c.slug}`} aria-labelledby={`cat-h-${c.slug}`}
                className="scroll-mt-44 rounded-2xl border border-line bg-surface p-4 md:p-6 lg:scroll-mt-24">
                <header className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center">
                  <div className="flex min-w-0 flex-1 items-center gap-3.5">
                    <span className="grid h-12 w-12 shrink-0 place-items-center rounded-xl" style={{ background: `${c.colorHex ?? '#667085'}1a` }}>
                      <Img src={c.iconUrl} alt="" className="icon-boost h-7 w-7" rounded="rounded" fit="contain" fallbackText={c.name} />
                    </span>
                    <div className="min-w-0">
                      <h2 id={`cat-h-${c.slug}`} className="text-lg font-bold tracking-[-0.01em] md:text-xl">
                        <Link to={`/categories/${c.slug}`} className="hover:underline">{c.name}</Link>
                      </h2>
                      <p className="line-clamp-2 text-sm text-muted">{c.description}</p>
                    </div>
                  </div>
                  <div className="flex shrink-0 flex-wrap gap-2 self-start sm:self-center">
                    <Button component={Link} to={`/nearby?category=${c.slug}`} size="small" startIcon={<TravelExploreRounded />}>
                      Explore nearby
                    </Button>
                    <Button component={Link} to={`/search?category=${c.slug}`} variant="outlined" size="small" endIcon={<ArrowForwardRounded />}>
                      View all · {number(c.businessCount)}
                    </Button>
                  </div>
                </header>
                <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2 xl:grid-cols-3">
                  {c.subCategories.map((s) => <ServiceTile key={s.id} s={s} />)}
                </div>
              </section>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
