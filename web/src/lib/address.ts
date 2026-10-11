/**
 * A business's address in one line: street, area, city, state and pincode, leaving out any part an earlier one already holds
 * ("Gokhale Road, Dadar West" + "Dadar West").
 */
export function fullAddress(p: { addressLine?: string | null; area?: string | null; city?: string | null; state?: string | null; pincode?: string | null }) {
  return [p.addressLine, p.area, p.city, p.state, p.pincode].reduce<string[]>((parts, value) => {
    const part = value?.trim();
    return part && !parts.some((x) => x.toLowerCase().includes(part.toLowerCase())) ? [...parts, part] : parts;
  }, []).join(', ');
}

/** Landmarks that already say where they are ("Opp. JNTU Gate", "Behind City Mall") are shown as written. */
const POSITIONED = /^(near|opp\.?|opposite|behind|beside|next to|adjacent to|above|below|beneath|in front of|off|inside|at)\b/i;

/** "Near Dadar Station" from a landmark ("Dadar Station"), or the landmark as written ("Opp. JNTU Gate"); null when there is none. */
export const nearLandmark = (landmark?: string | null) => {
  const l = landmark?.trim();
  return l ? (POSITIONED.test(l) ? l : `Near ${l}`) : null;
};
