import { Link } from 'react-router';

/**
 * The legal pages. Their text is in the database (MarketingContent, PageKey "Legal-…"), with company details filled in from the
 * legal settings that admins edit; this is only the list of pages and where they live.
 */
export const POLICIES = {
  terms: { label: 'Terms and Conditions', short: 'Terms', path: '/terms', pageKey: 'Legal-Terms' },
  privacy: { label: 'Privacy Policy', short: 'Privacy', path: '/privacy', pageKey: 'Legal-Privacy' },
  cancellation: { label: 'Cancellation Policy', short: 'Cancellation', path: '/cancellation-policy', pageKey: 'Legal-Cancellation' },
  refund: { label: 'Refund Policy', short: 'Refunds', path: '/refund-policy', pageKey: 'Legal-Refund' },
} as const;

export type PolicyKey = keyof typeof POLICIES;
export const POLICY_KEYS = Object.keys(POLICIES) as PolicyKey[];

/**
 * A link to a policy. In the middle of a task (checkout, booking, sign-up) it opens in a new tab, so reading it never loses what the
 * person has entered.
 */
export function PolicyLink({ policy, newTab = true, className }: { policy: PolicyKey; newTab?: boolean; className?: string }) {
  const p = POLICIES[policy];
  return (
    <Link to={p.path} target={newTab ? '_blank' : undefined} rel={newTab ? 'noopener' : undefined}
      className={className ?? 'font-medium text-ink underline underline-offset-2 hover:text-accent-ink'}>
      {p.label}
    </Link>
  );
}

/** "the Terms and Conditions, Privacy Policy and Refund Policy": the named policies as links, joined into a sentence. */
export function PolicyList({ policies }: { policies: PolicyKey[] }) {
  return (
    <>
      the{' '}
      {policies.map((key, i) => (
        <span key={key}>
          {i > 0 && (i === policies.length - 1 ? ' and ' : ', ')}
          <PolicyLink policy={key} />
        </span>
      ))}
    </>
  );
}
