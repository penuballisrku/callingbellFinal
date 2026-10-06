import { useEffect, useState } from 'react';
import { Alert, Button, TextField } from '@mui/material';
import CheckCircleRounded from '@mui/icons-material/CheckCircleRounded';
import { ApiError, api, errorMessage } from '@/lib/api';

export type OtpPurpose = 'SignIn' | 'SignUp';
export interface OtpChallenge { maskedPhone: string; expiresInSeconds: number; resendInSeconds: number; developmentCode?: string | null }
export interface PhoneVerificationResult { verificationToken: string; expiresAt: string }

export const OTP_LENGTH = 6;
export const MOBILE_PATTERN = /^(\+91[\s-]?)?[6-9]\d{4}[\s-]?\d{5}$/;
/** The 10 significant digits, for comparing numbers typed in different formats. */
export const phoneDigits = (value: string) => value.replace(/\D/g, '').slice(-10);
/** The first message for `field` in a 400 validation response, if any. */
export const fieldError = (e: unknown, field: string) => (e instanceof ApiError ? e.errors?.[field]?.[0] : undefined);

/** Sends codes and tracks the resend countdown. Errors are thrown to the caller so field errors can be shown in place. */
export function useOtpChallenge(purpose: OtpPurpose) {
  const [challenge, setChallenge] = useState<OtpChallenge | null>(null);
  const [sending, setSending] = useState(false);
  const [cooldown, setCooldown] = useState(0);

  useEffect(() => {
    if (cooldown <= 0) return;
    const t = setTimeout(() => setCooldown((c) => c - 1), 1000);
    return () => clearTimeout(t);
  }, [cooldown]);

  const send = async (phoneNumber: string) => {
    setSending(true);
    try {
      const { data } = await api.post<OtpChallenge>('/api/auth/otp/send', { phoneNumber, purpose });
      setChallenge(data);
      setCooldown(data.resendInSeconds);
      return data;
    } finally {
      setSending(false);
    }
  };

  const reset = () => { setChallenge(null); setCooldown(0); };
  return { challenge, sending, cooldown, send, reset };
}

export function OtpCodeField({ value, onChange, onEnter, error, disabled, autoFocus = true }: {
  value: string; onChange: (code: string) => void; error?: string; disabled?: boolean; autoFocus?: boolean;
  /** Called on Enter instead of submitting a surrounding form. */
  onEnter?: () => void;
}) {
  return (
    <TextField label="Verification code" value={value} autoFocus={autoFocus} disabled={disabled} required
      onChange={(e) => onChange(e.target.value.replace(/\D/g, '').slice(0, OTP_LENGTH))}
      onKeyDown={onEnter ? (e) => { if (e.key === 'Enter') { e.preventDefault(); onEnter(); } } : undefined}
      error={!!error} helperText={error ?? `Enter the ${OTP_LENGTH}-digit code from the SMS`}
      slotProps={{ htmlInput: { inputMode: 'numeric', autoComplete: 'one-time-code', maxLength: OTP_LENGTH, 'aria-label': 'Verification code', style: { letterSpacing: '0.4em', fontVariantNumeric: 'tabular-nums' } } }} />
  );
}

/** "Code sent to +91 ••••• •3210 · Change number · Resend in 24s", plus the code itself in local development. */
export function OtpSentNote({ challenge, cooldown, sending, onResend, onChangeNumber }: {
  challenge: OtpChallenge; cooldown: number; sending: boolean; onResend: () => void; onChangeNumber: () => void;
}) {
  return (
    <div className="space-y-3">
      <p className="text-sm text-muted">
        We sent a code to <span className="font-semibold text-ink">{challenge.maskedPhone}</span>.{' '}
        <button type="button" onClick={onChangeNumber} className="font-semibold text-ink underline-offset-2 hover:underline">Change number</button>
        <span aria-hidden> · </span>
        {cooldown > 0
          ? <span aria-live="polite">Resend in {cooldown}s</span>
          : <button type="button" onClick={onResend} disabled={sending} className="font-semibold text-ink underline-offset-2 hover:underline disabled:opacity-60">{sending ? 'Sending…' : 'Resend code'}</button>}
      </p>
      {challenge.developmentCode && (
        <Alert severity="info" variant="outlined">Development mode: no SMS is sent. Your code is <strong>{challenge.developmentCode}</strong>.</Alert>
      )}
    </div>
  );
}

