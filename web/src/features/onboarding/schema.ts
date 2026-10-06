import { z } from 'zod';

/** Client-side mirror of the server rules (BusinessOnboardingFeature.cs). The server re-validates everything. */

const thisYear = new Date().getFullYear();
const mobile = /^(\+91[\s-]?)?[6-9]\d{4}[\s-]?\d{5}$/;
const optionalText = (max: number) => z.string().trim().max(max, `Keep this under ${max} characters`);

/** Indian mobile or landline with STD code: 10 significant digits after an optional +91 / 0 prefix. */
export function isIndianPhone(value: string) {
  let digits = value.replace(/\D/g, '');
  if (value.trim().startsWith('+') && digits.startsWith('91')) digits = digits.slice(2);
  else if (digits.length === 11 && digits.startsWith('0')) digits = digits.slice(1);
  return digits.length === 10 && digits[0] !== '0';
}
const isUrl = (v: string) => {
  try { const u = new URL(v); return (u.protocol === 'https:' || u.protocol === 'http:') && u.hostname.includes('.'); } catch { return false; }
};

export const serviceSchema = z.object({
  name: z.string().trim().min(2, 'Enter the service name').max(150),
  description: optionalText(1000),
  price: z.string().trim().regex(/^\d+(\.\d{1,2})?$/, 'Enter a price in rupees, e.g. 499').refine((v) => Number(v) <= 10_000_000, 'Price is too high'),
  priceUnit: optionalText(40),
  durationMinutes: z.string().trim().regex(/^\d+$/, 'Enter minutes, e.g. 60').refine((v) => Number(v) <= 1440, 'At most 1,440 minutes'),
  type: z.string().min(1, 'Choose how this service is delivered'),
});

export const socialSchema = z.object({
  platform: z.string().min(1, 'Choose a platform'),
  url: z.string().trim().min(1, 'Enter the profile URL').refine(isUrl, 'Enter a valid URL starting with https://'),
});

export const businessSchema = z.object({
  businessName: z.string().trim().min(3, 'Enter your business name (at least 3 characters)').max(150),
  categorySlug: z.string().min(1, 'Choose your industry'),
  subCategorySlug: z.string().min(1, 'Choose your business category'),
  tagline: optionalText(200),
  description: z.string().trim().min(50, 'Write at least 50 characters about your business').max(2000),
  yearEstablished: z.string().trim().refine((v) => v === '' || (/^\d{4}$/.test(v) && +v >= 1900 && +v <= thisYear), `Enter a year between 1900 and ${thisYear}`),
  teamSize: z.string().trim().refine((v) => v === '' || (/^\d+$/.test(v) && +v >= 1 && +v <= 100000), 'Enter the number of people'),
  languages: optionalText(200),
  acceptsOnlineBooking: z.boolean(),
  offersHomeService: z.boolean(),
  offersVideoConsultation: z.boolean(),

  businessPhone: z.string().trim().refine(isIndianPhone, 'Enter a valid 10-digit phone number'),
  whatsAppNumber: z.string().trim().refine((v) => v === '' || isIndianPhone(v), 'Enter a valid 10-digit WhatsApp number'),
  businessEmail: z.string().trim().refine((v) => v === '' || z.string().email().safeParse(v).success, 'Enter a valid email address'),
  website: z.string().trim().max(300).refine((v) => v === '' || isUrl(v), 'Enter a valid URL starting with https://'),
  citySlug: z.string().min(1, 'Choose your city'),
  areaSlug: z.string(),
  addressLine: z.string().trim().min(5, 'Enter your business address').max(300),
  landmark: optionalText(150),
  pincode: z.string().trim().regex(/^[1-9]\d{5}$/, 'Enter a valid 6-digit pincode'),

  services: z.array(serviceSchema).min(1, 'Add at least one service'),
  planCode: z.string().min(1, 'Choose a plan'),
  billingCycle: z.enum(['Monthly', 'Annual']),
  socialLinks: z.array(socialSchema),
});

const accountShape = {
  displayName: z.string().trim().min(2, 'Enter your full name').max(120),
  email: z.string().trim().email('Enter a valid email address'),
  phoneNumber: z.string().trim().regex(mobile, 'Enter a valid 10-digit mobile number'),
  /** Issued by POST /api/auth/otp/verify for {@link verifiedPhone}; never saved in the draft. */
  phoneVerificationToken: z.string(),
  verifiedPhone: z.string(),
};

