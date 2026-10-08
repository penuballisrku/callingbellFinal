/** tel: link for a phone number as written ("098480 12345", "+91 98480 12345"). */
export const telHref = (phone: string) => `tel:${phone.replace(/[^\d+]/g, '')}`;

/**
 * WhatsApp chat link for a phone number, or null when it isn't a mobile number (WhatsApp needs one). Indian numbers are written many
 * ways ("098480 12345", "+91 98480 12345", "9848012345"); they are reduced to the 10-digit mobile number with the 91 country code.
 */
export function whatsAppHref(phone: string | null | undefined, name: string): string | null {
  if (!phone) return null;
  let digits = phone.replace(/\D/g, '');
  // Other countries, written with their country code ("+1 416-555-0142"): mobiles can't be told apart, so any full number is offered.
  if (phone.trim().startsWith('+') && !digits.startsWith('91')) {
    if (digits.length < 8 || digits.length > 15) return null;
    return `https://wa.me/${digits}?text=${encodeURIComponent(waText(name))}`;
  }
  if (digits.length === 12 && digits.startsWith('91')) digits = digits.slice(2);
  else if (digits.length === 11 && digits.startsWith('0')) digits = digits.slice(1);
  if (!/^[6-9]\d{9}$/.test(digits)) return null;
  return `https://wa.me/91${digits}?text=${encodeURIComponent(waText(name))}`;
}

const waText = (name: string) => `Hi ${name}, I found your business on Calling Bell and would like to know more about your services.`;

/** Google Maps directions to a point, or to a name and address when there are no coordinates. */
export function directionsHref(lat?: number | null, lng?: number | null, query?: string | null): string | null {
  if (lat != null && lng != null) return `https://www.google.com/maps/dir/?api=1&destination=${lat},${lng}`;
  return query ? `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(query)}` : null;
}

/** Business sign-up, pre-filled with what is known about the place (the owner can change everything). */
export function joinHref(p: { name: string; phone?: string | null; website?: string | null; address?: string | null }): string {
  const params = new URLSearchParams({ type: 'business', name: p.name });
  if (p.phone) params.set('phone', p.phone);
  if (p.website) params.set('website', p.website);
  if (p.address) params.set('address', p.address);
  return `/register?${params}`;
}
