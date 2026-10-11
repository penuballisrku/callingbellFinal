import googleMapsLogoGray from '@/assets/google-maps/GoogleMaps_Logo_Gray.svg';
import googleMapsLogoWhite from '@/assets/google-maps/GoogleMaps_Logo_White.svg';

/**
 * "Google Maps", as Google's Places API policies require wherever Places data (names, addresses, ratings, phone numbers, photos) is
 * shown without a Google map. The text form, for tight spots such as a result's source tag; the look is fixed by the
 * `.google-maps-attribution` class in index.css; don't restyle it.
 */
export function GoogleMapsAttribution({ className }: { className?: string }) {
  return <span translate="no" className={`google-maps-attribution ${className ?? ''}`}>Google Maps</span>;
}

/**
 * "Data provided by" and the Google Maps logo, Google's preferred attribution, below results, lists and details that show Google Maps
 * data. The logo is Google's own artwork, unmodified (src/assets/google-maps, from Google's attribution assets): grey on light
 * backgrounds, white on dark, 18px tall (Google allows 16-19px) with its required clear space (10px left, right and top, 5px below).
 */
export function GoogleMapsDataCredit({ className }: { className?: string }) {
  return (
    <span className={`inline-flex items-center ${className ?? ''}`}>
      <span>Data provided by</span>
      <span className="inline-flex shrink-0 px-[10px] pb-[5px] pt-[10px]">
        <img src={googleMapsLogoGray} alt="Google Maps" width={98} height={18} className="block h-[18px] w-[98px] max-w-none dark:hidden" />
        <img src={googleMapsLogoWhite} alt="Google Maps" width={98} height={18} className="hidden h-[18px] w-[98px] max-w-none dark:block" />
      </span>
    </span>
  );
}