/**
 * Account fields are validated only when creating a new account. (Field-level rules, not an object-level refine, so each step's
 * errors show even while later steps are still empty. Mobile verification is checked in the wizard when leaving the step.)
 */
export const makeWizardSchema = (mode: 'register' | 'setup') => z.object({
  mode: z.enum(['register', 'setup']),
  displayName: mode === 'register' ? accountShape.displayName : z.string(),
  email: mode === 'register' ? accountShape.email : z.string(),
  phoneNumber: mode === 'register' ? accountShape.phoneNumber : z.string(),
  phoneVerificationToken: accountShape.phoneVerificationToken,
  verifiedPhone: accountShape.verifiedPhone,
  acceptTerms: z.boolean().refine((v) => v, 'Please accept the terms to continue'),
  /** Optional GSTIN printed on the subscription invoice. */
  gstin: z.string().trim().toUpperCase().refine((v) => v === '' || /^\d{2}[A-Z]{5}\d{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/.test(v), 'Enter a valid 15-character GSTIN, e.g. 29ABCDE1234F1Z5'),
  business: businessSchema,
});

const wizardSchema = makeWizardSchema('setup');
export type WizardForm = z.infer<typeof wizardSchema>;
export type ServiceForm = z.infer<typeof serviceSchema>;

export const emptyService = (type = ''): ServiceForm => ({ name: '', description: '', price: '', priceUnit: '', durationMinutes: '60', type });

export const defaultValues = (mode: 'register' | 'setup', user?: { displayName?: string; email?: string; phoneNumber?: string | null }): WizardForm => ({
  mode,
  displayName: user?.displayName ?? '', email: user?.email ?? '', phoneNumber: user?.phoneNumber ?? '', phoneVerificationToken: '', verifiedPhone: '',
  acceptTerms: false,
  gstin: '',
  business: {
    businessName: '', categorySlug: '', subCategorySlug: '', tagline: '', description: '', yearEstablished: '', teamSize: '', languages: '',
    acceptsOnlineBooking: true, offersHomeService: false, offersVideoConsultation: false,
    businessPhone: '', whatsAppNumber: '', businessEmail: '', website: '', citySlug: '', areaSlug: '', addressLine: '', landmark: '', pincode: '',
    services: [emptyService()], planCode: 'FREE', billingCycle: 'Monthly', socialLinks: [],
  },
});

/** Wizard steps and the form fields each one validates before moving on. */
export const STEPS = [
  { key: 'account', label: 'Account', title: 'Create your account', subtitle: 'You will use this to sign in and manage your business.',
    fields: ['displayName', 'email', 'phoneNumber'] },
  { key: 'business', label: 'Business', title: 'About your business', subtitle: 'Tell customers who you are and what you do.',
    fields: ['business.businessName', 'business.categorySlug', 'business.subCategorySlug', 'business.tagline', 'business.description',
      'business.yearEstablished', 'business.teamSize', 'business.languages'] },
  { key: 'contact', label: 'Contact & address', title: 'Contact information & address', subtitle: 'How and where customers can reach you.',
    fields: ['business.businessPhone', 'business.whatsAppNumber', 'business.businessEmail', 'business.website', 'business.citySlug',
      'business.areaSlug', 'business.addressLine', 'business.landmark', 'business.pincode'] },
  { key: 'offer', label: 'Services & plan', title: 'Services & plan', subtitle: 'List what you offer and choose how you want to grow.',
    fields: ['business.services', 'business.planCode', 'business.billingCycle'] },
  { key: 'payment', label: 'Payment', title: 'Billing & payment', subtitle: 'Review your order. You will pay securely with Razorpay right after your profile is created.',
    fields: ['gstin'] },
  { key: 'media', label: 'Media & social', title: 'Photos, videos & social profiles', subtitle: 'Profiles with photos and videos get far more enquiries.',
    fields: ['business.socialLinks'] },
  { key: 'review', label: 'Review', title: 'Review & submit', subtitle: 'Check everything before we create your business profile.',
    fields: ['acceptTerms'] },
] as const;

export type StepKey = (typeof STEPS)[number]['key'];

/** Maps a server error key ("business.services[0].name") to a form path ("business.services.0.name"). */
export const toFormPath = (key: string) => key.replace(/\[(\d+)\]/g, '.$1');
