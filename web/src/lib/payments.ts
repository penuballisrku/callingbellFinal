import { useQuery } from '@tanstack/react-query';
import { api, errorMessage } from './api';
import type { CheckoutOrder, PaymentConfig, PaymentResult } from './types';

/** Whether online checkout is available (Razorpay keys configured on the server). */
export function usePaymentConfig() {
  return useQuery({ queryKey: ['payments', 'config'], queryFn: () => api.get<PaymentConfig>('/api/payments/config'), staleTime: 10 * 60_000 });
}

export const GST_RATE = 0.18;
export const priceBreakdown = (subtotal: number) => {
  const tax = Math.round(subtotal * GST_RATE * 100) / 100;
  return { subtotal, tax, total: Math.round((subtotal + tax) * 100) / 100 };
};

/* ---------- Razorpay Checkout (https://razorpay.com/docs/payments/payment-gateway/web-integration/standard/) ---------- */

interface RazorpaySuccess { razorpay_payment_id: string; razorpay_order_id: string; razorpay_signature: string }
interface RazorpayFailure { error: { description?: string; reason?: string } }
interface RazorpayInstance { open(): void; on(event: 'payment.failed', cb: (r: RazorpayFailure) => void): void }
declare global { interface Window { Razorpay?: new (options: Record<string, unknown>) => RazorpayInstance } }

const SCRIPT = 'https://checkout.razorpay.com/v1/checkout.js';
let loading: Promise<void> | null = null;

function loadCheckout(): Promise<void> {
  if (window.Razorpay) return Promise.resolve();
  loading ??= new Promise<void>((resolve, reject) => {
    const s = document.createElement('script');
    s.src = SCRIPT;
    s.async = true;
    s.onload = () => resolve();
    s.onerror = () => { loading = null; reject(new Error('Could not load the secure payment window. Check your connection and try again.')); };
    document.head.appendChild(s);
  });
  return loading;
}

export type CheckoutOutcome =
  | { status: 'paid'; result: PaymentResult }
  | { status: 'cancelled' | 'failed'; reason: string; orderId?: string };

/**
 * Buys a plan: creates the order on our server, opens Razorpay Checkout, then verifies the payment server-side
 * (the plan only activates after the server has confirmed the payment with Razorpay).
 */
export async function payForPlan(businessId: string, plan: { planCode: string; billingCycle: string; gstin?: string | null }): Promise<CheckoutOutcome> {
  let order: CheckoutOrder;
  try {
    [order] = await Promise.all([
      api.post<CheckoutOrder>(`/api/owner/businesses/${businessId}/payments/orders`, { ...plan, gstin: plan.gstin || null }).then((r) => r.data),
      loadCheckout(),
    ]);
  } catch (e) {
    return { status: 'failed', reason: errorMessage(e) };
  }

  const base = `/api/owner/businesses/${businessId}/payments/orders/${order.orderId}`;
  const record = (outcome: 'Cancelled' | 'Failed', reason?: string) =>
    api.post(`${base}/outcome`, { outcome, reason: reason ?? null }).catch(() => undefined);

  return new Promise<CheckoutOutcome>((resolve) => {
    let lastError: string | undefined;
    const rzp = new window.Razorpay!({
      key: order.keyId,
      order_id: order.gatewayOrderId,
      amount: order.amountInPaise,
      currency: order.currency,
      name: 'Calling Bell',
      description: `${order.planName} plan · ${order.billingCycle}`,
      prefill: order.prefill,
      notes: { orderNumber: order.orderNumber, business: order.businessName },
      theme: { color: '#F4A62C' },
      modal: {
        confirm_close: true,
        ondismiss: () => {
          void record(lastError ? 'Failed' : 'Cancelled', lastError);
          resolve({ status: lastError ? 'failed' : 'cancelled', reason: lastError ?? 'Payment was not completed.', orderId: order.orderId });
        },
      },
      handler: async (res: RazorpaySuccess) => {
        try {
          const verified = await api.post<PaymentResult>(`${base}/verify`, {
            gatewayOrderId: res.razorpay_order_id, gatewayPaymentId: res.razorpay_payment_id, signature: res.razorpay_signature,
          });
          resolve({ status: 'paid', result: verified.data });
        } catch (e) {
          resolve({ status: 'failed', reason: errorMessage(e), orderId: order.orderId });
        }
      },
    });
    // Razorpay keeps the window open after a failed attempt so the customer can retry another method.
    rzp.on('payment.failed', (r) => { lastError = r.error.description ?? r.error.reason ?? 'The payment failed.'; void record('Failed', lastError); });
    rzp.open();
  });
}
