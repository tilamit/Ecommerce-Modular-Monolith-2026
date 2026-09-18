import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import path from 'node:path';

export default defineConfig({
  plugins: [react()],
  resolve: { alias: { '@': path.resolve(import.meta.dirname, './src') } },
  test: {
    environment: 'jsdom',
    environmentOptions: {
      // jsdom refuses to expose localStorage on an opaque origin and its default document
      // URL is about:blank. Without an explicit URL, `localStorage` is simply absent -
      // which would make the cart-expiry tests fail for a reason that has nothing to do
      // with the cart. The scheme matches the dev server, which serves over HTTPS.
      jsdom: { url: 'https://localhost:5173/' },
    },
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
