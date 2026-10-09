import { useState } from 'react';
import { Link } from 'react-router';
import {
  Alert, Button, Checkbox, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, Skeleton, TextField, Tooltip,
} from '@mui/material';
import StarRounded from '@mui/icons-material/StarRounded';
import MapRounded from '@mui/icons-material/MapRounded';
import OpenInNewRounded from '@mui/icons-material/OpenInNewRounded';
import ReportProblemOutlined from '@mui/icons-material/ReportProblemOutlined';
import CheckCircleRounded from '@mui/icons-material/CheckCircleRounded';
import { api, ApiError, errorMessage } from '@/lib/api';
import { number } from '@/lib/format';
import type { ExistingBusinessMatch, JoinCallingBellBusiness } from '@/lib/types';
import { emptyHours, type WizardForm } from './schema';

/* ---------- Merging the import into the form ---------- */

const tenDigits = (phone?: string | null) => {
  let digits = (phone ?? '').replace(/\D/g, '');
  if (digits.length === 12 && digits.startsWith('91')) digits = digits.slice(2);
  else if (digits.length === 11 && digits.startsWith('0')) digits = digits.slice(1);
  return digits.length === 10 ? digits : '';
};

/**
 * Fills the sign-up form from an imported place. Only empty fields are filled, so what the person already typed (a saved draft for the
 * same place) is never overwritten. Returns the form and the names of the fields that were filled, for the "imported" summary.
 */
export function withImport(form: WizardForm, d: JoinCallingBellBusiness): { form: WizardForm; filled: string[] } {
  const b = form.business;
  const filled: string[] = [];
  const fill = (label: string, current: string, value?: string | null) => {
    if (current || !value) return current;
    filled.push(label);
    return value;
  };
  const hasCategory = !!(b.categorySlug || b.subCategorySlug);
  const hoursEmpty = b.hours.every((h) => !h.open && !h.close && !h.closed);
  const importedHours = d.businessHours.length > 0 && hoursEmpty
    ? emptyHours().map((h) => {
      const x = d.businessHours.find((i) => i.dayOfWeek === h.dayOfWeek);
      return x ? { ...h, open: x.open ?? '', close: x.close ?? '', closed: x.isClosed } : h;
    })
    : b.hours;
  if (importedHours !== b.hours) filled.push('Opening hours');
  const social = b.socialLinks.length === 0 && d.socialLinks.length > 0 ? d.socialLinks : b.socialLinks;
  if (social !== b.socialLinks) filled.push('Social profiles');
  if (!hasCategory && d.subCategorySlug && d.categorySlug) filled.push('Category');
  const phone = tenDigits(d.phone);
  const whatsapp = tenDigits(d.whatsApp);

  return {
    filled,
    form: {
      ...form,
      business: {
        ...b,
        sourceId: d.sourceBusinessId,
        latitude: b.latitude ?? d.latitude ?? null,
        longitude: b.longitude ?? d.longitude ?? null,
        businessName: fill('Business name', b.businessName, d.businessName?.slice(0, 150)),
        categorySlug: hasCategory ? b.categorySlug : (d.subCategorySlug && d.categorySlug) || '',
        subCategorySlug: hasCategory ? b.subCategorySlug : (d.categorySlug && d.subCategorySlug) || '',
        description: fill('Description', b.description, d.description?.slice(0, 2000)),
        businessPhone: fill('Phone', b.businessPhone, phone),
        whatsAppNumber: fill('WhatsApp', b.whatsAppNumber, /^[6-9]\d{9}$/.test(whatsapp) ? whatsapp : ''),
        businessEmail: fill('Email', b.businessEmail, d.email),
        website: fill('Website', b.website, d.website),
        citySlug: fill('City', b.citySlug, d.citySlug),
        areaSlug: b.citySlug && b.citySlug !== d.citySlug ? b.areaSlug : fill('Area', b.areaSlug, d.areaSlug),
        addressLine: fill('Address', b.addressLine, d.addressLine?.slice(0, 300)),
        pincode: fill('Pincode', b.pincode, d.pincode),
        socialLinks: social,
        hours: importedHours,
      },
    },
  };
}

