import { Link } from 'react-router';

/** The bell, drawn on a 40×40 grid (scaled into the tile by {@link LogoMark}). */
const BELL = 'M20 9.6c-4.5 0-7.5 3.4-7.5 7.7v4.8l-2.1 3.3c-.55.86.07 1.95 1.08 1.95h17.04c1.01 0 1.63-1.09 1.08-1.95l-2.1-3.3v-4.8c0-4.3-3-7.7-7.5-7.7Z';

/**
 * CallInBell brand mark: a ringing bell (amber clapper, signal waves on both sides): someone calling in, answered in real time.
 * The same drawing is in public/favicon.svg and the PNG app icons (public/icons). `tone="onDark"` is for navy surfaces (hero,
 * sidebar, footer); the default adapts to light/dark mode.
 */
export function LogoMark({ size = 32, tone = 'auto' }: { size?: number; tone?: 'auto' | 'onDark' }) {
  const tile = tone === 'onDark' ? '#F4A62C' : 'var(--cb-inverse)';
  const bell = tone === 'onDark' ? '#0B1220' : 'var(--cb-on-inverse)';
  const ring = tone === 'onDark' ? '#0B1220' : '#F4A62C';
  return (
    <svg width={size} height={size} viewBox="0 0 40 40" aria-hidden focusable="false">
      <rect width="40" height="40" rx="11" fill={tile} />
      <g transform="translate(20 20) scale(0.78) translate(-20 -19.6)">
        <rect x="18.9" y="7.2" width="2.2" height="3.4" rx="1.1" fill={bell} />
        <path d={BELL} fill={bell} />
        <path d="M16.7 29.2h6.6a3.3 3.3 0 0 1-6.6 0Z" fill={ring} />
      </g>
      <g stroke={ring} strokeWidth="2.1" strokeLinecap="round" fill="none">
        <path d="M30.6 13.6a10.6 10.6 0 0 1 0 11.2" />
        <path d="M9.4 13.6a10.6 10.6 0 0 0 0 11.2" />
        <path d="M34.2 11.2a14.6 14.6 0 0 1 0 16" opacity="0.5" />
        <path d="M5.8 11.2a14.6 14.6 0 0 0 0 16" opacity="0.5" />
      </g>
    </svg>
  );
}

/** Mark + wordmark. "In" is amber: it is what makes "CallInBell" read as Call·In·Bell (a capital I looks like an l in Inter). */
export function Logo({ tone = 'auto', size = 32, to = '/' }: { tone?: 'auto' | 'onDark'; size?: number; to?: string }) {
  return (
    <Link to={to} className="flex items-center gap-2.5 rounded-lg" aria-label="CallInBell home">
      <LogoMark size={size} tone={tone} />
      <span className={`text-[18px] font-extrabold leading-none tracking-[-0.035em] ${tone === 'onDark' ? 'text-white' : 'text-ink'}`}>
        Call<span style={{ color: tone === 'onDark' ? '#F4A62C' : 'var(--cb-brand-in)' }}>In</span>Bell
      </span>
    </Link>
  );
}
