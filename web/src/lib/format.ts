import dayjs from 'dayjs';
import relativeTime from 'dayjs/plugin/relativeTime';

dayjs.extend(relativeTime);

const inr = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 });
const num = new Intl.NumberFormat('en-IN');
const compact = new Intl.NumberFormat('en-IN', { notation: 'compact', maximumFractionDigits: 1 });

export const money = (v?: number | null) => (v === null || v === undefined ? '' : v === 0 ? 'Free' : inr.format(v));
export const moneyExact = (v: number) => inr.format(v);
const inrPaise = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', minimumFractionDigits: 2, maximumFractionDigits: 2 });
/** Exact amount for payments and invoices: keeps paise when present (₹2,948.82), whole rupees otherwise (₹2,499). */
export const moneyPrecise = (v: number) => (Number.isInteger(v) ? inr.format(v) : inrPaise.format(v));
export const number = (v: number) => num.format(v);
export const compactNumber = (v: number) => compact.format(v);
/** Indian short scale for revenue: ₹4.2L, ₹1.3Cr */
export const moneyShort = (v: number) =>
  v >= 1e7 ? `₹${(v / 1e7).toFixed(2)}Cr` : v >= 1e5 ? `₹${(v / 1e5).toFixed(1)}L` : v >= 1e3 ? `₹${(v / 1e3).toFixed(1)}K` : `₹${Math.round(v)}`;

export const date = (v?: string | null) => (v ? dayjs(v).format('D MMM YYYY') : '');
export const dateTime = (v?: string | null) => (v ? dayjs(v).format('D MMM YYYY, h:mm A') : '');
export const time = (v?: string | null) => (v ? dayjs(v).format('h:mm A') : '');
export const ago = (v?: string | null) => (v ? dayjs(v).fromNow() : '');

export const initials = (name: string) =>
  name.replace(/^(Dr|Adv|Ar|Prof)\.?\s+/i, '').split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join('');

export const hhmm = (v?: string | null) => (v ? dayjs(`2000-01-01T${v}`).format('h:mm A') : '');

export const pluralize = (n: number, one: string, many = `${one}s`) => `${number(n)} ${n === 1 ? one : many}`;
