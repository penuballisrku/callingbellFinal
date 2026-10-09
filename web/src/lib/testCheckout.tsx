import { useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { moneyExact } from './format';
import type { CheckoutOrder } from './types';

/** What the test checkout returns: a payment (with the method picked), a simulated decline, or closed without paying. */
export type TestCheckoutResult =
  | { status: 'paid'; paymentId: string; signature: string }
  | { status: 'failed'; reason: string }
  | { status: 'cancelled' };

/** Must match TestPaymentGateway on the server. */
const SIGNATURE = 'test_signature';
const METHODS = [
  { id: 'upi', label: 'UPI' },
  { id: 'card', label: 'Card' },
  { id: 'netbanking', label: 'Net banking' },
  { id: 'wallet', label: 'Wallet' },
] as const;

/**
 * The development stand-in for Razorpay Checkout, used when the server's gateway is "Test" (no Razorpay keys, Payments:Test:Enabled).
 * No money moves; the server still verifies the payment and activates the plan exactly as it does for Razorpay.
 */
export function openTestCheckout(order: CheckoutOrder): Promise<TestCheckoutResult> {
  return new Promise((resolve) => {
    const host = document.createElement('div');
    document.body.appendChild(host);
    const root = createRoot(host);
    const done = (result: TestCheckoutResult) => {
      root.unmount();
      host.remove();
      resolve(result);
    };
    root.render(<TestCheckout order={order} onDone={done} />);
  });
}

function TestCheckout({ order, onDone }: { order: CheckoutOrder; onDone: (r: TestCheckoutResult) => void }) {
  const [method, setMethod] = useState<(typeof METHODS)[number]['id']>('upi');
  const payRef = useRef<HTMLButtonElement>(null);
  const amount = moneyExact(order.total, order.currency);
  const orderHex = order.gatewayOrderId.replace(/^order_test_/, '');

  useEffect(() => {
    payRef.current?.focus();
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onDone({ status: 'cancelled' }); };
    document.addEventListener('keydown', onKey);
    const overflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => { document.removeEventListener('keydown', onKey); document.body.style.overflow = overflow; };
  }, [onDone]);

  return (
    <div className="fixed inset-0 z-[2000] grid place-items-center bg-black/50 p-4" role="dialog" aria-modal="true" aria-labelledby="test-checkout-title">
      <div className="w-full max-w-sm rounded-2xl border border-line bg-surface p-5 text-ink shadow-xl">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h2 id="test-checkout-title" className="text-base font-semibold">Calling Bell</h2>
            <p className="truncate text-sm text-muted">{order.planName} plan · {order.billingCycle}</p>
          </div>
          <span className="shrink-0 rounded-full bg-accent-soft px-2.5 py-1 text-[11px] font-semibold uppercase tracking-wide text-accent-ink">Test mode</span>
        </div>

        <p className="mt-4 text-3xl font-bold tabular-nums">{amount}</p>
        <p className="mt-1 text-xs text-muted">Order {order.orderNumber} · {order.businessName}</p>

        <fieldset className="mt-4">
          <legend className="text-xs font-semibold uppercase tracking-wide text-muted">Pay with</legend>
          <div className="mt-2 grid grid-cols-2 gap-2">
            {METHODS.map((m) => (
              <label key={m.id}
                className={`flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-2 text-sm ${method === m.id ? 'border-accent bg-accent-soft' : 'border-line'}`}>
                <input type="radio" name="test-method" value={m.id} checked={method === m.id} onChange={() => setMethod(m.id)} className="accent-[var(--cb-accent)]" />
                {m.label}
              </label>
            ))}
          </div>
        </fieldset>

        <p className="mt-4 rounded-lg bg-subtle px-3 py-2 text-xs text-muted">
          This is a simulated payment for testing. No money is charged. Set Razorpay keys on the server to use real checkout.
        </p>

        <div className="mt-4 flex flex-col gap-2">
          <button ref={payRef} type="button"
            onClick={() => onDone({ status: 'paid', paymentId: `pay_test_${method}_${orderHex}`, signature: SIGNATURE })}
            className="h-11 rounded-lg bg-accent font-semibold text-on-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent">
            Pay {amount}
          </button>
          <div className="grid grid-cols-2 gap-2">
            <button type="button" onClick={() => onDone({ status: 'failed', reason: 'Payment declined by the bank (simulated test failure).' })}
              className="h-10 rounded-lg border border-line text-sm font-medium hover:bg-subtle">
              Simulate failure
            </button>
            <button type="button" onClick={() => onDone({ status: 'cancelled' })}
              className="h-10 rounded-lg border border-line text-sm font-medium hover:bg-subtle">
              Cancel
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
