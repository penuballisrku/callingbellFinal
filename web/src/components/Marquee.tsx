import type { CSSProperties, ReactNode } from 'react';

/**
 * Continuously scrolling row (marquee). The items are rendered twice and the track moves by exactly one copy's width, so the
 * loop is seamless; the duplicate is hidden from assistive technology and keyboard focus. Pauses on hover or keyboard focus,
 * and becomes a normal swipeable row when the user prefers reduced motion. Styles: `.marquee*` in index.css.
 */
export function Marquee({ children, label, secondsPerItem = 2.5, itemCount, reverse = false }: {
  children: ReactNode;
  label: string;
  /** Speed: seconds of travel per item, so long and short lists move at the same pace. */
  secondsPerItem?: number;
  itemCount: number;
  reverse?: boolean;
}) {
  const style = { '--marquee-duration': `${Math.max(20, itemCount * secondsPerItem)}s` } as CSSProperties;
  return (
    <div className="marquee" role="region" aria-label={label}>
      <div className="marquee-track" style={style} data-reverse={reverse || undefined}>
        <div className="marquee-group">{children}</div>
        <div className="marquee-group" aria-hidden="true" inert>{children}</div>
      </div>
    </div>
  );
}
