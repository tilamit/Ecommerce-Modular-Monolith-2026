import { Link, useLocation, useParams } from 'react-router';
import { CheckCircle2, Info } from 'lucide-react';
import { formatCurrency, formatDateTime } from '../../../shared/lib/format';
import type { CheckoutResponse } from '../../../shared/api/types';

export const OrderConfirmationPage = () => {
  const { orderNumber = '' } = useParams();
  const location = useLocation();

  // The checkout response is passed through router state so the confirmation needs no
  // second round trip. A direct visit or a refresh loses it, which is why every field
  // below is optional and the order number comes from the URL.
  const order = location.state as CheckoutResponse | null;

  return (
    <div className="mx-auto flex max-w-2xl flex-col items-center gap-6 py-10 text-center">
      <CheckCircle2 className="size-14 text-success" aria-hidden="true" />

      <div>
        <h1 className="text-2xl font-semibold text-content">Thank you - your order is placed</h1>
        <p className="mt-2 text-content-muted">
          Order <span className="font-medium text-content">{orderNumber}</span>
        </p>
      </div>

      {order?.accountExistsPrompt !== null && order?.accountExistsPrompt !== undefined && (
        // Spec §8.4: a soft prompt, never an automatic link. The order stays a guest order.
        <div role="status" className="flex items-start gap-3 rounded-card border border-border-subtle p-4 text-left">
          <Info className="mt-0.5 size-5 shrink-0 text-brand-600" aria-hidden="true" />
          <div>
            <p className="text-sm text-content">{order.accountExistsPrompt}</p>
            <Link to="/login" className="mt-1 inline-block text-sm font-medium text-brand-600 hover:underline">
              Sign in
            </Link>
          </div>
        </div>
      )}

      {order !== null && (
        <dl className="w-full rounded-card border border-border-subtle p-4 text-left text-sm">
          <div className="flex justify-between py-1">
            <dt className="text-content-muted">Placed</dt>
            <dd className="text-content">{formatDateTime(order.placedUtc)}</dd>
          </div>
          <div className="flex justify-between py-1">
            <dt className="text-content-muted">Status</dt>
            <dd className="text-content">{order.status}</dd>
          </div>
          <div className="flex justify-between py-1">
            <dt className="text-content-muted">Payment</dt>
            <dd className="text-content">{order.paymentMethod}</dd>
          </div>
          <div className="flex justify-between border-t border-border-subtle pt-2 text-base font-semibold">
            <dt>Total</dt>
            <dd>{formatCurrency(order.grandTotal, order.currencyCode)}</dd>
          </div>
        </dl>
      )}

      <Link
        to="/"
        className="inline-flex h-10 items-center justify-center rounded-lg bg-brand-600 px-4 text-sm font-medium text-white transition-colors hover:bg-brand-700"
      >
        Continue shopping
      </Link>
    </div>
  );
};
