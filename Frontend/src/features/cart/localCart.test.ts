import { describe, expect, it } from 'vitest';
import {
  CART_TTL_MS,
  LOCAL_CART_KEY,
  addToLocalCart,
  clearLocalCart,
  emptyCart,
  isExpired,
  localCartCount,
  readLocalCart,
  removeFromLocalCart,
  setLocalCartQuantity,
  writeLocalCart,
} from './localCart';

/** Spec §12 names "cart reducer + expiry" as required frontend coverage. */

const NOW = new Date('2026-08-23T12:00:00.000Z');
const later = (ms: number) => new Date(NOW.getTime() + ms);

describe('localCart', () => {
  it('starts empty with a one-hour expiry', () => {
    const cart = emptyCart(NOW);

    expect(cart.items).toEqual([]);
    expect(Date.parse(cart.expiresUtc) - NOW.getTime()).toBe(CART_TTL_MS);
  });

  /**
   * The price-tampering guard (spec §8.4): localStorage is fully under the user's control,
   * so it must hold nothing the server would trust. Only productId and quantity.
   */
  it('persists no price information whatsoever', () => {
    addToLocalCart(null, 'p1', 2, NOW);

    const raw = localStorage.getItem(LOCAL_CART_KEY) ?? '';

    expect(raw).toContain('p1');
    expect(raw).not.toContain('price');
    expect(raw).not.toContain('unitPrice');
    expect(raw).not.toContain('total');
  });

  it('sums quantities when the same product is added twice', () => {
    const first = addToLocalCart(null, 'p1', 2, NOW);
    const second = addToLocalCart(first, 'p1', 3, NOW);

    expect(second.items).toHaveLength(1);
    expect(second.items[0].quantity).toBe(5);
  });

  it('keeps separate lines for different products', () => {
    const cart = addToLocalCart(addToLocalCart(null, 'p1', 1, NOW), 'p2', 1, NOW);

    expect(cart.items).toHaveLength(2);
    expect(localCartCount(cart)).toBe(2);
  });

  it('removes the line when quantity is set to zero', () => {
    const cart = addToLocalCart(null, 'p1', 3, NOW);

    expect(setLocalCartQuantity(cart, 'p1', 0, NOW).items).toHaveLength(0);
  });

  it('removes a line explicitly', () => {
    const cart = addToLocalCart(addToLocalCart(null, 'p1', 1, NOW), 'p2', 1, NOW);

    expect(removeFromLocalCart(cart, 'p1', NOW).items.map((i) => i.productId)).toEqual(['p2']);
  });

  // --- expiry (spec §8.4 state 1) -----------------------------------------

  it('is not expired inside the window', () => {
    expect(isExpired(emptyCart(NOW), later(CART_TTL_MS - 1_000))).toBe(false);
  });

  it('is expired once the window passes', () => {
    expect(isExpired(emptyCart(NOW), later(CART_TTL_MS + 1_000))).toBe(true);
  });

  /**
   * The behaviour that makes the hour mean something: every mutation pushes the deadline
   * out, so an actively-used cart never expires under the user.
   */
  it('refreshes the expiry on every mutation', () => {
    const cart = addToLocalCart(null, 'p1', 1, NOW);
    const thirtyMinutesLater = later(30 * 60_000);

    const touched = addToLocalCart(cart, 'p2', 1, thirtyMinutesLater);

    expect(Date.parse(touched.expiresUtc)).toBe(thirtyMinutesLater.getTime() + CART_TTL_MS);
  });

  it('clears an expired cart on read and reports it', () => {
    writeLocalCart(addToLocalCart(null, 'p1', 1, NOW), NOW);

    const result = readLocalCart(later(CART_TTL_MS + 1_000));

    expect(result.cart).toBeNull();
    expect(result.expired).toBe(true);
    expect(localStorage.getItem(LOCAL_CART_KEY)).toBeNull();
  });

  it('returns an unexpired cart unchanged', () => {
    writeLocalCart(addToLocalCart(null, 'p1', 2, NOW), NOW);

    const result = readLocalCart(later(10 * 60_000));

    expect(result.expired).toBe(false);
    expect(result.cart?.items[0]).toMatchObject({ productId: 'p1', quantity: 2 });
  });

  it('reports no cart, rather than an expired one, when there is nothing stored', () => {
    expect(readLocalCart(NOW)).toEqual({ cart: null, expired: false });
  });

  // --- resilience ---------------------------------------------------------

  /** A corrupted entry must not brick the storefront. */
  it('discards malformed JSON instead of throwing', () => {
    localStorage.setItem(LOCAL_CART_KEY, '{not valid json');

    expect(() => readLocalCart(NOW)).not.toThrow();
    expect(readLocalCart(NOW).cart).toBeNull();
  });

  it('discards a payload from an older schema version', () => {
    localStorage.setItem(LOCAL_CART_KEY, JSON.stringify({ version: 0, items: [{ productId: 'p1' }] }));

    expect(readLocalCart(NOW).cart).toBeNull();
  });

  it('clears everything on demand', () => {
    addToLocalCart(null, 'p1', 1, NOW);
    clearLocalCart();

    expect(localStorage.getItem(LOCAL_CART_KEY)).toBeNull();
  });

  it('counts zero for an absent cart', () => {
    expect(localCartCount(null)).toBe(0);
  });
});
