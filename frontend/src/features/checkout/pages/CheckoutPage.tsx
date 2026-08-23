import { useState } from 'react';
import { useNavigate } from 'react-router';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useQuery } from '@tanstack/react-query';
import { api, ApiError } from '../../../shared/api/httpClient';
import { useAuthStore } from '../../../shared/api/authStore';
import { useCartStore } from '../../cart';
import { fetchServerCart, priceAnonymousCart } from '../../cart';
import { Button } from '../../../shared/components/ui/Button';
import { Input, Select } from '../../../shared/components/ui/Field';
import { ErrorState, Skeleton } from '../../../shared/components/ui/States';
import { formatCurrency } from '../../../shared/lib/format';
import type { CheckoutResponse, ReservationFailure } from '../../../shared/api/types';

const GUEST_PROFILE_KEY = 'shophub.guestProfile.v1';

const addressSchema = z.object({
  fullName: z.string().min(1, 'Required').max(256),
  line1: z.string().min(1, 'Required').max(256),
  line2: z.string().max(256).optional(),
  city: z.string().min(1, 'Required').max(128),
  state: z.string().max(128).optional(),
  postalCode: z.string().min(1, 'Required').max(32),
  country: z.string().min(1, 'Required').max(128),
  phoneNumber: z.string().max(32).optional(),
});

const guestSchema = addressSchema.extend({
  email: z.email('Enter a valid email address').max(256),
  paymentMethod: z.enum(['CashOnDelivery', 'Card', 'BankTransfer']),
});

type GuestForm = z.infer<typeof guestSchema>;

/**
 * Prefills the guest form from a previous checkout (spec §8.4).
 *
 * Convenience only, and deliberately never payment credentials - the payment method is a
 * label, nothing sensitive. This entry has no expiry, unlike the cart.
 */
const readSavedGuestProfile = (): Partial<GuestForm> => {
  try {
    const raw = localStorage.getItem(GUEST_PROFILE_KEY);

    return raw === null ? {} : (JSON.parse(raw) as Partial<GuestForm>);
  } catch {
    return {};
  }
};

const saveGuestProfile = (form: GuestForm): void => {
  try {
    localStorage.setItem(GUEST_PROFILE_KEY, JSON.stringify(form));
  } catch {
    // Non-essential; a failure here must not block the order.
  }
};

const StockFailureNotice = ({ failures }: { failures: ReservationFailure[] }) => (
  // ADR-002: the order was refused as a whole, with a reason per line. Rendering them next
  // to the cart is what lets the customer decide, rather than discovering a smaller order.
  <div role="alert" className="rounded-card border border-danger/40 bg-danger/10 p-4">
    <h2 className="text-sm font-semibold text-content">We could not complete your order</h2>
    <ul className="mt-2 flex flex-col gap-1 text-sm text-content-muted">
      {failures.map((failure) => (
        <li key={failure.productId}>
          <span className="font-medium text-content">{failure.productName}</span>{' '}
          {failure.reason === 'insufficient_stock'
            ? `- only ${failure.available} left, you asked for ${failure.requested}.`
            : '- no longer available.'}
        </li>
      ))}
    </ul>
    <p className="mt-2 text-sm text-content-muted">
      Adjust the quantities in your cart and try again. Nothing has been charged.
    </p>
  </div>
);

