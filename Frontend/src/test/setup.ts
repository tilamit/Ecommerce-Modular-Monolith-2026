import '@testing-library/jest-dom/vitest';
import { afterEach, beforeEach } from 'vitest';
import { cleanup } from '@testing-library/react';

/**
 * In-memory `localStorage` for the test environment.
 *
 * VERIFIED at install time: jsdom 30 does provide `localStorage` when constructed with a
 * non-opaque URL - `new JSDOM(html, { url: 'https://localhost:5173/' }).window.localStorage`
 * is an object. But Vitest 4's jsdom environment does not surface it on the test global,
 * even with `environmentOptions.jsdom.url` set. So it is supplied here rather than assumed.
 *
 * This matters more than a normal shim: the anonymous cart *is* localStorage (spec §8.4),
 * so without it the cart tests would exercise the silent try/catch fallbacks and pass while
 * testing nothing.
 */
class MemoryStorage implements Storage {
  #entries = new Map<string, string>();

  get length(): number {
    return this.#entries.size;
  }

  clear(): void {
    this.#entries.clear();
  }

  getItem(key: string): string | null {
    return this.#entries.get(key) ?? null;
  }

  key(index: number): string | null {
    return [...this.#entries.keys()][index] ?? null;
  }

  removeItem(key: string): void {
    this.#entries.delete(key);
  }

  setItem(key: string, value: string): void {
    this.#entries.set(key, String(value));
  }
}

if (typeof globalThis.localStorage === 'undefined') {
  const storage = new MemoryStorage();

  Object.defineProperty(globalThis, 'localStorage', { value: storage, configurable: true });

  if (typeof window !== 'undefined') {
    Object.defineProperty(window, 'localStorage', { value: storage, configurable: true });
  }
}

beforeEach(() => {
  // Each test starts from an empty store. A leaked entry would make tests order-dependent,
  // which is how a suite starts lying about what it covers.
  localStorage.clear();
});

afterEach(() => {
  cleanup();
});
