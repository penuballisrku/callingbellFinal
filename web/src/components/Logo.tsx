import { Link } from 'react-router';

/**
 * Calling Bell brand mark: a geometric bell with "live" signal arcs - the real-time availability network.
 * `tone="onDark"` is for navy surfaces (hero, sidebar, footer); the default adapts to light/dark mode.
 */
export function LogoMark({ size = 32, tone = 'auto' }: { size?: number; tone?: 'auto' | 'onDark' }) {
  const tile = tone === 'onDark' ? '#F4A62C' : 'var(--cb-inverse)';
  const bell = tone === 'onDark' ? '#0B1220' : 'var(--cb-on-inverse)';
  const signal = tone === 'onDark' ? '#0B1220' : '#F4A62C';
  return (
    <svg width={size} height={size} viewBox="0 0 40 40" aria-hidden focusable="false">
      <rect width="40" height="40" rx="11" fill={tile} />
      <g transform="translate(-0.4 1.2) scale(0.92)">
      <rect x="18.9" y="7.2" width="2.2" height="3.4" rx="1.1" fill={bell} />
      <path d="M20 9.6c-4.5 0-7.5 3.4-7.5 7.7v4.8l-2.1 3.3c-.55.86.07 1.95 1.08 1.95h17.04c1.01 0 1.63-1.09 1.08-1.95l-2.1-3.3v-4.8c0-4.3-3-7.7-7.5-7.7Z" fill={bell} />
      <path d="M16.7 29.2h6.6a3.3 3.3 0 0 1-6.6 0Z" fill={bell} />
      </g>
      <path d="M26.6 11.2a7 7 0 0 1 2.7 4.6" stroke={signal} strokeWidth="2" strokeLinecap="round" fill="none" />
      <path d="M29.4 8.2a10.8 10.8 0 0 1 3.9 7" stroke={signal} strokeWidth="2" strokeLinecap="round" fill="none" opacity="0.55" />
    </svg>
  );
}

export function Logo({ tone = 'auto', size = 32, to = '/' }: { tone?: 'auto' | 'onDark'; size?: number; to?: string }) {
  return (
    <Link to={to} className="flex items-center gap-2.5 rounded-lg" aria-label="Calling Bell home">
      <LogoMark size={size} tone={tone} />
      <span className={`text-[17px] font-bold leading-none tracking-[-0.03em] ${tone === 'onDark' ? 'text-white' : 'text-ink'}`}>
        Calling<span className="font-semibold tracking-[-0.02em]"> Bell</span>
      </span>
    </Link>
  );
}
