import { useEffect, useState } from 'react';

/**
 * Animated placeholder for a search box: cycles "Search for <name>" through `names` while the input is empty. Render it inside the
 * input's positioned wrapper; it is decorative (aria-hidden), so give the input its own aria-label.
 */
export function PlaceholderTicker({ names, prefix = 'Search for', className = 'text-[15px]' }: { names: string[]; prefix?: string; className?: string }) {
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
      <span key={i} className="ticker-in truncate font-medium text-muted">{names[i % names.length]}</span>
    </span>
  );
}
