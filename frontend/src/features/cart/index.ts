/**
 * Cart's public surface (spec §11.1).
 *
 * The frontend mirror of a backend Contracts project: other features import from here and
 * nowhere else, so cart internals stay free to change. `boundaries.test.ts` enforces it.
 */
export { useCartStore, useLocalCartCount } from './cartStore';
export { fetchServerCart, priceAnonymousCart, mergeCart } from './api';
export type { CartLineInput } from './api';
export { CART_TTL_MS } from './localCart';
export type { LocalCart, LocalCartItem } from './localCart';