/* ---------- Loading and errors ---------- */

export function ImportLoading() {
  return (
    <div className="container-page py-10 md:py-14" aria-busy="true">
      <div className="mx-auto max-w-3xl">
        <div className="card flex items-center gap-4 p-5">
          <CircularProgress size={28} />
          <div>
            <p className="font-semibold">Loading your business information…</p>
            <p className="text-sm text-muted">We're gathering the details, hours and photos so you don't have to type them.</p>
          </div>
        </div>
        <div className="mt-6 space-y-3">
          <Skeleton variant="rounded" height={44} />
          <div className="grid gap-3 sm:grid-cols-2"><Skeleton variant="rounded" height={56} /><Skeleton variant="rounded" height={56} /></div>
          <Skeleton variant="rounded" height={120} />
          <div className="grid grid-cols-3 gap-2 sm:grid-cols-5">{Array.from({ length: 5 }, (_, i) => <Skeleton key={i} variant="rounded" sx={{ aspectRatio: '4/3', height: 'auto' }} />)}</div>
        </div>
      </div>
    </div>
  );
}

export function ImportError({ error, onRetry, onContinue }: { error: unknown; onRetry: () => void; onContinue: () => void }) {
  const notFound = error instanceof ApiError && (error.status === 404 || error.status === 400);
  return (
    <div className="container-page py-10 md:py-14">
      <div className="card mx-auto max-w-xl p-6 text-center">
        <ReportProblemOutlined className="text-warning" sx={{ fontSize: 36 }} />
        <h1 className="mt-2 text-xl font-bold">{notFound ? 'We couldn’t find that business' : 'We couldn’t load the business details'}</h1>
        <p className="mt-1 text-sm text-muted">
          {notFound ? 'It may have closed or moved on the map. You can still list your business by entering the details yourself.'
            : 'The map service didn’t answer in time. Try again, or enter the details yourself.'}
        </p>
        <div className="mt-5 flex flex-wrap justify-center gap-2">
          {!notFound && <Button variant="contained" onClick={onRetry}>Try again</Button>}
          <Button variant={notFound ? 'contained' : 'outlined'} onClick={onContinue}>Enter details myself</Button>
        </div>
      </div>
    </div>
  );
}

/* ---------- What was imported ---------- */