/**
 * Inline mobile-number verification for sign-up forms. Sends a code to `phoneNumber`, verifies it, and hands the
 * verification token to `onVerified`; the parent sends that token with the registration request.
 * The parent owns the phone field and must clear the token when the number changes.
 */
export function PhoneVerification({ phoneNumber, verified, validatePhone, onVerified, onPhoneError }: {
  phoneNumber: string;
  verified: boolean;
  /** Validates the phone field (showing its error) before a code is sent. */
  validatePhone: () => Promise<boolean>;
  onVerified: (token: string, phoneNumber: string) => void;
  onPhoneError: (message: string) => void;
}) {
  const { challenge, sending, cooldown, send, reset } = useOtpChallenge('SignUp');
  const [code, setCode] = useState('');
  const [codeError, setCodeError] = useState<string>();
  const [error, setError] = useState<string | null>(null);
  const [verifying, setVerifying] = useState(false);
  const digits = phoneDigits(phoneNumber);

  // A different number needs a new code.
  useEffect(() => { reset(); setCode(''); setCodeError(undefined); setError(null); }, [digits]); // eslint-disable-line react-hooks/exhaustive-deps

  const sendCode = async () => {
    setError(null);
    if (!(await validatePhone())) return;
    try {
      await send(phoneNumber);
      setCode('');
      setCodeError(undefined);
    } catch (e) {
      const phoneMsg = fieldError(e, 'phoneNumber');
      if (phoneMsg) onPhoneError(phoneMsg); else setError(errorMessage(e));
    }
  };

  const verify = async () => {
    if (code.length !== OTP_LENGTH) { setCodeError(`Enter the ${OTP_LENGTH}-digit code`); return; }
    setVerifying(true);
    setCodeError(undefined);
    try {
      const { data } = await api.post<PhoneVerificationResult>('/api/auth/otp/verify', { phoneNumber, code });
      onVerified(data.verificationToken, phoneNumber);
      reset();
    } catch (e) {
      const msg = fieldError(e, 'code');
      if (msg) setCodeError(msg); else setError(errorMessage(e));
    } finally {
      setVerifying(false);
    }
  };

  if (verified) {
    return (
      <p className="flex items-center gap-1.5 text-sm font-medium text-success" role="status">
        <CheckCircleRounded sx={{ fontSize: 18 }} aria-hidden /> Mobile number verified
      </p>
    );
  }

  return (
    <div className="space-y-3 rounded-xl border border-line bg-canvas p-4">
      {error && <Alert severity="error">{error}</Alert>}
      {!challenge ? (
        <div className="flex flex-wrap items-center justify-between gap-3">
          <p className="text-sm text-muted">We'll text a one-time code to verify this number. You'll use it to sign in.</p>
          <Button variant="outlined" onClick={() => void sendCode()} disabled={sending}>{sending ? 'Sending…' : 'Send code'}</Button>
        </div>
      ) : (
        <>
          <OtpSentNote challenge={challenge} cooldown={cooldown} sending={sending} onResend={() => void sendCode()} onChangeNumber={() => { reset(); setCode(''); }} />
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start">
            <div className="flex-1">
              <OtpCodeField value={code} onChange={(v) => { setCode(v); setCodeError(undefined); }} onEnter={() => void verify()} error={codeError} disabled={verifying} />
            </div>
            <Button variant="contained" onClick={() => void verify()} disabled={verifying} sx={{ height: 56, minWidth: 120 }}>
              {verifying ? 'Verifying…' : 'Verify'}
            </Button>
          </div>
        </>
      )}
    </div>
  );
}
