import { create } from 'zustand';
import {
  addToLocalCart,
  clearLocalCart,
  emptyCart,
  localCartCount,
  readLocalCart,
  removeFromLocalCart,
  setLocalCartQuantity,
  type LocalCart,
} from './localCart';

/**
 * Client-side cart state.
 *
 * Holds the anonymous cart only. Once signed in the server cart is authoritative and this
 * is merged into it and cleared (spec §8.4 state 3).
 */
interface CartState {
  cart: LocalCart | null;
  /** True when a cart was found expired on load, so the UI can show a dismissible notice. */
  showExpiredNotice: boolean;

  hydrate: () => void;
  add: (productId: string, quantity: number) => void;
  setQuantity: (productId: string, quantity: number) => void;
  remove: (productId: string) => void;
  clear: () => void;
  dismissExpiredNotice: () => void;
  /** Re-checks expiry for the tab-left-open case; returns true if the cart was cleared. */
  sweep: () => boolean;
}

export const useCartStore = create<CartState>((set, get) => ({
  cart: null,
  showExpiredNotice: false,

  hydrate: () => {
    const { cart, expired } = readLocalCart();

    set({ cart, showExpiredNotice: expired });
  },

  add: (productId, quantity) => {
    set({ cart: addToLocalCart(get().cart, productId, quantity) });
  },

  setQuantity: (productId, quantity) => {
    const current = get().cart;

    if (current !== null) {
      set({ cart: setLocalCartQuantity(current, productId, quantity) });
    }
  },

  remove: (productId) => {
    const current = get().cart;

    if (current !== null) {
      set({ cart: removeFromLocalCart(current, productId) });
    }
  },

  clear: () => {
    clearLocalCart();
    set({ cart: emptyCart(), showExpiredNotice: false });
  },

  dismissExpiredNotice: () => set({ showExpiredNotice: false }),

  sweep: () => {
    const { cart, expired } = readLocalCart();

    if (expired) {
      set({ cart: null, showExpiredNotice: true });
      return true;
    }

    set({ cart });
    return false;
  },
}));

export const useLocalCartCount = (): number => localCartCount(useCartStore((state) => state.cart));
