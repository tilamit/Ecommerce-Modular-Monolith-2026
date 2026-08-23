/**
 * The anonymous cart (spec §8.4 state 1).
 *
 * Two rules govern this file:
 *
 * 1. **Only `productId` and `quantity` are stored.** Never a price. Anything else is a
 *    price-tampering hole, because localStorage is fully under the user's control. Every
 *    render and the checkout itself re-price on the server.
 * 2. **`expiresUtc` is one hour out and is refreshed on every mutation.** On load, an
 *    expired cart is cleared and the caller shows a dismissible notice.
 */

export const LOCAL_CART_KEY = 'shophub.cart.v1';

export const CART_TTL_MS = 60 * 60 * 1000;

const CART_VERSION = 1;

export interface LocalCartItem {
  productId: string;
  quantity: number;
  addedUtc: string;
}

export interface LocalCart {
  version: number;
  items: LocalCartItem[];
  createdUtc: string;
  expiresUtc: string;
}

export const emptyCart = (now: Date = new Date()): LocalCart => ({
  version: CART_VERSION,
  items: [],
  createdUtc: now.toISOString(),
  expiresUtc: new Date(now.getTime() + CART_TTL_MS).toISOString(),
});

const isLocalCart = (value: unknown): value is LocalCart => {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Partial<LocalCart>;

  return (
    candidate.version === CART_VERSION &&
    Array.isArray(candidate.items) &&
    typeof candidate.expiresUtc === 'string'
  );
};

export const isExpired = (cart: LocalCart, now: Date = new Date()): boolean =>
  Date.parse(cart.expiresUtc) <= now.getTime();

/**
 * Reads the cart, returning null when there is none or it has expired.
 *
 * A malformed or foreign-version payload is treated as absent rather than thrown on: a
 * corrupted localStorage entry must not brick the storefront.
 */
export const readLocalCart = (now: Date = new Date()): { cart: LocalCart | null; expired: boolean } => {
  let raw: string | null;

  try {
    raw = localStorage.getItem(LOCAL_CART_KEY);
  } catch {
    // Private browsing modes can throw on access; behave as if there is no cart.
    return { cart: null, expired: false };
  }

  if (raw === null) {
    return { cart: null, expired: false };
  }

  let parsed: unknown;

  try {
    parsed = JSON.parse(raw);
  } catch {
    clearLocalCart();
    return { cart: null, expired: false };
  }

  if (!isLocalCart(parsed)) {
    clearLocalCart();
    return { cart: null, expired: false };
  }

  if (isExpired(parsed, now)) {
    clearLocalCart();
    return { cart: null, expired: true };
  }

  return { cart: parsed, expired: false };
};

/** Writes the cart, refreshing the expiry window (spec §8.4: "refreshed on every mutation"). */
export const writeLocalCart = (cart: LocalCart, now: Date = new Date()): LocalCart => {
  const refreshed: LocalCart = {
    ...cart,
    version: CART_VERSION,
    expiresUtc: new Date(now.getTime() + CART_TTL_MS).toISOString(),
  };

  try {
    localStorage.setItem(LOCAL_CART_KEY, JSON.stringify(refreshed));
  } catch {
    // Quota or private mode. The in-memory cart still works for this page view.
  }

  return refreshed;
};

export const clearLocalCart = (): void => {
  try {
    localStorage.removeItem(LOCAL_CART_KEY);
  } catch {
    // Nothing useful to do; the caller treats the cart as empty either way.
  }
};

/** Adds or increases a line, summing quantities for a product already present. */
export const addToLocalCart = (
  cart: LocalCart | null,
  productId: string,
  quantity: number,
  now: Date = new Date(),
): LocalCart => {
  const base = cart ?? emptyCart(now);
  const existing = base.items.find((item) => item.productId === productId);

  const items = existing
    ? base.items.map((item) =>
        item.productId === productId ? { ...item, quantity: item.quantity + quantity } : item,
      )
    : [...base.items, { productId, quantity, addedUtc: now.toISOString() }];

  return writeLocalCart({ ...base, items }, now);
};

/** Sets an absolute quantity; zero or less removes the line, matching the UI stepper. */
export const setLocalCartQuantity = (
  cart: LocalCart,
  productId: string,
  quantity: number,
  now: Date = new Date(),
): LocalCart => {
  const items =
    quantity <= 0
      ? cart.items.filter((item) => item.productId !== productId)
      : cart.items.map((item) => (item.productId === productId ? { ...item, quantity } : item));

  return writeLocalCart({ ...cart, items }, now);
};

export const removeFromLocalCart = (cart: LocalCart, productId: string, now: Date = new Date()): LocalCart =>
  writeLocalCart({ ...cart, items: cart.items.filter((item) => item.productId !== productId) }, now);

export const localCartCount = (cart: LocalCart | null): number =>
  cart?.items.reduce((total, item) => total + item.quantity, 0) ?? 0;
