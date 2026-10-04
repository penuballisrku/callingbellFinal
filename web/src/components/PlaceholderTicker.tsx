import { useEffect, useState } from 'react';

/**
 * Animated placeholder for a search box: cycles "Search for <name>" through `names` while the input is empty. Render it inside the
 * input's positioned wrapper; it is decorative (aria-hidden), so give the input its own aria-label.
 */
export function PlaceholderTicker({ names, prefix = 'Search for', suffix, className = 'text-[15px]' }: {
  names: string[]; prefix?: string; className?: string;
  /** Fixed text after the name, e.g. "in Hyderabad" (a space is added before it). */
  suffix?: string;
}) {
  const [i, setI] = useState(0);
  useEffect(() => {
    if (names.length < 2) return;
    const t = setInterval(() => setI((n) => (n + 1) % names.length), 2500);
    return () => clearInterval(t);
  }, [names.length]);
  if (names.length === 0) return null;
  return (
    <span aria-hidden className={`pointer-events-none absolute inset-0 flex items-center overflow-hidden whitespace-nowrap text-faint ${className}`}>
      {prefix}&nbsp;
      <span key={i} className={`ticker-in font-medium text-muted ${suffix ? 'shrink-0' : 'truncate'}`}>{names[i % names.length]}</span>
      {suffix && <span className="truncate">&nbsp;{suffix.trim()}</span>}
    </span>
  );
}
