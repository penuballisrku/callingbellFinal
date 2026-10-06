import { useEffect, useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { Controller, useFieldArray, useFormContext, useWatch } from 'react-hook-form';
import {
  Button, Checkbox, FormControlLabel, IconButton, InputAdornment, MenuItem, Switch, TextField, ToggleButton, ToggleButtonGroup, Tooltip,
} from '@mui/material';
import AddRounded from '@mui/icons-material/AddRounded';
import DeleteOutlineRounded from '@mui/icons-material/DeleteOutlineRounded';
import CheckCircleRounded from '@mui/icons-material/CheckCircleRounded';
import EditOutlined from '@mui/icons-material/EditOutlined';
import ImageOutlined from '@mui/icons-material/ImageOutlined';
import LockOutlined from '@mui/icons-material/LockOutlined';
import PanoramaOutlined from '@mui/icons-material/PanoramaOutlined';
import PhotoLibraryOutlined from '@mui/icons-material/PhotoLibraryOutlined';
import VideoLibraryOutlined from '@mui/icons-material/VideoLibraryOutlined';
import { moneyExact, moneyPrecise, number } from '@/lib/format';
import { IMAGE_ACCEPT, MEDIA_LIMITS, VIDEO_ACCEPT, fileSize, type PendingMedia } from '@/lib/media';
import type { Category, City, Lookup, MediaKind, Plan } from '@/lib/types';
import { DropZone, MediaTile } from '@/components/media';
import { CityAutocomplete } from '@/components/CityAutocomplete';
import { useCityAreas } from '@/lib/hooks';
import { emptyService, type StepKey, type WizardForm } from './schema';
import { priceBreakdown } from '@/lib/payments';
import { PhoneVerification, phoneDigits } from '@/features/auth/otp';

export interface WizardData { categories: Category[]; cities: City[]; plans: Plan[]; serviceTypes: Lookup[]; socialPlatforms: Lookup[] }

export interface MediaState {
  items: PendingMedia[];
  add: (kind: MediaKind, files: File[]) => void;
  remove: (id: string) => void;
  rename: (id: string, title: string) => void;
  errors: string[];
}

function Section({ title, hint, children }: { title: string; hint?: string; children: ReactNode }) {
  return (
    <section className="space-y-4">
      <div>
        <h3 className="text-[15px] font-semibold">{title}</h3>
        {hint && <p className="mt-0.5 text-sm text-muted">{hint}</p>}
      </div>
      {children}
    </section>
  );
}

const get = (obj: unknown, path: string): { message?: string } | undefined =>
  path.split('.').reduce<unknown>((o, k) => (o as Record<string, unknown> | undefined)?.[k], obj) as { message?: string } | undefined;

function useField(name: string) {
  const { register, formState: { errors } } = useFormContext<WizardForm>();
  const error = get(errors, name)?.message;
  return { ...register(name as never), error: !!error, helperText: error };
}

/* ======================= 1. Account ======================= */
export function AccountStep({ emailStatus, onEmailBlur }: { emailStatus: 'idle' | 'checking' | 'available' | 'taken'; onEmailBlur: (email: string) => void }) {
  const { register, control, setValue, setError, clearErrors, trigger, formState: { errors } } = useFormContext<WizardForm>();
  const [phoneNumber = '', token, verifiedPhone] = useWatch({ control, name: ['phoneNumber', 'phoneVerificationToken', 'verifiedPhone'] });
  const phoneHelper = errors.phoneNumber?.message ?? 'You will sign in with a one-time code sent to this number';
  const emailField = register('email');
  const emailHelper = errors.email?.message
    ?? (emailStatus === 'checking' ? 'Checking…' : emailStatus === 'available' ? 'This email is available' : 'We will send booking and lead alerts here');
  return (
    <div className="space-y-6">
      <Section title="Your details" hint="The business owner or the person who will manage this listing.">
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="sm:col-span-2"><TextField label="Full name" autoComplete="name" required {...useField('displayName')} /></div>
          <TextField label="Email" type="email" autoComplete="email" required {...emailField}
            onBlur={(e) => { void emailField.onBlur(e); onEmailBlur(e.target.value); }}
            error={!!errors.email || emailStatus === 'taken'}
            helperText={emailStatus === 'taken' ? <>Already registered. <Link to="/login" className="font-semibold underline">Sign in</Link> to add your business.</> : emailHelper}
            slotProps={{ formHelperText: { sx: emailStatus === 'available' && !errors.email ? { color: 'success.main' } : undefined } }} />
          <TextField label="Mobile number" type="tel" autoComplete="tel" required placeholder="98765 43210" {...useField('phoneNumber')}
            helperText={phoneHelper}
            slotProps={{ input: { startAdornment: <InputAdornment position="start">+91</InputAdornment> } }} />
          <div className="sm:col-span-2">
            <PhoneVerification phoneNumber={phoneNumber} verified={!!token && verifiedPhone === phoneDigits(phoneNumber)}
              validatePhone={() => trigger('phoneNumber')}
              onVerified={(t, phone) => { setValue('phoneVerificationToken', t); setValue('verifiedPhone', phoneDigits(phone)); clearErrors('phoneNumber'); }}
              onPhoneError={(message) => setError('phoneNumber', { message }, { shouldFocus: true })} />
          </div>
        </div>
      </Section>
    </div>
  );
}

/* ======================= 2. Business ======================= */
export function BusinessStep({ data }: { data: WizardData }) {
  const { control, setValue, getValues } = useFormContext<WizardForm>();
  const categorySlug = useWatch({ control, name: 'business.categorySlug' });
  const description = useWatch({ control, name: 'business.description' }) ?? '';
  const subCategories = data.categories.find((c) => c.slug === categorySlug)?.subCategories ?? [];
  const nameField = useField('business.businessName');
  const tagField = useField('business.tagline');
  const descField = useField('business.description');
  const yearField = useField('business.yearEstablished');
  const teamField = useField('business.teamSize');
  const langField = useField('business.languages');

  return (
    <div className="space-y-8">
      <Section title="Business identity">
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="sm:col-span-2"><TextField label="Business name" required placeholder="e.g. Sharma Electricals" {...nameField} /></div>
          <Controller control={control} name="business.categorySlug" render={({ field, fieldState }) => (
            <TextField select label="Industry type" required {...field} error={!!fieldState.error} helperText={fieldState.error?.message ?? 'The sector you work in'}
              onChange={(e) => {
                field.onChange(e.target.value);
                const subs = data.categories.find((c) => c.slug === e.target.value)?.subCategories ?? [];
                if (!subs.some((s) => s.slug === getValues('business.subCategorySlug'))) setValue('business.subCategorySlug', '');
              }}>
              {data.categories.map((c) => <MenuItem key={c.slug} value={c.slug}>{c.name}</MenuItem>)}
            </TextField>
          )} />
          <Controller control={control} name="business.subCategorySlug" render={({ field, fieldState }) => (
            <TextField select label="Business category" required {...field} disabled={!categorySlug} error={!!fieldState.error}
              helperText={fieldState.error?.message ?? (categorySlug ? 'Where customers will find you' : 'Choose an industry first')}>
              {subCategories.map((s) => <MenuItem key={s.slug} value={s.slug}>{s.name}</MenuItem>)}
            </TextField>
          )} />
          <div className="sm:col-span-2"><TextField label="Tagline" placeholder="A short line customers see under your name" {...tagField} helperText={tagField.helperText ?? 'Optional · up to 200 characters'} /></div>
          <div className="sm:col-span-2">
            <TextField label="Business description" required multiline minRows={5} {...descField}
              placeholder="What do you offer, who do you serve, what makes you different? Mention experience, certifications and service areas."
              helperText={descField.helperText ?? `${description.trim().length} / 2000 characters · at least 50`} />
          </div>
        </div>
      </Section>
      <Section title="More about you" hint="Optional, but it helps customers choose you.">
        <div className="grid gap-4 sm:grid-cols-3">
          <TextField label="Year established" inputMode="numeric" placeholder="2015" {...yearField} />
          <TextField label="Team size" inputMode="numeric" placeholder="6" {...teamField} />
          <TextField label="Languages spoken" placeholder="Hindi, English" {...langField} />
        </div>
        <div className="flex flex-col gap-1 rounded-lg border border-line p-3 sm:flex-row sm:flex-wrap sm:gap-x-6">
          {([['acceptsOnlineBooking', 'Accept online bookings'], ['offersHomeService', 'Visit customers at home'], ['offersVideoConsultation', 'Offer video consultations']] as const).map(([k, label]) => (
            <Controller key={k} control={control} name={`business.${k}`} render={({ field }) => (
              <FormControlLabel control={<Switch checked={!!field.value} onChange={(e) => field.onChange(e.target.checked)} />} label={label} />
            )} />
          ))}
        </div>
      </Section>
    </div>
  );
}

/* ======================= 3. Contact & address ======================= */
// Cities and areas come from the shared hooks (the whole country), not the wizard data.
export function ContactStep(_props: { data: WizardData }) {
  const { control, setValue, getValues } = useFormContext<WizardForm>();
  const citySlug = useWatch({ control, name: 'business.citySlug' });
  const phone = useWatch({ control, name: 'business.businessPhone' });
  const whatsapp = useWatch({ control, name: 'business.whatsAppNumber' });
  const [sameAsPhone, setSame] = useState(() => !!phone && phone === whatsapp);
  // Every area of the chosen city; the area-discovery agent fills cities that have none yet (the list updates while it works).
  const { areas, discovering } = useCityAreas(citySlug);
  useEffect(() => { if (sameAsPhone) setValue('business.whatsAppNumber', phone ?? '', { shouldValidate: !!phone }); }, [sameAsPhone, phone, setValue]);

  return (
    <div className="space-y-8">
      <Section title="Contact information" hint="Shown on your profile so customers can call, WhatsApp or email you.">
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField label="Business phone" required inputMode="tel" placeholder="98765 43210 or 040 2345 6789" {...useField('business.businessPhone')} />
          <div>
            <TextField label="WhatsApp number" inputMode="tel" disabled={sameAsPhone} {...useField('business.whatsAppNumber')} />
            <FormControlLabel sx={{ mt: 0.5 }} control={<Checkbox size="small" checked={sameAsPhone} onChange={(e) => setSame(e.target.checked)} />}
              label={<span className="text-sm">Same as business phone</span>} />
          </div>
          <TextField label="Business email" type="email" placeholder="hello@yourbusiness.in" {...useField('business.businessEmail')} />
          <TextField label="Website URL" placeholder="https://www.yourbusiness.in" {...useField('business.website')} />
        </div>
      </Section>
      <Section title="Business address">
        <div className="grid gap-4 sm:grid-cols-2">
          <Controller control={control} name="business.citySlug" render={({ field, fieldState }) => (
            <CityAutocomplete value={field.value || null} label="City" required size="medium" error={!!fieldState.error} helperText={fieldState.error?.message}
              onChange={(slug) => { field.onChange(slug ?? ''); field.onBlur(); setValue('business.areaSlug', ''); }} />
          )} />
          <Controller control={control} name="business.areaSlug" render={({ field }) => (
            <TextField select label="Area / locality" {...field} value={areas.some((a) => a.slug === field.value) ? field.value : ''} disabled={!citySlug}
              helperText={!citySlug ? 'Choose a city first' : discovering ? 'Finding areas in this city… you can continue meanwhile' : 'Optional · helps "near me" searches'}
              onChange={(e) => {
                field.onChange(e.target.value);
                const area = areas.find((a) => a.slug === e.target.value);
                if (area && !getValues('business.pincode')) setValue('business.pincode', area.pincode, { shouldValidate: true });
              }}>
              <MenuItem value=""><em>Not listed</em></MenuItem>
              {areas.map((a) => <MenuItem key={a.slug} value={a.slug}>{a.name} · {a.pincode}</MenuItem>)}
            </TextField>
          )} />
          <div className="sm:col-span-2"><TextField label="Address" required placeholder="Shop / building, street" {...useField('business.addressLine')} /></div>
          <TextField label="Landmark" placeholder="Near …" {...useField('business.landmark')} />
          <TextField label="Pincode" required inputMode="numeric" {...useField('business.pincode')} />
        </div>
      </Section>
    </div>
  );
}

/* ======================= 4. Services & plan ======================= */
export function OfferStep({ data }: { data: WizardData }) {
  const { control, formState: { errors } } = useFormContext<WizardForm>();
  const { fields, append, remove } = useFieldArray({ control, name: 'business.services' });
  const planCode = useWatch({ control, name: 'business.planCode' });
  const cycle = useWatch({ control, name: 'business.billingCycle' });
  const plan = data.plans.find((p) => p.code === planCode);
  const maxServices = plan?.maxServices ?? 5;
  const overLimit = fields.length > maxServices;
  const listError = (errors.business?.services as { message?: string; root?: { message?: string } } | undefined);

  return (
    <div className="space-y-8">
      <Section title="Services offered" hint="Add the services customers can enquire about or book. You can add more later.">
        <div className="space-y-3">
          {fields.map((f, i) => (
            <ServiceRow key={f.id} index={i} data={data} onRemove={fields.length > 1 ? () => remove(i) : undefined} />
          ))}
        </div>
        {(listError?.message || listError?.root?.message) && <p className="text-sm text-danger">{listError.message ?? listError.root?.message}</p>}
        {overLimit && <p className="text-sm text-danger">The {plan?.name} plan allows up to {maxServices} services. Remove some or choose a bigger plan.</p>}
        <Button startIcon={<AddRounded />} variant="outlined" onClick={() => append(emptyService(data.serviceTypes[0]?.code ?? ''))} disabled={fields.length >= maxServices}>
          Add another service
        </Button>
        <span className="ml-3 text-xs text-muted">{fields.length} of {maxServices >= 999 ? 'unlimited' : maxServices} on {plan?.name ?? 'this'} plan</span>
      </Section>

      <Section title="Choose your plan" hint="Free forever, or choose a paid plan for more leads and visibility. Paid plans are paid securely online in the next step.">
        <Controller control={control} name="business.billingCycle" render={({ field }) => (
          <ToggleButtonGroup exclusive size="small" value={field.value} onChange={(_, v) => v && field.onChange(v)} aria-label="Billing cycle">
            <ToggleButton value="Monthly" sx={{ px: 2.5 }}>Monthly</ToggleButton>
            <ToggleButton value="Annual" sx={{ px: 2.5 }}>Annual · 2 months free</ToggleButton>
          </ToggleButtonGroup>
        )} />
        <Controller control={control} name="business.planCode" render={({ field }) => (
          <div role="radiogroup" aria-label="Plan" className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {data.plans.map((p) => {
              const selected = field.value === p.code;
              const price = cycle === 'Annual' ? p.annualPrice : p.monthlyPrice;
              return (
                <button key={p.code} type="button" role="radio" aria-checked={selected} onClick={() => field.onChange(p.code)}
                  className={`relative flex flex-col rounded-xl border p-4 text-left transition-[border-color,box-shadow] ${selected ? 'border-accent shadow-[0_0_0_3px_var(--cb-ring)]' : 'border-line hover:border-line-strong'}`}>
                  {p.isPopular && <span className="absolute -top-2.5 right-3 rounded-full bg-accent px-2 py-0.5 text-[10px] font-bold text-on-accent">Most popular</span>}
                  <span className="flex items-center justify-between gap-2">
                    <span className="font-semibold">{p.name}</span>
                    <span className={`flex h-5 w-5 items-center justify-center rounded-full border-2 ${selected ? 'border-accent bg-accent' : 'border-line-strong'}`}>
                      {selected && <span className="h-2 w-2 rounded-full bg-on-accent" />}
                    </span>
                  </span>
                  <span className="mt-2 text-xl font-bold tracking-tight">{price === 0 ? 'Free' : moneyExact(price)}
                    {price > 0 && <span className="text-xs font-normal text-muted"> /{cycle === 'Annual' ? 'year' : 'month'} + GST</span>}</span>
                  <span className="mt-2 text-xs text-muted">{number(p.leadCredits)} leads/month · {p.maxServices >= 999 ? 'Unlimited' : p.maxServices} services · {p.maxImages >= 999 ? 'Unlimited' : p.maxImages} photos</span>
                  {p.tagline && <span className="mt-1 text-xs text-ink-2">{p.tagline}</span>}
                </button>
              );
            })}
          </div>
        )} />
      </Section>
    </div>
  );
}

function ServiceRow({ index, data, onRemove }: { index: number; data: WizardData; onRemove?: () => void }) {
  const { control } = useFormContext<WizardForm>();
  const base = `business.services.${index}`;
  const name = useField(`${base}.name`);
  const price = useField(`${base}.price`);
  const unit = useField(`${base}.priceUnit`);
  const mins = useField(`${base}.durationMinutes`);
  const desc = useField(`${base}.description`);
  return (
    <fieldset className="rounded-xl border border-line p-4">
      <legend className="sr-only">Service {index + 1}</legend>
      <div className="mb-3 flex items-center justify-between">
        <span className="text-xs font-semibold uppercase tracking-wide text-muted">Service {index + 1}</span>
        {onRemove && <Tooltip title="Remove service"><IconButton size="small" onClick={onRemove} aria-label={`Remove service ${index + 1}`}><DeleteOutlineRounded fontSize="small" /></IconButton></Tooltip>}
      </div>
      <div className="grid gap-3 sm:grid-cols-6">
        <div className="sm:col-span-4"><TextField label="Service name" required placeholder="e.g. Fan installation" {...name} /></div>
        <div className="sm:col-span-2">
          <Controller control={control} name={`business.services.${index}.type`} render={({ field, fieldState }) => (
            <TextField select label="Delivered" required {...field} error={!!fieldState.error} helperText={fieldState.error?.message}>
              {data.serviceTypes.map((t) => <MenuItem key={t.code} value={t.code}>{t.name}</MenuItem>)}
            </TextField>
          )} />
        </div>
        <div className="sm:col-span-2"><TextField label="Price" required inputMode="decimal" {...price} slotProps={{ input: { startAdornment: <InputAdornment position="start">₹</InputAdornment> } }} /></div>
        <div className="sm:col-span-2"><TextField label="Price unit" placeholder="per visit" {...unit} /></div>
        <div className="sm:col-span-2"><TextField label="Duration" required inputMode="numeric" {...mins} slotProps={{ input: { endAdornment: <InputAdornment position="end">min</InputAdornment> } }} /></div>
        <div className="sm:col-span-6"><TextField label="Short description" multiline minRows={2} placeholder="What's included" {...desc} /></div>
      </div>
    </fieldset>
  );
}

/* ======================= 5. Billing & payment (paid plans only) ======================= */
const orderAmount = (plan: Plan, cycle: string) => priceBreakdown(cycle === 'Annual' ? plan.annualPrice : plan.monthlyPrice);

function OrderTotal({ plan, cycle }: { plan: Plan; cycle: string }) {
  const { total } = orderAmount(plan, cycle);
  return <span className="font-semibold">{moneyPrecise(total)} <span className="font-normal text-muted">incl. 18% GST</span></span>;
}

export function PaymentStep({ data, paymentsEnabled, onChangePlan }: { data: WizardData; paymentsEnabled: boolean; onChangePlan: () => void }) {
  const { control } = useFormContext<WizardForm>();
  const planCode = useWatch({ control, name: 'business.planCode' });
  const cycle = useWatch({ control, name: 'business.billingCycle' });
  const plan = data.plans.find((p) => p.code === planCode);
  const gstin = useField('gstin');
  if (!plan) return null;
  const { subtotal, tax, total } = orderAmount(plan, cycle);
  const months = cycle === 'Annual' ? 12 : 1;

  return (
    <div className="space-y-6">
      {!paymentsEnabled && (
        <div className="rounded-lg border border-warning-line bg-warning-soft px-4 py-3 text-sm text-warning-ink" role="status">
          Online payment is unavailable right now. Your business will start on the Free plan and you can upgrade from <strong>Plan & billing</strong> later.
        </div>
      )}
      <Section title="Order summary">
        <div className="rounded-xl border border-line">
          <div className="flex items-start justify-between gap-3 border-b border-line p-4">
            <div>
              <div className="font-semibold">{plan.name} plan</div>
              <div className="text-sm text-muted">{cycle} billing · {months === 12 ? '12 months' : '1 month'} · starts as soon as payment is confirmed</div>
            </div>
            <Button size="small" onClick={onChangePlan}>Change plan</Button>
          </div>
          <ul className="space-y-1.5 border-b border-line p-4 text-sm">
            {plan.features.filter((f) => !/^Everything in/i.test(f)).slice(0, 4).map((f) => (
              <li key={f} className="flex gap-2"><CheckCircleRounded sx={{ fontSize: 16 }} className="mt-0.5 text-success" />{f}</li>
            ))}
          </ul>
          <dl className="space-y-2 p-4 text-sm">
            <div className="flex justify-between"><dt className="text-muted">{plan.name} · {cycle}</dt><dd className="tabular">{moneyPrecise(subtotal)}</dd></div>
            <div className="flex justify-between"><dt className="text-muted">GST (18%)</dt><dd className="tabular">{moneyPrecise(tax)}</dd></div>
            <div className="flex justify-between border-t border-line pt-2 text-base font-bold"><dt>Total payable</dt><dd className="tabular">{moneyPrecise(total)}</dd></div>
          </dl>
        </div>
      </Section>
      <Section title="Invoice details" hint="Add your GSTIN to claim input tax credit. The invoice is issued in your business name.">
        <div className="sm:max-w-sm"><TextField label="GSTIN (optional)" placeholder="29ABCDE1234F1Z5" {...gstin} slotProps={{ htmlInput: { maxLength: 15, style: { textTransform: 'uppercase' } } }} /></div>
      </Section>
      <div className="flex items-start gap-3 rounded-xl bg-subtle p-4 text-sm">
        <LockOutlined className="mt-0.5 text-muted" fontSize="small" />
        <div>
          <div className="font-semibold">Secure payment with Razorpay</div>
          <p className="mt-0.5 text-muted">Pay by UPI, debit or credit card, net banking or wallet. Calling Bell never sees your card or bank details.
            If you close the payment window, your business stays on the Free plan and you can pay any time from your dashboard.</p>
        </div>
      </div>
    </div>
  );
}

/* ======================= 6. Media & social ======================= */
export function MediaStep({ data, media }: { data: WizardData; media: MediaState }) {
  const { control } = useFormContext<WizardForm>();
  const planCode = useWatch({ control, name: 'business.planCode' });
  const plan = data.plans.find((p) => p.code === planCode);
  const byKind = (k: MediaKind) => media.items.filter((m) => m.kind === k);
  const logo = byKind('logo')[0];
  const cover = byKind('cover')[0];
  const photos = byKind('photo');
  const videos = byKind('video');
  const maxPhotos = plan?.maxImages ?? 5;

  return (
    <div className="space-y-8">
      {media.errors.length > 0 && (
        <ul className="space-y-1 rounded-lg border border-danger/30 bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">
          {media.errors.map((e) => <li key={e}>{e}</li>)}
        </ul>
      )}
      <Section title="Logo & cover image" hint="JPG, PNG or WebP up to 20 MB. We optimise images automatically.">
        <div className="grid gap-4 sm:grid-cols-[200px_1fr]">
          <div>
            <div className="mb-1.5 text-sm font-medium">Business logo</div>
            {logo ? <MediaTile src={logo.previewUrl} kind="logo" aspect="1/1" fit="contain" onRemove={() => media.remove(logo.id)} title="Logo" />
              : <DropZone accept={IMAGE_ACCEPT} onFiles={(f) => media.add('logo', f)} title="Add logo" hint="Square works best" icon={<ImageOutlined />} compact />}
          </div>
          <div>
            <div className="mb-1.5 text-sm font-medium">Cover image / banner</div>
            {cover ? <MediaTile src={cover.previewUrl} kind="cover" aspect="16/6" onRemove={() => media.remove(cover.id)} title="Cover image" />
              : <DropZone accept={IMAGE_ACCEPT} onFiles={(f) => media.add('cover', f)} title="Add a cover image" hint="Wide photo, at least 1600 × 600 px" icon={<PanoramaOutlined />} />}
          </div>
        </div>
      </Section>

      <Section title={`Business photos (${photos.length}/${maxPhotos >= 999 ? '∞' : maxPhotos})`} hint="Your work, premises, team and products. The first photo becomes your main photo.">
        {photos.length > 0 && (
          <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 lg:grid-cols-5">
            {photos.map((p, i) => <MediaTile key={p.id} src={p.previewUrl} kind="photo" primary={i === 0} onRemove={() => media.remove(p.id)} title={p.file.name} />)}
          </div>
        )}
        <DropZone accept={IMAGE_ACCEPT} multiple disabled={photos.length >= maxPhotos} onFiles={(f) => media.add('photo', f)}
          title={photos.length >= maxPhotos ? `You've added the maximum for the ${plan?.name} plan` : 'Drag photos here or click to browse'}
          hint={`Up to ${maxPhotos >= 999 ? 'unlimited' : maxPhotos} photos on the ${plan?.name ?? 'selected'} plan`} icon={<PhotoLibraryOutlined />} />
      </Section>

      <Section title={`Promotional videos (${videos.length}/${MEDIA_LIMITS.maxVideos})`} hint={`MP4, WebM or MOV up to ${MEDIA_LIMITS.maxVideoMb} MB each. Short clips (under 2 minutes) work best.`}>
        {videos.length > 0 && (
          <ul className="space-y-2">
            {videos.map((v) => (
              <li key={v.id} className="flex items-center gap-3 rounded-lg border border-line p-2">
                <div className="w-28 shrink-0"><MediaTile src={v.poster ? v.previewUrl : null} kind="video" aspect="16/9" durationSeconds={v.durationSeconds} title={v.title} /></div>
                <div className="min-w-0 flex-1">
                  <TextField label="Video title" value={v.title ?? ''} onChange={(e) => media.rename(v.id, e.target.value)} slotProps={{ htmlInput: { maxLength: 150 } }} />
                  <div className="mt-1 text-xs text-muted">{fileSize(v.file.size)}</div>
                </div>
                <Tooltip title="Remove video"><IconButton onClick={() => media.remove(v.id)} aria-label={`Remove ${v.title}`}><DeleteOutlineRounded /></IconButton></Tooltip>
              </li>
            ))}
          </ul>
        )}
        <DropZone accept={VIDEO_ACCEPT} multiple disabled={videos.length >= MEDIA_LIMITS.maxVideos} onFiles={(f) => media.add('video', f)}
          title={videos.length >= MEDIA_LIMITS.maxVideos ? 'Maximum videos added' : 'Drag videos here or click to browse'} hint="Videos upload after your account is created" icon={<VideoLibraryOutlined />} />
      </Section>

      <SocialLinks data={data} />
    </div>
  );
}

function SocialLinks({ data }: { data: WizardData }) {
  const { control } = useFormContext<WizardForm>();
  const { fields, append, remove } = useFieldArray({ control, name: 'business.socialLinks' });
  const links = useWatch({ control, name: 'business.socialLinks' }) ?? [];
  const used = new Set(links.map((l) => l.platform));
  const next = data.socialPlatforms.find((p) => !used.has(p.code));
  return (
    <Section title="Social media links" hint="Optional. Link the profiles where customers can see more of your work.">
      <div className="space-y-3">
        {fields.map((f, i) => <SocialRow key={f.id} index={i} data={data} used={used} onRemove={() => remove(i)} />)}
      </div>
      {next && <Button startIcon={<AddRounded />} variant="outlined" onClick={() => append({ platform: next.code, url: '' })}>Add social profile</Button>}
    </Section>
  );
}

function SocialRow({ index, data, used, onRemove }: { index: number; data: WizardData; used: Set<string>; onRemove: () => void }) {
  const { control } = useFormContext<WizardForm>();
  const platform = useWatch({ control, name: `business.socialLinks.${index}.platform` });
  const example = data.socialPlatforms.find((p) => p.code === platform)?.description ?? 'https://';
  const url = useField(`business.socialLinks.${index}.url`);
  return (
    <div className="flex items-start gap-2">
      <div className="w-40 shrink-0">
        <Controller control={control} name={`business.socialLinks.${index}.platform`} render={({ field }) => (
          <TextField select label="Platform" {...field}>
            {data.socialPlatforms.map((p) => <MenuItem key={p.code} value={p.code} disabled={p.code !== field.value && used.has(p.code)}>{p.name}</MenuItem>)}
          </TextField>
        )} />
      </div>
      <div className="min-w-0 flex-1"><TextField label="Profile URL" placeholder={example} {...url} /></div>
      <IconButton onClick={onRemove} aria-label="Remove social link" sx={{ mt: 0.25 }}><DeleteOutlineRounded /></IconButton>
    </div>
  );
}

/* ======================= 7. Review ======================= */
export function ReviewStep({ data, media, onEdit, paymentsEnabled }: { data: WizardData; media: MediaState; onEdit: (step: StepKey) => void; paymentsEnabled: boolean }) {
  const { control, getValues } = useFormContext<WizardForm>();
  const v = getValues();
  const b = v.business;
  const category = data.categories.find((c) => c.slug === b.categorySlug);
  const sub = category?.subCategories.find((s) => s.slug === b.subCategorySlug);
  const city = data.cities.find((c) => c.slug === b.citySlug);
  const { areas: cityAreas } = useCityAreas(b.citySlug);
  const area = cityAreas.find((a) => a.slug === b.areaSlug) ?? city?.areas.find((a) => a.slug === b.areaSlug);
  const plan = data.plans.find((p) => p.code === b.planCode);
  const count = (k: MediaKind) => media.items.filter((m) => m.kind === k).length;

  const block = (title: string, step: StepKey, rows: [string, ReactNode][]) => (
    <div className="rounded-xl border border-line">
      <div className="flex items-center justify-between border-b border-line px-4 py-2.5">
        <h3 className="text-sm font-semibold">{title}</h3>
        <Button size="small" startIcon={<EditOutlined fontSize="small" />} onClick={() => onEdit(step)}>Edit</Button>
      </div>
      <dl className="divide-y divide-line">
        {rows.filter(([, val]) => val !== '' && val !== null && val !== undefined).map(([k, val]) => (
          <div key={k} className="grid gap-1 px-4 py-2 text-sm sm:grid-cols-[160px_1fr]"><dt className="text-muted">{k}</dt><dd className="min-w-0 break-words">{val}</dd></div>
        ))}
      </dl>
    </div>
  );

  return (
    <div className="space-y-4">
      {v.mode === 'register' && block('Account', 'account', [['Name', v.displayName], ['Email', v.email], ['Mobile', v.phoneNumber]])}
      {block('Business', 'business', [
        ['Business name', b.businessName], ['Industry', category?.name], ['Category', sub?.name], ['Tagline', b.tagline],
        ['Description', <span className="line-clamp-3">{b.description}</span>], ['Established', b.yearEstablished], ['Team size', b.teamSize], ['Languages', b.languages],
      ])}
      {block('Contact & address', 'contact', [
        ['Phone', b.businessPhone], ['WhatsApp', b.whatsAppNumber], ['Email', b.businessEmail], ['Website', b.website],
        ['Address', [b.addressLine, b.landmark, area?.name, city?.name, b.pincode].filter(Boolean).join(', ')],
      ])}
      {block('Services & plan', 'offer', [
        ['Services', <ul className="space-y-0.5">{b.services.map((s) => <li key={s.name}>{s.name} · {moneyExact(Number(s.price || 0))}{s.priceUnit ? ` ${s.priceUnit}` : ''}</li>)}</ul>],
        ['Plan', plan ? `${plan.name} · ${b.billingCycle}` : ''],
      ])}
      {plan && plan.monthlyPrice > 0 && block('Payment', paymentsEnabled ? 'payment' : 'offer', [
        ['Amount', paymentsEnabled ? <OrderTotal plan={plan} cycle={b.billingCycle} /> : 'Online payment unavailable: you will start on the Free plan'],
        ['GSTIN', v.gstin || 'Not provided'],
        ['Paid via', paymentsEnabled ? 'Razorpay · UPI, cards, net banking or wallets' : '-'],
      ])}
      {block('Media & social', 'media', [
        ['Logo', count('logo') ? <Done /> : 'Not added'], ['Cover image', count('cover') ? <Done /> : 'Not added'],
        ['Photos', `${count('photo')} selected`], ['Videos', `${count('video')} selected`],
        ['Social links', b.socialLinks.length ? b.socialLinks.map((l) => l.platform).join(', ') : 'None'],
      ])}
      <Controller control={control} name="acceptTerms" render={({ field, fieldState }) => (
        <div className="rounded-xl bg-subtle p-4">
          <FormControlLabel control={<Checkbox checked={field.value} onChange={(e) => field.onChange(e.target.checked)} />}
            label={<span className="text-sm">I confirm the details are accurate and agree to the Calling Bell Terms of Service, Privacy Policy and Listing Guidelines.</span>} />
          {fieldState.error && <p className="ml-8 text-sm text-danger">{fieldState.error.message}</p>}
        </div>
      )} />
    </div>
  );
}

const Done = () => <span className="inline-flex items-center gap-1 text-success"><CheckCircleRounded sx={{ fontSize: 16 }} />Added</span>;
