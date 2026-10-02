import { useEffect, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material';
import CheckCircleRounded from '@mui/icons-material/CheckCircleRounded';
import LockOutlined from '@mui/icons-material/LockOutlined';
import { date, moneyPrecise } from '@/lib/format';
import { payForPlan, priceBreakdown } from '@/lib/payments';
import type { PaymentResult, Plan } from '@/lib/types';

const GSTIN = /^\d{2}[A-Z]{5}\d{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/;

/** Order summary + GSTIN, then Razorpay checkout. The plan activates only after the server verifies the payment. */
export function PlanCheckoutDialog({ businessId, plan, cycle, open, onClose }: {
  businessId: string; plan: Plan | null; cycle: 'Monthly' | 'Annual'; open: boolean; onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const [gstin, setGstin] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [paid, setPaid] = useState<PaymentResult | null>(null);
  useEffect(() => { if (open) { setError(null); setPaid(null); setBusy(false); } }, [open]);
  if (!plan) return null;

  const { subtotal, tax, total } = priceBreakdown(cycle === 'Annual' ? plan.annualPrice : plan.monthlyPrice);
  const gstinError = gstin && !GSTIN.test(gstin) ? 'Enter a valid 15-character GSTIN' : null;

  const pay = async () => {
    setBusy(true);
    setError(null);
    const outcome = await payForPlan(businessId, { planCode: plan.code, billingCycle: cycle, gstin: gstin || null });
    setBusy(false);
    if (outcome.status === 'paid') {
      setPaid(outcome.result);
      void queryClient.invalidateQueries({ queryKey: ['owner'] });
    } else {
      setError(outcome.status === 'cancelled' ? 'Payment was not completed. You have not been charged.' : outcome.reason);
    }
  };

  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} maxWidth="xs" fullWidth>
      {paid ? (
        <>
          <DialogContent sx={{ pt: 4, textAlign: 'center' }}>
            <span className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-success-soft text-success"><CheckCircleRounded /></span>
            <h2 className="mt-3 text-lg font-bold">Payment successful</h2>
            <p className="mt-1 text-sm text-muted">
              Your {paid.planName} plan is active{paid.activeUntil ? ` until ${date(paid.activeUntil)}` : ''}.
              {paid.activeFrom && new Date(paid.activeFrom) > new Date() ? ` It starts on ${date(paid.activeFrom)}, after your current period.` : ''}
            </p>
            <dl className="mt-4 space-y-1.5 rounded-lg bg-subtle p-3 text-left text-sm">
              <div className="flex justify-between"><dt className="text-muted">Amount paid</dt><dd className="font-semibold">{moneyPrecise(paid.total)}</dd></div>
              <div className="flex justify-between"><dt className="text-muted">Invoice</dt><dd className="font-mono text-xs">{paid.invoiceNumber}</dd></div>
              <div className="flex justify-between"><dt className="text-muted">Paid by</dt><dd>{paid.paymentMethod}</dd></div>
            </dl>
          </DialogContent>
          <DialogActions><Button variant="contained" onClick={onClose} fullWidth>Done</Button></DialogActions>
        </>
      ) : (
        <>
          <DialogTitle>Upgrade to {plan.name}</DialogTitle>
          <DialogContent>
            <dl className="space-y-2 rounded-lg border border-line p-3 text-sm">
              <div className="flex justify-between"><dt className="text-muted">{plan.name} · {cycle}</dt><dd className="tabular">{moneyPrecise(subtotal)}</dd></div>
              <div className="flex justify-between"><dt className="text-muted">GST (18%)</dt><dd className="tabular">{moneyPrecise(tax)}</dd></div>
              <div className="flex justify-between border-t border-line pt-2 font-bold"><dt>Total payable</dt><dd className="tabular">{moneyPrecise(total)}</dd></div>
            </dl>
            <div className="mt-4">
              <TextField label="GSTIN (optional)" value={gstin} onChange={(e) => setGstin(e.target.value.toUpperCase().trim())} error={!!gstinError}
                helperText={gstinError ?? 'Printed on your tax invoice'} slotProps={{ htmlInput: { maxLength: 15 } }} />
            </div>
            {error && <p className="mt-3 rounded-lg bg-danger-soft px-3 py-2 text-sm text-danger" role="alert">{error}</p>}
            <p className="mt-3 flex items-start gap-2 text-xs text-muted"><LockOutlined sx={{ fontSize: 14, mt: '1px' }} />Secure payment by Razorpay: UPI, cards, net banking or wallets. Your plan activates as soon as the payment is confirmed.</p>
          </DialogContent>
          <DialogActions>
            <Button onClick={onClose} disabled={busy} color="inherit">Cancel</Button>
            <Button variant="contained" color="secondary" onClick={() => void pay()} disabled={busy || !!gstinError}>
              {busy ? 'Waiting for payment…' : `Pay ${moneyPrecise(total)}`}
            </Button>
          </DialogActions>
        </>
      )}
    </Dialog>
  );
}
