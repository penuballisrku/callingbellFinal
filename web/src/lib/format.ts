import dayjs from 'dayjs';
import relativeTime from 'dayjs/plugin/relativeTime';

dayjs.extend(relativeTime);

const inr = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 });
const num = new Intl.NumberFormat('en-IN');
const compact = new Intl.NumberFormat('en-IN', { notation: 'compact', maximumFractionDigits: 1 });

/**
 * A currency to format in: an ISO code ("USD") or anything carrying one, such as a plan priced for the visitor's country. Without one,
 * amounts are Indian rupees: a business's own prices are always in its own currency, and every business is in India so far.
 */
export type CurrencyLike = string | { currencyCode: string; locale?: string | null } | null | undefined;

const currencyFormats = new Map<string, Intl.NumberFormat>();
function currencyFormat(c: CurrencyLike, whole: boolean): Intl.NumberFormat {
  const code = (typeof c === 'string' ? c : c?.currencyCode)?.trim().toUpperCase() || 'INR';
  if (code === 'INR' && whole) return inr;
  const locale = (typeof c === 'object' && c?.locale) || (code === 'INR' ? 'en-IN' : undefined);
  const key = `${code}|${locale ?? ''}|${whole}`;
  let f = currencyFormats.get(key);
  if (!f) {
    try {
      f = new Intl.NumberFormat(locale, { style: 'currency', currency: code, ...(whole ? { maximumFractionDigits: 0, minimumFractionDigits: 0 } : {}) });
    } catch {
      f = new Intl.NumberFormat(undefined, { style: 'currency', currency: code, ...(whole ? { maximumFractionDigits: 0, minimumFractionDigits: 0 } : {}) });
    }
    currencyFormats.set(key, f);
  }
  return f;
}

/** Whole amounts without decimals; amounts with a fraction (cents, fils, paise) with the currency's usual decimals. */
const formatAmount = (v: number, c?: CurrencyLike) => currencyFormat(c, Number.isInteger(v)).format(v);

export const money = (v?: number | null, c?: CurrencyLike) => (v === null || v === undefined ? '' : v === 0 ? 'Free' : formatAmount(v, c));
export const moneyExact = (v: number, c?: CurrencyLike) => formatAmount(v, c);
/** Exact amount for payments and invoices: keeps paise/cents when present (₹2,948.82), whole units otherwise (₹2,499, $24). */
export const moneyPrecise = (v: number, c?: CurrencyLike) => formatAmount(v, c);
/** Decimal places of a currency's smallest unit (2 for INR/USD, 0 for JPY, 3 for KWD), for rounding tax like the server. */
export const currencyDigits = (c?: CurrencyLike) => currencyFormat(c, false).resolvedOptions().maximumFractionDigits ?? 2;
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
