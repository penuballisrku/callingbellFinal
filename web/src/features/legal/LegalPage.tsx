import { Fragment, type ReactNode } from 'react';
import { Link } from 'react-router';
import { Skeleton } from '@mui/material';
import GavelRounded from '@mui/icons-material/GavelRounded';
import MailOutlineRounded from '@mui/icons-material/MailOutlineRounded';
import PhoneOutlined from '@mui/icons-material/PhoneOutlined';
import PlaceOutlined from '@mui/icons-material/PlaceOutlined';
import SupportAgentOutlined from '@mui/icons-material/SupportAgentOutlined';
import { useDocumentTitle } from '@/lib/hooks';
import { POLICIES, POLICY_KEYS, type PolicyKey } from '@/lib/legal';
import type { ContentBlock } from '@/lib/types';
import { ErrorState } from '@/components/ui';
import { useMarketingPage } from '@/features/content/ContentBlocks';

/**
 * A legal page: Terms and Conditions, Privacy, Cancellation or Refund policy. Every word comes from the database (MarketingContent,
 * PageKey "Legal-…"); company details (legal name, address, customer care, Grievance Officer) are filled in by the API from the legal
 * settings admins edit, and the ones that are set also arrive as "Setting" blocks for the contact card.
 */
export default function LegalPage({ policy }: { policy: PolicyKey }) {
  const meta = POLICIES[policy];
  useDocumentTitle(meta.label);
  const { data, isLoading, isError, refetch } = useMarketingPage(meta.pageKey);

  if (isError) return <div className="container-page py-16"><ErrorState onRetry={() => refetch()} /></div>;

  const blocks = data?.blocks ?? [];
  const intro = blocks.find((b) => b.section === 'Intro');
  const sections = blocks.filter((b) => b.section === 'Section');
  const setting = (key: string) => blocks.find((b) => b.section === 'Setting' && b.code === key)?.title;
  const effective = setting('EffectiveDate');

  return (
    <div className="bg-canvas">
      <header className="border-b border-line bg-surface">
        <div className="container-page py-8 md:py-10">
          <nav aria-label="Breadcrumb" className="text-sm text-muted">
            <Link to="/" className="hover:text-ink">Home</Link><span className="mx-1.5">/</span><span className="text-ink">{meta.label}</span>
          </nav>
          <p className="mt-5 inline-flex items-center gap-1.5 text-xs font-semibold uppercase tracking-[0.12em] text-accent-ink">
            <GavelRounded sx={{ fontSize: 15 }} />Legal
          </p>
          {isLoading ? <Skeleton width={320} height={48} /> : <h1 className="mt-2 text-3xl font-bold tracking-tight md:text-4xl">{intro?.title ?? meta.label}</h1>}
          {intro?.subtitle && <p className="mt-3 max-w-3xl text-base text-muted">{intro.subtitle}</p>}
          {effective && <p className="mt-3 text-sm text-ink-2">Effective <span className="font-semibold">{effective}</span></p>}

          {/* The four policies, one tap apart. */}
          <nav aria-label="Policies" className="-mx-4 mt-6 flex gap-2 overflow-x-auto px-4 pb-1 [scrollbar-width:none] sm:mx-0 sm:flex-wrap sm:px-0">
            {POLICY_KEYS.map((key) => {
              const p = POLICIES[key];
              const current = key === policy;
              return (
                <Link key={key} to={p.path} aria-current={current ? 'page' : undefined}
                  className={`shrink-0 rounded-full border px-3.5 py-1.5 text-sm font-medium transition-colors ${current
                    ? 'border-transparent bg-inverse text-on-inverse' : 'border-line bg-surface text-ink-2 hover:border-line-strong hover:text-ink'}`}>
                  {p.label}
                </Link>
              );
            })}
          </nav>
        </div>
      </header>

      <div className="container-page grid gap-8 py-8 md:py-10 lg:grid-cols-[240px_minmax(0,1fr)] lg:gap-12">
        {/* Contents: a sticky list beside the text on desktop; a compact list above it on smaller screens. */}
        <aside className="lg:sticky lg:top-24 lg:self-start">
          {sections.length > 0 && (
            <nav aria-label="On this page" className="card p-4">
              <p className="text-xs font-semibold uppercase tracking-[0.08em] text-muted">On this page</p>
              <ol className="mt-2 space-y-1 text-sm">
                {sections.map((s) => (
                  <li key={s.code}><a href={`#${s.eyebrow ?? s.code}`} className="block rounded px-1 py-1 text-ink-2 hover:bg-subtle hover:text-ink">{s.title}</a></li>
                ))}
              </ol>
            </nav>
          )}
        </aside>

        <article className="min-w-0">
          <div className="card divide-y divide-line">
            {isLoading
              ? Array.from({ length: 4 }, (_, i) => <div key={i} className="space-y-2 p-6"><Skeleton width="40%" height={28} /><Skeleton /><Skeleton /><Skeleton width="80%" /></div>)
              : sections.map((s) => (
                <section key={s.code} id={s.eyebrow ?? s.code} className="scroll-mt-24 p-5 md:p-7">
                  <h2 className="text-lg font-semibold tracking-tight md:text-xl">{s.title}</h2>
                  <div className="mt-3 space-y-3 text-[15px] leading-7 text-ink-2">{renderBody(s.body)}</div>
                </section>
              ))}
          </div>

          {!isLoading && <ContactCard blocks={blocks} />}
          <p className="mt-6 text-sm text-muted">
            Questions about this policy? <Link to="/support" className="font-medium text-ink underline underline-offset-2">Contact support</Link>.
          </p>
        </article>
      </div>
    </div>
  );
}