export function ImportedBanner({ d, filled }: { d: JoinCallingBellBusiness; filled: string[] }) {
  return (
    <Alert severity="info" icon={<MapRounded />} sx={{ mb: 3, alignItems: 'flex-start' }}>
      <p className="font-semibold">Imported from {d.sourceName}: {d.businessName}</p>
      <p className="mt-0.5 text-sm">
        {filled.length ? <>We filled in {filled.length} {filled.length === 1 ? 'detail' : 'details'} ({filled.slice(0, 6).join(', ')}{filled.length > 6 ? ', …' : ''}). </> : null}
        Check everything and change anything that isn’t right before you submit.
      </p>
      <p className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
        {d.rating != null && d.reviewCount ? (
          <span className="inline-flex items-center gap-0.5">
            <StarRounded sx={{ fontSize: 14, color: '#F4A62C' }} />{d.rating.toFixed(1)} · {number(d.reviewCount)} reviews on {d.sourceName}
            <span className="text-muted">(not copied: Calling Bell reviews start fresh)</span>
          </span>
        ) : null}
        {d.sourceUrl && <a href={d.sourceUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-0.5 font-semibold hover:underline">View on {d.sourceName}<OpenInNewRounded sx={{ fontSize: 12 }} /></a>}
      </p>
    </Alert>
  );
}

/* ---------- Duplicates and claims ---------- */

const matchLabel: Record<string, string> = {
  SourceId: 'Created from this same place', Phone: 'Same phone number', Website: 'Same website', Location: 'Same location', NameAndCity: 'Same name in this city',
};

/**
 * Shown when the place may already be on Calling Bell. A strong match (same place, or same phone / website / location with a similar
 * name) can't be registered again: the person can view or claim it. Otherwise they may carry on if it's a different business.
 */
export function DuplicateDialog({ matches, sourceId, onContinue, onClose }: {
  matches: ExistingBusinessMatch[]; sourceId?: string; onContinue: () => void; onClose: () => void;
}) {
  const [claiming, setClaiming] = useState<ExistingBusinessMatch | null>(null);
  const strong = matches.some((m) => m.isStrong);
  return (
    <>
      <Dialog open={!claiming} onClose={strong ? undefined : onClose} maxWidth="sm" fullWidth aria-labelledby="dup-title">
        <DialogTitle id="dup-title">{strong ? 'This business is already on Calling Bell' : 'Is this business already on Calling Bell?'}</DialogTitle>
        <DialogContent>
          <p className="mb-3 text-sm text-muted">
            {strong ? 'To avoid duplicate listings, view it or claim it if it’s yours. Our team verifies claims and hands the listing over to you.'
              : 'We found listings that look similar. If one of them is yours, claim it instead of creating another.'}
          </p>
          <ul className="space-y-2">
            {matches.map((m) => (
              <li key={m.id} className="flex flex-col gap-2 rounded-lg border border-line p-3 sm:flex-row sm:items-center">
                <div className="min-w-0 flex-1">
                  <p className="truncate font-semibold">{m.name}</p>
                  <p className="text-xs text-muted">{[m.city, matchLabel[m.matchedBy] ?? m.matchedBy, m.status === 'Active' ? null : 'Not live yet'].filter(Boolean).join(' · ')}</p>
                </div>
                <div className="flex shrink-0 gap-2">
                  {m.status === 'Active' && <Button size="small" component={Link} to={`/business/${m.slug}`} target="_blank" endIcon={<OpenInNewRounded />}>View business</Button>}
                  <Button size="small" variant="contained" disableElevation onClick={() => setClaiming(m)}>Claim business</Button>
                </div>
              </li>
            ))}
          </ul>
        </DialogContent>
        <DialogActions>
          {strong ? <Button component={Link} to="/nearby">Back to search</Button>
            : <Button onClick={onContinue}>It’s a different business, continue</Button>}
        </DialogActions>
      </Dialog>
      {claiming && <ClaimDialog business={claiming} sourceId={sourceId} onClose={() => setClaiming(null)} />}
    </>
  );
}

export function ClaimDialog({ business, sourceId, onClose }: { business: ExistingBusinessMatch; sourceId?: string; onClose: () => void }) {
  const [form, setForm] = useState({ name: '', phone: '', email: '', message: '' });
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
  const [done, setDone] = useState<string | null>(null);
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => { setForm((f) => ({ ...f, [k]: e.target.value })); setErrors((x) => ({ ...x, [k]: '' })); };

  const submit = async () => {
    const local: Record<string, string> = {};
    if (form.name.trim().length < 2) local.name = 'Enter your name';
    if (form.phone.replace(/\D/g, '').length < 10) local.phone = 'Enter your mobile number';
    setErrors(local);
    if (Object.keys(local).length) return;
    setSending(true);
    setError(null);
    try {
      const { data } = await api.post<{ requestNumber: string }>(`/api/businesses/${business.id}/claim-requests`, {
        name: form.name, phone: form.phone, email: form.email || null, message: form.message || null, sourceId: sourceId ?? null,
      });
      setDone(data.requestNumber);
    } catch (e) {
      if (e instanceof ApiError && e.errors) setErrors(Object.fromEntries(Object.entries(e.errors).map(([k, v]) => [k, v[0] ?? ''])));
      else setError(errorMessage(e));
    } finally {
      setSending(false);
    }
  };

  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth aria-labelledby="claim-title">
      <DialogTitle id="claim-title">Claim {business.name}</DialogTitle>
      <DialogContent>
        {done ? (
          <div className="py-2 text-center">
            <CheckCircleRounded className="text-success" sx={{ fontSize: 40 }} />
            <p className="mt-2 font-semibold">Claim request sent</p>
            <p className="mt-1 text-sm text-muted">Reference <strong>{done}</strong>. Our team will verify that the business is yours and contact you.</p>
          </div>
        ) : (
          <div className="space-y-3 pt-1">
            {error && <Alert severity="error">{error}</Alert>}
            <p className="text-sm text-muted">Tell us how to reach you. We’ll check that you own or manage this business before handing it over.</p>
            <TextField label="Your name" required fullWidth value={form.name} onChange={set('name')} error={!!errors.name} helperText={errors.name} />
            <TextField label="Mobile number" required fullWidth type="tel" value={form.phone} onChange={set('phone')} error={!!errors.phone} helperText={errors.phone} />
            <TextField label="Email" fullWidth type="email" value={form.email} onChange={set('email')} error={!!errors.email} helperText={errors.email} />
            <TextField label="Anything we should know?" fullWidth multiline minRows={2} value={form.message} onChange={set('message')}
              placeholder="e.g. I'm the owner; the listed number is my shop's landline" />
          </div>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{done ? 'Close' : 'Cancel'}</Button>
        {!done && <Button variant="contained" disableElevation onClick={() => void submit()} disabled={sending}>{sending ? 'Sending…' : 'Send claim request'}</Button>}
      </DialogActions>
    </Dialog>
  );
}

/* ---------- Photos from the source ---------- */

export interface ImportedPhotosState {
  images: JoinCallingBellBusiness['images'];
  sourceName: string;
  importable: boolean;
  selected: string[];
  toggle: (reference: string) => void;
}

/**
 * The place's photos, with the credit the source requires. They can be added to the new listing only when the server allows it;
 * otherwise they are shown for reference, since map photos usually belong to the people who took them.
 */
export function ImportedPhotos({ state, maxSelectable }: { state: ImportedPhotosState; maxSelectable: number }) {
  if (state.images.length === 0) return null;
  return (
    <section className="space-y-3">
      <div>
        <h3 className="text-[15px] font-semibold">Photos from {state.sourceName} ({state.images.length})</h3>
        <p className="mt-0.5 text-sm text-muted">
          {state.importable
            ? `Tick the photos to add to your listing (they're added after your profile is created). Only add photos you own or have permission to use.`
            : `Shown for reference. These photos belong to the people who took them, so they can't be copied to your listing. Add your own photos above.`}
        </p>
      </div>
      <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 lg:grid-cols-5">
        {state.images.map((img) => {
          const checked = state.selected.includes(img.reference);
          return (
            <figure key={img.reference} className={`relative overflow-hidden rounded-lg border bg-subtle ${checked ? 'border-accent ring-2 ring-accent/40' : 'border-line'}`}>
              <img src={img.thumbnailUrl} alt={img.altText} loading="lazy" className="aspect-[4/3] h-auto w-full object-cover" />
              {state.importable && (
                <Tooltip title={checked ? 'Don’t add' : 'Add to my listing'}>
                  <Checkbox size="small" checked={checked} disabled={!checked && state.selected.length >= maxSelectable} onChange={() => state.toggle(img.reference)}
                    slotProps={{ input: { 'aria-label': `Add ${img.altText} to my listing` } }}
                    sx={{ position: 'absolute', top: 2, left: 2, bgcolor: 'rgba(255,255,255,.85)', p: 0.25, '&:hover': { bgcolor: '#fff' } }} />
                </Tooltip>
              )}
              {checked && state.selected[0] === img.reference && (
                <span className="absolute right-1.5 top-1.5 inline-flex items-center gap-0.5 rounded bg-black/65 px-1.5 py-0.5 text-[11px] font-semibold text-white"><StarRounded sx={{ fontSize: 12 }} />First</span>
              )}
              {img.attribution && (
                <figcaption className="absolute inset-x-0 bottom-0 truncate bg-black/55 px-1.5 py-0.5 text-[10px] text-white/90">
                  Photo: {img.attributionUrl ? <a href={img.attributionUrl} target="_blank" rel="noreferrer nofollow" className="hover:underline">{img.attribution}</a> : img.attribution}
                </figcaption>
              )}
            </figure>
          );
        })}
      </div>
    </section>
  );
}
