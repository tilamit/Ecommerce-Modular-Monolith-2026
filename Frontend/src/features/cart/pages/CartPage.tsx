import { Link, useNavigate } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { ShoppingBag, Trash2 } from 'lucide-react';
import { fetchServerCart, priceAnonymousCart, removeServerCartItem, setServerCartQuantity } from '../api';
import { useCartStore } from '../cartStore';
import { useAuthStore } from '../../../shared/api/authStore';
import { Button } from '../../../shared/components/ui/Button';
import { NewTabLink } from '../../../shared/components/ui/NewTabLink';
import { QuantityStepper } from '../../../shared/components/ui/QuantityStepper';
import { EmptyState, ErrorState, Skeleton } from '../../../shared/components/ui/States';
import { formatCurrency } from '../../../shared/lib/format';
import type { Cart, SkippedLine } from '../../../shared/api/types';

const skippedReasonText = (reason: string): string =>
  ({
    not_found: 'is no longer available',
    inactive: 'has been discontinued',
    out_of_stock: 'is out of stock',
  })[reason] ?? 'could not be added';

const SkippedNotice = ({ skipped }: { skipped: SkippedLine[] }) => {
  if (skipped.length === 0) {
    return null;
  }

  // Spec §8.4: skipped lines are returned "so the UI can say why".
  return (
    <div role="status" className="rounded-card border border-warning/40 bg-warning/10 p-4">
      <h2 className="text-sm font-semibold text-content">Some items were removed</h2>
      <ul className="mt-2 list-inside list-disc text-sm text-content-muted">
        {skipped.map((line) => (
          <li key={line.productId}>
            {line.name ?? 'An item'} {skippedReasonText(line.reason)}.
          </li>
        ))}
      </ul>
    </div>
  );
};

export const CartPage = () => {
  const navigate = useNavigate();
  const isSignedIn = useAuthStore((state) => state.accessToken !== null);
  const localCart = useCartStore((state) => state.cart);
  const setQuantity = useCartStore((state) => state.setQuantity);
  const remove = useCartStore((state) => state.remove);

  const localItems = localCart?.items ?? [];

  /**
   * Re-prices on every render of this page (spec §8.4).
   *
   * Signed in, the server cart is authoritative. Anonymous, the localStorage lines are sent
   * for pricing and nothing is persisted. Either way the prices come from the server -
   * the client never computes money.
   */
  const cartQuery = useQuery({
    queryKey: ['cart', isSignedIn ? 'server' : 'local', isSignedIn ? null : localItems],
    queryFn: ({ signal }) =>
      isSignedIn
        ? fetchServerCart(signal)
        : priceAnonymousCart(
            localItems.map((item) => ({ productId: item.productId, quantity: item.quantity })),
            signal,
          ),
    // Spec §11.3: staleTime 0 for the cart. Prices and stock move underneath it.
    staleTime: 0,
  });

  const updateQuantity = async (productId: string, quantity: number) => {
    if (isSignedIn) {
      await setServerCartQuantity(productId, quantity);
      await cartQuery.refetch();
    } else {
      setQuantity(productId, quantity);
    }
  };

  const removeLine = async (productId: string) => {
    if (isSignedIn) {
      await removeServerCartItem(productId);
      await cartQuery.refetch();
    } else {
      remove(productId);
    }
  };

  if (!isSignedIn && localItems.length === 0) {
    return (
      <EmptyState
        title="Your cart is empty"
        icon={<ShoppingBag className="size-10" />}
        description="Browse the catalogue and add something you like."
        action={
          <Button onClick={() => navigate('/')} size="sm">
            Start shopping
          </Button>
        }
      />
    );
  }

  if (cartQuery.isPending) {
    return (
      <div aria-busy="true" className="flex flex-col gap-3">
        <Skeleton className="h-24 w-full" />
        <Skeleton className="h-24 w-full" />
      </div>
    );
  }

  if (cartQuery.isError) {
    return <ErrorState onRetry={() => void cartQuery.refetch()} />;
  }

  const cart: Cart = cartQuery.data;

  if (cart.items.length === 0) {
    return (
      <div className="flex flex-col gap-4">
        <SkippedNotice skipped={cart.skipped} />
        <EmptyState
          title="Your cart is empty"
          icon={<ShoppingBag className="size-10" />}
          action={
            <Button onClick={() => navigate('/')} size="sm">
              Start shopping
            </Button>
          }
        />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-xl font-semibold text-content">Your cart</h1>

      <SkippedNotice skipped={cart.skipped} />

      <div className="grid gap-6 lg:grid-cols-[1fr_20rem]">
        <ul className="flex flex-col gap-3">
          {cart.items.map((line) => (
            <li
              key={line.productId}
              className="flex flex-wrap items-center gap-4 rounded-card border border-border-subtle p-4"
            >
              <div className="min-w-0 flex-1">
                {/* The detail page resolves an id as readily as a slug
                    (`/api/v1/catalog/products/{idOrSlug}`), and the cart line carries the id,
                    so the link needs nothing new from the server. Opening in a new tab keeps
                    the cart, and the quantities the reader has just set, exactly where it is. */}
                <p className="min-w-0 max-w-full font-medium text-content">
                  <NewTabLink
                    to={`/products/${line.productId}`}
                    label={line.name}
                    // No hover styling here: the line already reads as a title and the
                    // external-link icon carries the affordance on its own.
                    className="max-w-full hover:no-underline"
                  />
                </p>
                <p className="text-xs text-content-muted">SKU {line.sku}</p>

                {line.quantityWasClamped && (
                  <p role="status" className="mt-1 text-xs text-warning">
                    Only {line.availableStock} left - quantity reduced.
                  </p>
                )}
              </div>

              <QuantityStepper
                value={line.quantity}
                onChange={(quantity) => void updateQuantity(line.productId, quantity)}
                // A line already in the cart can be taken down to zero, which removes it.
                min={0}
                max={line.availableStock}
                label={`Quantity for ${line.name}`}
                decreaseLabel={`Decrease quantity of ${line.name}`}
                increaseLabel={`Increase quantity of ${line.name}`}
              />

              <p className="w-24 text-right font-semibold text-content">
                {formatCurrency(line.lineTotal, cart.currencyCode)}
              </p>

              <Button
                variant="ghost"
                size="sm"
                aria-label={`Remove ${line.name} from cart`}
                onClick={() => void removeLine(line.productId)}
              >
                <Trash2 className="size-4" aria-hidden="true" />
              </Button>
            </li>
          ))}
        </ul>

        <aside className="h-fit rounded-card border border-border-subtle p-4">
          <h2 className="text-sm font-semibold text-content">Order summary</h2>

          <dl className="mt-3 flex flex-col gap-2 text-sm">
            <div className="flex justify-between">
              <dt className="text-content-muted">Subtotal</dt>
              <dd className="font-medium text-content">
                {formatCurrency(cart.subTotal, cart.currencyCode)}
              </dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-content-muted">Shipping</dt>
              <dd className="text-content-muted">Calculated at checkout</dd>
            </div>
          </dl>

          <Button className="mt-4 w-full" size="lg" onClick={() => navigate('/checkout')}>
            Checkout
          </Button>

          <Link
            to="/"
            className="mt-3 block text-center text-sm text-content-muted hover:text-content hover:underline"
          >
            Continue shopping
          </Link>
        </aside>
      </div>
    </div>
  );
};