const NUMBERED = /^(\d+)\.\s+/;

/**
 * Paragraphs; consecutive "- " lines as a bulleted list and "1. " lines as a numbered list; a "### " line as a sub-heading
 * (e.g. "11.1 Cancellation of Business Services").
 */
function renderBody(body?: string | null): ReactNode {
  const out: ReactNode[] = [];
  // The list being collected (in a holder, so the closure below and the loop share it).
  const open: { list: { kind: 'ul' | 'ol'; items: string[]; start: number } | null } = { list: null };
  const flush = () => {
    const list = open.list;
    if (!list) return;
    const items = list.items.map((l, i) => <li key={i}>{l}</li>);
    out.push(list.kind === 'ul'
      ? <ul key={`l${out.length}`} className="list-disc space-y-1.5 pl-5 marker:text-faint">{items}</ul>
      : <ol key={`l${out.length}`} start={list.start} className="list-decimal space-y-1.5 pl-6 marker:text-muted">{items}</ol>);
    open.list = null;
  };
  for (const raw of (body ?? '').split('\n')) {
    const line = raw.trim();
    if (!line) continue;
    const numbered = NUMBERED.exec(line);
    const kind = line.startsWith('- ') ? 'ul' : numbered ? 'ol' : null;
    if (kind) {
      if (open.list?.kind !== kind) { flush(); open.list = { kind, items: [], start: numbered ? Number(numbered[1]) : 1 }; }
      open.list!.items.push(numbered ? line.slice(numbered[0].length) : line.slice(2));
      continue;
    }
    flush();
    out.push(line.startsWith('### ')
      ? <h3 key={`h${out.length}`} className="pt-2 text-base font-semibold text-ink">{line.slice(4)}</h3>
      : <p key={`p${out.length}`}>{line}</p>);
  }
  flush();
  return out.map((node, i) => <Fragment key={i}>{node}</Fragment>);
}

/** The operator and Grievance Officer, from the legal settings; only details that are filled in are shown. */
function ContactCard({ blocks }: { blocks: ContentBlock[] }) {
  const v = (key: string) => blocks.find((b) => b.section === 'Setting' && b.code === key)?.title?.trim() || null;
  const company = [v('LegalName'), v('RegisteredAddress')].filter(Boolean).join(', ');
  const rows: { icon: ReactNode; label: string; value: ReactNode }[] = [];
  if (company) rows.push({ icon: <PlaceOutlined fontSize="small" />, label: 'Operated by', value: company });
  if (v('SupportEmail')) rows.push({ icon: <MailOutlineRounded fontSize="small" />, label: 'Customer care', value: <a href={`mailto:${v('SupportEmail')}`} className="hover:underline">{v('SupportEmail')}</a> });
  if (v('SupportPhone')) rows.push({ icon: <PhoneOutlined fontSize="small" />, label: 'Phone', value: <a href={`tel:${v('SupportPhone')!.replace(/[^\d+]/g, '')}`} className="hover:underline">{v('SupportPhone')}</a> });
  const officer = [v('GrievanceOfficer'), v('GrievanceEmail'), v('GrievancePhone')].filter(Boolean);
  if (officer.length) {
    rows.push({
      icon: <SupportAgentOutlined fontSize="small" />, label: 'Grievance Officer',
      value: <>
        {v('GrievanceOfficer') && <span className="block">{v('GrievanceOfficer')}</span>}
        {v('GrievanceEmail') && <a href={`mailto:${v('GrievanceEmail')}`} className="block hover:underline">{v('GrievanceEmail')}</a>}
        {v('GrievancePhone') && <a href={`tel:${v('GrievancePhone')!.replace(/[^\d+]/g, '')}`} className="block hover:underline">{v('GrievancePhone')}</a>}
      </>,
    });
  }
  if (!rows.length) return null;
  return (
    <section id="contact-details" aria-labelledby="contact-details-h" className="card mt-6 p-5 md:p-7">
      <h2 id="contact-details-h" className="text-lg font-semibold tracking-tight">Contact and grievance redressal</h2>
      <dl className="mt-4 grid gap-4 sm:grid-cols-2">
        {rows.map((r) => (
          <div key={r.label} className="flex gap-3">
            <span className="mt-0.5 text-muted" aria-hidden>{r.icon}</span>
            <div className="min-w-0">
              <dt className="text-xs font-semibold uppercase tracking-[0.06em] text-muted">{r.label}</dt>
              <dd className="mt-0.5 break-words text-sm text-ink">{r.value}</dd>
            </div>
          </div>
        ))}
      </dl>
    </section>
  );
}
