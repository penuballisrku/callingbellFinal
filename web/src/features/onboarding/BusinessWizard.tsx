import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router';
import { FormProvider, useForm, useWatch, type FieldPath } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, Button, LinearProgress, Skeleton } from '@mui/material';
import ArrowBackRounded from '@mui/icons-material/ArrowBackRounded';
import ArrowForwardRounded from '@mui/icons-material/ArrowForwardRounded';
import CheckRounded from '@mui/icons-material/CheckRounded';
import CheckCircleRounded from '@mui/icons-material/CheckCircleRounded';
import ErrorOutlineRounded from '@mui/icons-material/ErrorOutlineRounded';
import RadioButtonUncheckedRounded from '@mui/icons-material/RadioButtonUncheckedRounded';
import AutorenewRounded from '@mui/icons-material/AutorenewRounded';
import StorefrontOutlined from '@mui/icons-material/StorefrontOutlined';
import { useSnackbar } from 'notistack';
import { ApiError, api, errorMessage } from '@/lib/api';
import { useCities, useCityAreas, useDocumentTitle, useLookup, usePlans } from '@/lib/hooks';
import { MEDIA_LIMITS, checkFile, releasePending, toPending, uploadMedia, type PendingMedia } from '@/lib/media';
import { payForPlan, priceBreakdown, usePaymentConfig } from '@/lib/payments';
import { moneyPrecise } from '@/lib/format';
import type { BusinessRegistrationResult, Category, CreatedBusiness, MediaKind } from '@/lib/types';
import { useAuth } from '@/stores/auth';
import { useSelectedBusiness } from '@/stores/ownerBusiness';
import { ErrorState } from '@/components/ui';
import { phoneDigits } from '@/features/auth/otp';
import { STEPS, defaultValues, makeWizardSchema, toFormPath, type WizardForm } from './schema';
import { AccountStep, BusinessStep, ContactStep, MediaStep, OfferStep, PaymentStep, ReviewStep, type MediaState, type WizardData } from './steps';
import type { StepKey } from './schema';

const DRAFT_KEY = 'cb-business-wizard';

type Task = { key: string; label: string; status: 'pending' | 'active' | 'done' | 'failed'; detail?: string };

/**
 * Multi-step business registration. `register` creates the owner account and the business together;
 * `setup` is for a signed-in owner who has no business yet.
 */
export default function BusinessWizard({ mode }: { mode: 'register' | 'setup' }) {
  useDocumentTitle(mode === 'register' ? 'List your business' : 'Set up your business');
  const categories = useQuery({ queryKey: ['categories'], queryFn: () => api.get<Category[]>('/api/categories'), staleTime: 30 * 60_000 });
  // Priced for the visitor's country (the cities offered are in it too); checkout charges the same in that currency.
  const plans = usePlans();
  const cities = useCities();
  const serviceTypes = useLookup('ServiceType');
  const socialPlatforms = useLookup('SocialPlatform');
  const payments = usePaymentConfig();

  if (categories.isError || plans.isError || cities.isError) {
    return <div className="container-page py-16"><ErrorState onRetry={() => { void categories.refetch(); void plans.refetch(); void cities.refetch(); }} /></div>;
  }
  if (!categories.data || !plans.data || !cities.data || !serviceTypes.length || !socialPlatforms.length || payments.isLoading) return <WizardSkeleton />;
  return <Wizard mode={mode} paymentsEnabled={!!payments.data?.enabled}
    data={{ categories: categories.data, plans: plans.data, cities: cities.data, serviceTypes, socialPlatforms }} />;
}

function loadDraft(mode: string): WizardForm | null {
  try {
    const raw = sessionStorage.getItem(DRAFT_KEY);
    const draft = raw ? (JSON.parse(raw) as WizardForm) : null;
    return draft?.mode === mode ? { ...draft, phoneVerificationToken: '', verifiedPhone: '', acceptTerms: false } : null;
  } catch { return null; }
}

/**
 * Fills empty fields from the link's query string (?name=&phone=&website=&address=), e.g. from "Join Calling Bell" on a search result.
 * Phone numbers are reduced to 10 digits when they are Indian mobile/landline numbers written with +91 or a leading 0.
 */