export const CheckoutPage = () => {
  const navigate = useNavigate();
  const isSignedIn = useAuthStore((state) => state.accessToken !== null);
  const localCart = useCartStore((state) => state.cart);
  const clearLocal = useCartStore((state) => state.clear);

  const [failures, setFailures] = useState<ReservationFailure[]>([]);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const localItems = localCart?.items ?? [];

  const cartQuery = useQuery({
    queryKey: ['cart', isSignedIn ? 'server' : 'local', isSignedIn ? null : localItems],
    queryFn: ({ signal }) =>
      isSignedIn
        ? fetchServerCart(signal)
        : priceAnonymousCart(
            localItems.map((item) => ({ productId: item.productId, quantity: item.quantity })),
            signal,
          ),
    staleTime: 0,
  });

  const form = useForm<GuestForm>({
    resolver: zodResolver(guestSchema),
    defaultValues: { paymentMethod: 'CashOnDelivery', country: 'USA', ...readSavedGuestProfile() },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    setFailures([]);
    setSubmitError(null);

    const address = {
      fullName: values.fullName,
      line1: values.line1,
      line2: values.line2,
      city: values.city,
      state: values.state,
      postalCode: values.postalCode,
      country: values.country,
      phoneNumber: values.phoneNumber,
    };

    try {
      const response = isSignedIn
        ? await api.post<CheckoutResponse>('/api/v1/checkout', {
            shippingAddress: address,
            paymentMethod: values.paymentMethod,
          })
        : await api.post<CheckoutResponse>('/api/v1/checkout/guest', {
            email: values.email,
            fullName: values.fullName,
            phoneNumber: values.phoneNumber,
            shippingAddress: address,
            paymentMethod: values.paymentMethod,
            items: localItems.map((item) => ({ productId: item.productId, quantity: item.quantity })),
          });

      if (!isSignedIn) {
        saveGuestProfile(values);
        clearLocal();
      }

      navigate(`/order-confirmation/${response.orderNumber}`, { state: response });
    } catch (error) {
      if (error instanceof ApiError && error.problem.failures !== undefined) {
        setFailures(error.problem.failures);
        return;
      }

      setSubmitError(error instanceof ApiError ? error.message : 'Something went wrong. Please try again.');
    }
  });

  if (cartQuery.isPending) {
    return <Skeleton className="h-64 w-full" />;
  }

  if (cartQuery.isError) {
    return <ErrorState onRetry={() => void cartQuery.refetch()} />;
  }

  const cart = cartQuery.data;

  if (cart.items.length === 0) {
    return (
      <ErrorState
        title="Your cart is empty"
        description="Add something before checking out."
        onRetry={() => navigate('/')}
      />
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-xl font-semibold text-content">Checkout</h1>

      {!isSignedIn && (
        <div className="rounded-card border border-border-subtle bg-surface-raised p-4">
          <p className="text-sm text-content">
            Checking out as a guest.{' '}
            <button
              type="button"
              onClick={() => navigate('/login?returnUrl=%2Fcheckout')}
              className="font-medium text-brand-600 hover:underline"
            >
              Sign in instead
            </button>{' '}
            to keep this order in your history.
          </p>
        </div>
      )}

      {failures.length > 0 && <StockFailureNotice failures={failures} />}

      {submitError !== null && (
        <div role="alert" className="rounded-card border border-danger/40 bg-danger/10 p-4 text-sm text-content">
          {submitError}
        </div>
      )}

      <form onSubmit={(event) => void onSubmit(event)} className="grid gap-6 lg:grid-cols-[1fr_20rem]">
        <div className="flex flex-col gap-4">
          {!isSignedIn && (
            <Input
              label="Email"
              type="email"
              autoComplete="email"
              required
              error={form.formState.errors.email?.message}
              hint="Your order confirmation is shown on the next screen."
              {...form.register('email')}
            />
          )}

          <Input
            label="Full name"
            autoComplete="name"
            required
            error={form.formState.errors.fullName?.message}
            {...form.register('fullName')}
          />

          <Input
            label="Address line 1"
            autoComplete="address-line1"
            required
            error={form.formState.errors.line1?.message}
            {...form.register('line1')}
          />

          <Input label="Address line 2" autoComplete="address-line2" {...form.register('line2')} />

          <div className="grid gap-4 sm:grid-cols-2">
            <Input
              label="City"
              autoComplete="address-level2"
              required
              error={form.formState.errors.city?.message}
              {...form.register('city')}
            />
            <Input label="State / region" autoComplete="address-level1" {...form.register('state')} />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <Input
              label="Postal code"
              autoComplete="postal-code"
              required
              error={form.formState.errors.postalCode?.message}
              {...form.register('postalCode')}
            />
            <Input
              label="Country"
              autoComplete="country-name"
              required
              error={form.formState.errors.country?.message}
              {...form.register('country')}
            />
          </div>

          <Input label="Phone number" type="tel" autoComplete="tel" {...form.register('phoneNumber')} />

          <Select label="Payment method" required {...form.register('paymentMethod')}>
            <option value="CashOnDelivery">Cash on delivery</option>
            <option value="Card">Card</option>
            <option value="BankTransfer">Bank transfer</option>
          </Select>

          <p className="text-xs text-content-muted">
            No payment is taken. The method is recorded with your order.
          </p>
        </div>

        <aside className="h-fit rounded-card border border-border-subtle p-4">
          <h2 className="text-sm font-semibold text-content">Order summary</h2>

          <ul className="mt-3 flex flex-col gap-2 text-sm">
            {cart.items.map((line) => (
              <li key={line.productId} className="flex justify-between gap-2">
                <span className="min-w-0 flex-1 truncate text-content-muted">
                  {line.quantity} × {line.name}
                </span>
                <span className="text-content">{formatCurrency(line.lineTotal, cart.currencyCode)}</span>
              </li>
            ))}
          </ul>

          <div className="mt-3 flex justify-between border-t border-border-subtle pt-3 text-base font-semibold text-content">
            <span>Total</span>
            <span>{formatCurrency(cart.subTotal, cart.currencyCode)}</span>
          </div>

          <Button type="submit" className="mt-4 w-full" size="lg" isLoading={form.formState.isSubmitting}>
            Place order
          </Button>
        </aside>
      </form>
    </div>
  );
};