function withPrefill(form: WizardForm, params: URLSearchParams): WizardForm {
  const get = (key: string) => params.get(key)?.trim().slice(0, 300) || '';
  const name = get('name'), phone = get('phone'), website = get('website'), address = get('address');
  if (!name && !phone && !website && !address) return form;
  let digits = phone.replace(/\D/g, '');
  if (digits.length === 12 && digits.startsWith('91')) digits = digits.slice(2);
  else if (digits.length === 11 && digits.startsWith('0')) digits = digits.slice(1);
  const b = form.business;
  const fill = (current: string, value: string) => current || value;
  return {
    ...form,
    business: {
      ...b,
      businessName: fill(b.businessName, name.slice(0, 150)),
      businessPhone: fill(b.businessPhone, digits.length === 10 ? digits : phone),
      whatsAppNumber: fill(b.whatsAppNumber, /^[6-9]\d{9}$/.test(digits) ? digits : ''),
      website: fill(b.website, /^https?:\/\//i.test(website) ? website : ''),
      addressLine: fill(b.addressLine, address),
      pincode: fill(b.pincode, address.match(/\b\d{6}\b/)?.[0] ?? ''),
    },
  };
}

function Wizard({ mode, data, paymentsEnabled }: { mode: 'register' | 'setup'; data: WizardData; paymentsEnabled: boolean }) {
  const user = useAuth((s) => s.user);
  const setSession = useAuth((s) => s.setSession);
  const selectBusiness = useSelectedBusiness((s) => s.set);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const [params] = useSearchParams();
  // "Join Calling Bell" on a Google Maps / OpenStreetMap result links here with what is known about the business; it only fills
  // fields that are still empty, so a saved draft is never overwritten.
  const [restored] = useState(() => loadDraft(mode));
  const [initial] = useState(() => withPrefill(restored ?? defaultValues(mode, user ?? undefined), params));
  const form = useForm<WizardForm>({
    resolver: zodResolver(makeWizardSchema(mode)),
    defaultValues: initial,
    mode: 'onTouched',
  });
  const [step, setStep] = useState(0);
  const [furthest, setFurthest] = useState(0);
  const [emailStatus, setEmailStatus] = useState<'idle' | 'checking' | 'available' | 'taken'>('idle');
  const [formError, setFormError] = useState<string | null>(null);
  const top = useRef<HTMLDivElement>(null);

  // ---- Media picked in the browser (uploaded after the business exists) ----
  const [media, setMedia] = useState<PendingMedia[]>([]);
  const [mediaErrors, setMediaErrors] = useState<string[]>([]);
  const mediaRef = useRef(media);
  mediaRef.current = media;
  useEffect(() => () => mediaRef.current.forEach(releasePending), []);
  const planCode = useWatch({ control: form.control, name: 'business.planCode' });
  const billingCycle = useWatch({ control: form.control, name: 'business.billingCycle' });
  const plan = data.plans.find((p) => p.code === planCode);
  const isPaidPlan = !!plan && plan.monthlyPrice > 0;
  const orderTotal = plan ? priceBreakdown(billingCycle === 'Annual' ? plan.annualPrice : plan.monthlyPrice, plan).total : 0;
  // The payment step comes right after plan selection, and only for paid plans.
  const steps = useMemo(() => STEPS.filter((s) => (mode === 'register' || s.key !== 'account') && (isPaidPlan || s.key !== 'payment')), [mode, isPaidPlan]);
  const goToKey = (key: StepKey) => { const i = steps.findIndex((s) => s.key === key); if (i >= 0) goTo(i); };

  const addMedia = useCallback(async (kind: MediaKind, files: File[]) => {
    const errors: string[] = [];
    const current = mediaRef.current;
    const limit = kind === 'photo' ? (plan?.maxImages ?? 5) : kind === 'video' ? MEDIA_LIMITS.maxVideos : 1;
    const existing = current.filter((m) => m.kind === kind).length;
    const room = kind === 'logo' || kind === 'cover' ? 1 : Math.max(0, limit - existing);
    const accepted: File[] = [];
    for (const f of files) {
      const err = checkFile(f, kind);
      if (err) errors.push(err);
      else if (accepted.length < room) accepted.push(f);
      else errors.push(`${f.name} wasn't added: the limit is ${limit} ${kind === 'video' ? 'videos' : 'photos'}.`);
    }
    setMediaErrors(errors);
    const items = await Promise.all(accepted.map((f) => toPending(f, kind)));
    setMedia((prev) => {
      if (kind === 'logo' || kind === 'cover') {
        prev.filter((m) => m.kind === kind).forEach(releasePending);
        return [...prev.filter((m) => m.kind !== kind), ...items];
      }
      return [...prev, ...items];
    });
  }, [plan?.maxImages]);

  const mediaState: MediaState = {
    items: media,
    errors: mediaErrors,
    add: (kind, files) => { void addMedia(kind, files); },
    remove: (id) => setMedia((prev) => { const gone = prev.find((m) => m.id === id); if (gone) releasePending(gone); return prev.filter((m) => m.id !== id); }),
    rename: (id, title) => setMedia((prev) => prev.map((m) => (m.id === id ? { ...m, title } : m))),
  };

  // ---- Draft persistence (per tab; never stores the phone verification token or files) ----
  useEffect(() => {
    let t: ReturnType<typeof setTimeout>;
    const sub = form.watch((values) => {
      clearTimeout(t);
      t = setTimeout(() => {
        try { sessionStorage.setItem(DRAFT_KEY, JSON.stringify({ ...values, phoneVerificationToken: '', verifiedPhone: '' })); } catch { /* storage unavailable */ }
      }, 500);
    });
    return () => { clearTimeout(t); sub.unsubscribe(); };
  }, [form]);

  const checkEmail = async (email: string) => {
    if (mode !== 'register' || !/^\S+@\S+\.\S+$/.test(email)) { setEmailStatus('idle'); return; }
    setEmailStatus('checking');
    try {
      const available = await api.get<boolean>('/api/auth/email-available', { email: email.trim() });
      setEmailStatus(available ? 'available' : 'taken');
    } catch { setEmailStatus('idle'); }
  };

  const goTo = (index: number) => {
    setStep(index);
    setFurthest((f) => Math.max(f, index));
    requestAnimationFrame(() => top.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  };

  /** The OTP verification token only counts for the number it was issued to. */
  const isPhoneVerified = () => {
    const { phoneVerificationToken, verifiedPhone, phoneNumber } = form.getValues();
    return !!phoneVerificationToken && verifiedPhone === phoneDigits(phoneNumber);
  };

  /** Validates the current step's fields (plus cross-field rules) before moving forward. */
  const validateStep = async (index: number) => {
    const current = steps[index]!;
    const ok = await form.trigger(current.fields as unknown as FieldPath<WizardForm>[], { shouldFocus: true });
    let extra = true;
    if (current.key === 'account') {
      if (ok && !isPhoneVerified()) { form.setError('phoneNumber', { message: 'Verify your mobile number to continue' }, { shouldFocus: true }); extra = false; }
      if (emailStatus === 'taken') { form.setError('email', { message: 'This email is already registered' }); extra = false; }
    }
    if (current.key === 'offer') {
      const count = form.getValues('business.services').length;
      if (plan && count > plan.maxServices) { form.setError('business.services', { message: `The ${plan.name} plan allows up to ${plan.maxServices} services` }); extra = false; }
      const photos = media.filter((m) => m.kind === 'photo').length;
      if (plan && photos > plan.maxImages) { setMediaErrors([`The ${plan.name} plan allows ${plan.maxImages} photos. Remove ${photos - plan.maxImages} on the next step.`]); }
    }
    if (current.key === 'media' && plan && media.filter((m) => m.kind === 'photo').length > plan.maxImages) {
      setMediaErrors([`The ${plan.name} plan allows up to ${plan.maxImages} photos. Remove some photos or choose a bigger plan.`]);
      extra = false;
    }
    return ok && extra;
  };

  const next = async () => {
    setFormError(null);
    if (await validateStep(step)) goTo(step + 1);
  };

  // ---- Submission ----
  const [submitting, setSubmitting] = useState(false);
  const [tasks, setTasks] = useState<Task[] | null>(null);
  const [created, setCreated] = useState<CreatedBusiness | null>(null);
  const [uploadPct, setUploadPct] = useState(0);
  useEffect(() => {
    if (!submitting) return;
    const warn = (e: BeforeUnloadEvent) => { e.preventDefault(); };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [submitting]);

  const patchTask = (key: string, patch: Partial<Task>) => setTasks((t) => t?.map((x) => (x.key === key ? { ...x, ...patch } : x)) ?? null);

  const runUploads = async (businessId: string, items: PendingMedia[]) => {
    const total = items.reduce((s, m) => s + m.file.size, 0) || 1;
    let doneBytes = 0;
    let failed = 0;
    const failedByKind: Partial<Record<MediaKind, number>> = {};
    for (const item of items) {
      const key = `media-${item.kind}`;
      patchTask(key, { status: 'active' });
      setMedia((prev) => prev.map((m) => (m.id === item.id ? { ...m, status: 'uploading', progress: 0 } : m)));
      try {
        await uploadMedia(businessId, item, (f) => {
          setUploadPct(Math.round(((doneBytes + f * item.file.size) / total) * 100));
          setMedia((prev) => prev.map((m) => (m.id === item.id ? { ...m, progress: f } : m)));
        });
        setMedia((prev) => prev.map((m) => (m.id === item.id ? { ...m, status: 'done', progress: 1 } : m)));
      } catch (e) {
        failed++;
        failedByKind[item.kind] = (failedByKind[item.kind] ?? 0) + 1;
        setMedia((prev) => prev.map((m) => (m.id === item.id ? { ...m, status: 'failed', error: errorMessage(e) } : m)));
      }
      doneBytes += item.file.size;
      setUploadPct(Math.round((doneBytes / total) * 100));
      const sameKind = items.filter((m) => m.kind === item.kind);
      if (sameKind[sameKind.length - 1]!.id === item.id) {
        const kindFailed = failedByKind[item.kind] ?? 0;
        patchTask(key, { status: kindFailed ? 'failed' : 'done', detail: kindFailed ? `${kindFailed} could not be uploaded` : undefined });
      }
    }
    return failed;
  };

  // ---- Payment (Razorpay) right after the business exists; uploads wait so the paid plan's limits apply ----
  const [paymentPrompt, setPaymentPrompt] = useState<{ reason: string; onRetry: () => void; onSkip: () => void } | null>(null);
  const runPayment = async (businessId: string, code: string, cycle: string, gstin: string) => {
    for (;;) {
      patchTask('payment', { status: 'active', detail: 'Complete the payment in the Razorpay window' });
      const outcome = await payForPlan(businessId, { planCode: code, billingCycle: cycle, gstin });
      if (outcome.status === 'paid') {
        patchTask('payment', { status: 'done', detail: `${outcome.result.planName} plan active · invoice ${outcome.result.invoiceNumber} · paid by ${outcome.result.paymentMethod}` });
        void queryClient.invalidateQueries({ queryKey: ['owner'] });
        return true;
      }
      patchTask('payment', { status: 'failed', detail: outcome.status === 'cancelled' ? 'Not completed' : 'Payment failed' });
      const choice = await new Promise<'retry' | 'skip'>((resolve) =>
        setPaymentPrompt({ reason: outcome.reason, onRetry: () => resolve('retry'), onSkip: () => resolve('skip') }));
      setPaymentPrompt(null);
      if (choice === 'skip') {
        patchTask('payment', { status: 'failed', detail: 'Skipped · your business is on the Free plan. You can pay any time from your dashboard.' });
        return false;
      }
    }
  };

  const finish = () => {
    try { sessionStorage.removeItem(DRAFT_KEY); } catch { /* ignore */ }
    navigate('/owner?welcome=1', { replace: true });
  };

  const onValid = async (v: WizardForm) => {
    setFormError(null);
    if (mode === 'register' && !isPhoneVerified()) {
      form.setError('phoneNumber', { message: 'Verify your mobile number to continue' });
      goToKey('account');
      return;
    }
    setSubmitting(true);
    const b = v.business;
    const business = {
      ...b,
      tagline: b.tagline || null, yearEstablished: b.yearEstablished ? Number(b.yearEstablished) : null, teamSize: b.teamSize ? Number(b.teamSize) : null,
      languages: b.languages || null, whatsAppNumber: b.whatsAppNumber || null, businessEmail: b.businessEmail || null, website: b.website || null,
      areaSlug: b.areaSlug || null, landmark: b.landmark || null,
      services: b.services.map((s) => ({ name: s.name, description: s.description || null, price: Number(s.price), priceUnit: s.priceUnit || null, durationMinutes: Number(s.durationMinutes), type: s.type })),
      socialLinks: b.socialLinks.filter((l) => l.url.trim()),
    };
    const order: MediaKind[] = ['logo', 'cover', 'photo', 'video'];
    const items = [...media].sort((a, b2) => order.indexOf(a.kind) - order.indexOf(b2.kind));
    const kindLabel: Record<MediaKind, string> = { logo: 'Uploading logo', cover: 'Uploading cover image', photo: 'Uploading photos', video: 'Uploading videos' };
    setTasks([
      ...(mode === 'register' ? [{ key: 'account', label: 'Creating your account', status: 'active' as const }] : []),
      { key: 'business', label: `Creating ${b.businessName}`, status: mode === 'register' ? 'pending' : 'active' },
      ...(isPaidPlan && paymentsEnabled ? [{ key: 'payment', label: `Payment · ${plan!.name} ${b.billingCycle.toLowerCase()} (${moneyPrecise(orderTotal, plan)})`, status: 'pending' as const }] : []),
      ...order.filter((k) => items.some((m) => m.kind === k)).map((k) => ({
        key: `media-${k}`, label: kindLabel[k], status: 'pending' as const,
        detail: k === 'photo' || k === 'video' ? `${items.filter((m) => m.kind === k).length} file(s)` : undefined,
      })),
    ]);

    let result: CreatedBusiness;
    try {
      if (mode === 'register') {
        const res = await api.post<BusinessRegistrationResult>('/api/auth/register-business', {
          displayName: v.displayName, email: v.email, phoneNumber: v.phoneNumber, phoneVerificationToken: v.phoneVerificationToken, business,
        });
        setSession(res.data.auth);
        result = res.data.business;
        patchTask('account', { status: 'done' });
      } else {
        result = (await api.post<CreatedBusiness>('/api/owner/businesses', { business })).data;
      }
      patchTask('business', { status: 'done', detail: result.requestedPlanCode && !paymentsEnabled ? 'Started on the Free plan · upgrade any time from Plan & billing' : undefined });
    } catch (e) {
      setSubmitting(false);
      setTasks(null);
      handleServerError(e);
      return;
    }

    setCreated(result);
    selectBusiness(result.businessId);
    void queryClient.invalidateQueries({ queryKey: ['owner'] });
    if (result.requestedPlanCode && paymentsEnabled) await runPayment(result.businessId, result.requestedPlanCode, b.billingCycle, v.gstin);
    const failed = items.length ? await runUploads(result.businessId, items) : 0;
    setSubmitting(false);
    if (failed === 0) {
      enqueueSnackbar('Your business profile has been created', { variant: 'success' });
      setTimeout(finish, 900);
    }
  };

  const handleServerError = (e: unknown) => {
    if (e instanceof ApiError && e.status === 409) {
      form.setError('email', { message: e.message });
      setEmailStatus('taken');
      goTo(0);
      return;
    }
    if (e instanceof ApiError && e.errors) {
      // Expired, used or already-registered number: the owner must verify (another) number again.
      if (e.errors.phoneNumber) { form.setValue('phoneVerificationToken', ''); form.setValue('verifiedPhone', ''); }
      const paths = Object.entries(e.errors).map(([key, msgs]) => {
        const path = toFormPath(key);
        form.setError(path as FieldPath<WizardForm>, { message: msgs[0] });
        return path;
      });
      const target = steps.findIndex((s) => s.fields.some((f) => paths.some((p) => p === f || p.startsWith(`${f}.`))));
      setFormError('Please correct the highlighted fields.');
      if (target >= 0) goTo(target);
      return;
    }
    setFormError(errorMessage(e));
  };

  const onInvalid = () => {
    const errs = form.formState.errors as Record<string, unknown>;
    const flat = (obj: unknown, prefix = ''): string[] =>
      obj && typeof obj === 'object' && !('message' in (obj as object))
        ? Object.entries(obj as Record<string, unknown>).flatMap(([k, val]) => flat(val, prefix ? `${prefix}.${k}` : k))
        : [prefix];
    const paths = flat(errs);
    const target = steps.findIndex((s) => s.fields.some((f) => paths.some((p) => p === f || p.startsWith(`${f}.`))));
    if (target >= 0 && target !== step) goTo(target);
  };

  if (tasks) {
    return (
      <SubmissionProgress tasks={tasks} uploadPct={uploadPct} media={media} created={created} submitting={submitting} paymentPrompt={paymentPrompt}
        onRetry={async () => {
          if (!created) return;
          setSubmitting(true);
          const failedItems = media.filter((m) => m.status === 'failed');
          const failed = await runUploads(created.businessId, failedItems);
          setSubmitting(false);
          if (failed === 0) finish();
        }}
        onContinue={finish} />
    );
  }

  const current = steps[step]!;
  const isLast = step === steps.length - 1;

  return (
    <FormProvider {...form}>
      <div ref={top} className="container-page scroll-mt-20 py-8 md:py-12">
        <div className="mx-auto max-w-6xl">
          <div className="mb-6 md:mb-8">
            <p className="text-xs font-semibold uppercase tracking-[0.12em] text-accent-ink">{mode === 'register' ? 'List your business' : 'Business setup'}</p>
            <h1 className="mt-1 text-2xl font-bold tracking-tight md:text-3xl">{mode === 'register' ? 'Create your business account' : 'Set up your business profile'}</h1>
            <p className="mt-1 text-muted">Takes about 5 minutes. Your progress is saved in this tab.</p>
          </div>

          <Stepper steps={steps.map((s) => s.label)} current={step} furthest={furthest} onSelect={(i) => { if (i <= furthest) goTo(i); }} />

          <div className="mt-6 grid gap-6 lg:grid-cols-[1fr_320px]">
            <form noValidate onSubmit={(e) => { e.preventDefault(); if (isLast) void form.handleSubmit(onValid, onInvalid)(); else void next(); }} className="card min-w-0">
              <div className="border-b border-line px-5 py-4 md:px-7 md:py-5">
                <div className="text-xs font-semibold text-muted">Step {step + 1} of {steps.length}</div>
                <h2 className="mt-0.5 text-lg font-bold md:text-xl">{current.title}</h2>
                <p className="mt-0.5 text-sm text-muted">{current.subtitle}</p>
              </div>
              <div className="px-5 py-6 md:px-7">
                {formError && <Alert severity="error" sx={{ mb: 3 }}>{formError}</Alert>}
                {restored && step === 0 && <Alert severity="info" sx={{ mb: 3 }}>We restored the details you entered earlier in this tab.{mode === 'register' ? ' Please verify your mobile number again.' : ''}</Alert>}
                {current.key === 'account' && <AccountStep emailStatus={emailStatus} onEmailBlur={(e) => void checkEmail(e)} />}
                {current.key === 'business' && <BusinessStep data={data} />}
                {current.key === 'contact' && <ContactStep data={data} />}
                {current.key === 'offer' && <OfferStep data={data} />}
                {current.key === 'media' && <MediaStep data={data} media={mediaState} />}
                {current.key === 'payment' && <PaymentStep data={data} paymentsEnabled={paymentsEnabled} onChangePlan={() => goToKey('offer')} />}
                {current.key === 'review' && <ReviewStep data={data} media={mediaState} onEdit={goToKey} paymentsEnabled={paymentsEnabled} />}
              </div>
              <div className="sticky bottom-0 z-10 flex items-center justify-between gap-3 rounded-b-xl border-t border-line bg-surface/95 px-5 py-3.5 backdrop-blur md:px-7">
                <Button onClick={() => goTo(step - 1)} disabled={step === 0} startIcon={<ArrowBackRounded />} color="inherit">Back</Button>
                <Button type="submit" variant="contained" color={isLast ? 'secondary' : 'primary'} size="large"
                  endIcon={isLast ? <CheckRounded /> : <ArrowForwardRounded />} disabled={emailStatus === 'checking'}>
                  {isLast
                    ? `${mode === 'register' ? 'Create account' : 'Create business'}${isPaidPlan && paymentsEnabled ? ` & pay ${moneyPrecise(orderTotal, plan)}` : mode === 'register' ? ' & finish' : ''}`
                    : 'Continue'}
                </Button>
              </div>
            </form>
            <LivePreview data={data} media={media} />
          </div>
        </div>
      </div>
    </FormProvider>
  );
}

/* ---------------------------------------------------------------------------------------------- */

function Stepper({ steps, current, furthest, onSelect }: { steps: string[]; current: number; furthest: number; onSelect: (i: number) => void }) {
  const pct = Math.round(((current + 1) / steps.length) * 100);
  return (
    <nav aria-label="Registration progress">
      <div className="md:hidden">
        <div className="mb-1.5 flex justify-between text-sm"><span className="font-semibold">{steps[current]}</span><span className="text-muted">Step {current + 1} of {steps.length}</span></div>
        <LinearProgress variant="determinate" value={pct} sx={{ height: 6, borderRadius: 3, '& .MuiLinearProgress-bar': { bgcolor: 'var(--cb-accent)' } }} aria-label={`${pct}% complete`} />
      </div>
      <ol className="hidden items-center md:flex">
        {steps.map((label, i) => {
          const done = i < current;
          const active = i === current;
          const reachable = i <= furthest;
          return (
            <li key={label} className={`flex items-center ${i < steps.length - 1 ? 'flex-1' : ''}`} style={{ minWidth: 0 }}>
              <button type="button" onClick={() => onSelect(i)} disabled={!reachable} aria-current={active ? 'step' : undefined}
                className="flex items-center gap-2 rounded-lg px-1 py-1 text-left disabled:cursor-default">
                <span className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-sm font-bold transition-colors
                  ${active ? 'bg-accent text-on-accent shadow-[0_0_0_4px_var(--cb-ring)]' : done ? 'bg-inverse text-on-inverse' : 'border-2 border-line-strong text-muted'}`}>
                  {done ? <CheckRounded sx={{ fontSize: 18 }} /> : i + 1}
                </span>
                <span className={`whitespace-nowrap text-sm ${active ? 'font-semibold text-ink' : `hidden xl:inline ${done ? 'font-medium text-ink-2' : 'text-muted'}`}`}>{label}</span>
              </button>
              {i < steps.length - 1 && <span className={`mx-2 h-0.5 min-w-6 flex-1 rounded lg:mx-3 ${i < current ? 'bg-inverse' : 'bg-line'}`} aria-hidden />}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

function LivePreview({ data, media }: { data: WizardData; media: PendingMedia[] }) {
  const v = useWatch<WizardForm>() as WizardForm;
  const b = v.business ?? ({} as WizardForm['business']);
  const category = data.categories.find((c) => c.slug === b.categorySlug);
  const sub = category?.subCategories.find((s) => s.slug === b.subCategorySlug);
  const city = data.cities.find((c) => c.slug === b.citySlug);
  const { areas: cityAreas } = useCityAreas(b.citySlug);
  const area = cityAreas.find((a) => a.slug === b.areaSlug) ?? city?.areas.find((a) => a.slug === b.areaSlug);
  const plan = data.plans.find((p) => p.code === b.planCode);
  const logo = media.find((m) => m.kind === 'logo');
  const cover = media.find((m) => m.kind === 'cover') ?? media.find((m) => m.kind === 'photo');
  const photos = media.filter((m) => m.kind === 'photo').length;
  const videos = media.filter((m) => m.kind === 'video').length;

  return (
    <aside className="hidden lg:block">
      <div className="sticky top-24 space-y-4">
        <div className="eyebrow">Live preview</div>
        <div className="card overflow-hidden">
          <div className="relative aspect-[16/7] bg-subtle">
            {cover && <img src={cover.previewUrl} alt="" className="h-full w-full object-cover" />}
            <div className="absolute -bottom-6 left-4 flex h-12 w-12 items-center justify-center overflow-hidden rounded-xl border-2 border-surface bg-surface">
              {logo ? <img src={logo.previewUrl} alt="" className="h-full w-full object-contain" /> : <StorefrontOutlined className="text-faint" />}
            </div>
          </div>
          <div className="px-4 pb-4 pt-8">
            <div className="truncate font-semibold">{b.businessName?.trim() || 'Your business name'}</div>
            <div className="truncate text-[13px] text-muted">{[sub?.name ?? category?.name, area?.name, city?.name].filter(Boolean).join(' · ') || 'Category · City'}</div>
            {b.tagline && <p className="mt-2 line-clamp-2 text-sm text-ink-2">{b.tagline}</p>}
            <div className="mt-3 flex flex-wrap gap-1.5 text-[11px] font-semibold">
              {plan && <span className="rounded bg-accent-soft px-1.5 py-0.5 text-accent-ink">{plan.name} plan</span>}
              {(b.services?.length ?? 0) > 0 && b.services[0]?.name && <span className="rounded bg-subtle px-1.5 py-0.5 text-muted">{b.services.length} service{b.services.length === 1 ? '' : 's'}</span>}
              {photos > 0 && <span className="rounded bg-subtle px-1.5 py-0.5 text-muted">{photos} photo{photos === 1 ? '' : 's'}</span>}
              {videos > 0 && <span className="rounded bg-subtle px-1.5 py-0.5 text-muted">{videos} video{videos === 1 ? '' : 's'}</span>}
            </div>
          </div>
        </div>
        <div className="rounded-xl border border-line p-4 text-sm">
          <div className="font-semibold">What happens next</div>
          <ol className="mt-2 space-y-2 text-muted">
            <li>1. Your profile is created and your media uploads.</li>
            <li>2. Our team reviews your listing, usually within one working day.</li>
            <li>3. Once approved, customers can find, call and book you.</li>
          </ol>
        </div>
      </div>
    </aside>
  );
}

function SubmissionProgress({ tasks, uploadPct, media, created, submitting, paymentPrompt, onRetry, onContinue }: {
  tasks: Task[]; uploadPct: number; media: PendingMedia[]; created: CreatedBusiness | null; submitting: boolean;
  paymentPrompt: { reason: string; onRetry: () => void; onSkip: () => void } | null; onRetry: () => void; onContinue: () => void;
}) {
  const failed = media.filter((m) => m.status === 'failed');
  const allDone = !submitting && created && failed.length === 0;
  const hasUploads = media.length > 0;
  const overall = Math.round(((tasks.filter((t) => t.status === 'done').length + (hasUploads ? uploadPct / 100 : 0)) / Math.max(1, tasks.length)) * 100);
  return (
    <div className="container-page py-12 md:py-20">
      <div className="card mx-auto max-w-xl p-6 md:p-8" aria-live="polite">
        <div className="flex items-center gap-3">
          <span className={`flex h-11 w-11 items-center justify-center rounded-full ${allDone ? 'bg-success-soft text-success' : 'bg-accent-soft text-accent-ink'}`}>
            {allDone ? <CheckCircleRounded /> : <AutorenewRounded className={submitting ? 'motion-safe:animate-spin' : ''} />}
          </span>
          <div>
            <h1 className="text-xl font-bold">{allDone ? 'Your business is ready' : paymentPrompt ? 'Waiting for payment' : failed.length && !submitting ? 'Almost there' : 'Setting up your business…'}</h1>
            <p className="text-sm text-muted">{allDone ? 'Taking you to your dashboard…' : paymentPrompt ? 'Choose how you would like to continue.' : submitting ? 'Please keep this tab open.' : 'Some files could not be uploaded.'}</p>
          </div>
        </div>
        <LinearProgress variant="determinate" value={allDone ? 100 : overall} sx={{ mt: 3, height: 8, borderRadius: 4, '& .MuiLinearProgress-bar': { bgcolor: allDone ? 'var(--cb-success)' : 'var(--cb-accent)' } }} />
        <ul className="mt-5 space-y-3">
          {tasks.map((t) => (
            <li key={t.key} className="flex items-start gap-3 text-sm">
              <span className="mt-0.5">
                {t.status === 'done' ? <CheckCircleRounded sx={{ fontSize: 20 }} className="text-success" />
                  : t.status === 'failed' ? <ErrorOutlineRounded sx={{ fontSize: 20 }} className="text-danger" />
                    : t.status === 'active' ? <AutorenewRounded sx={{ fontSize: 20 }} className="text-accent motion-safe:animate-spin" />
                      : <RadioButtonUncheckedRounded sx={{ fontSize: 20 }} className="text-faint" />}
              </span>
              <span className="flex-1">
                <span className={t.status === 'pending' ? 'text-muted' : 'font-medium'}>{t.label}</span>
                {t.detail && <span className="block text-xs text-muted">{t.detail}</span>}
              </span>
              {t.key.startsWith('media-') && t.status === 'active' && <span className="tabular text-xs text-muted">{uploadPct}%</span>}
            </li>
          ))}
        </ul>
        {paymentPrompt && (
          <div className="mt-6 rounded-lg border border-warning-line bg-warning-soft p-4 text-sm text-warning-ink" role="alert">
            <p className="font-semibold">Payment not completed</p>
            <p className="mt-1">{paymentPrompt.reason}</p>
            <p className="mt-2">Your business has been created on the Free plan. You have not been charged for this attempt.</p>
            <div className="mt-3 flex flex-wrap gap-2">
              <Button variant="contained" size="small" color="secondary" onClick={paymentPrompt.onRetry}>Try payment again</Button>
              <Button size="small" color="inherit" onClick={paymentPrompt.onSkip}>Continue on the Free plan</Button>
            </div>
          </div>
        )}
        {!submitting && failed.length > 0 && (
          <div className="mt-6 rounded-lg border border-warning-line bg-warning-soft p-4 text-sm text-warning-ink">
            <p className="font-semibold">{failed.length} file{failed.length === 1 ? '' : 's'} could not be uploaded</p>
            <ul className="mt-1 list-disc pl-5">{failed.slice(0, 4).map((f) => <li key={f.id}>{f.file.name}: {f.error}</li>)}</ul>
            <p className="mt-2">Your business has been created. You can retry now or add these later from <strong>Photos & videos</strong>.</p>
            <div className="mt-3 flex gap-2">
              <Button variant="contained" size="small" onClick={onRetry}>Retry uploads</Button>
              <Button size="small" color="inherit" onClick={onContinue}>Go to dashboard</Button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function WizardSkeleton() {
  return (
    <div className="container-page py-12" aria-busy="true">
      <div className="mx-auto max-w-6xl space-y-6">
        <Skeleton width={320} height={40} />
        <Skeleton variant="rounded" height={40} />
        <div className="grid gap-6 lg:grid-cols-[1fr_320px]"><Skeleton variant="rounded" height={520} /><Skeleton variant="rounded" height={320} /></div>
      </div>
    </div>
  );
}
