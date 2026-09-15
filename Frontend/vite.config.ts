import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import path from 'node:path';

// Tailwind v4 is CSS-first: there is no tailwind.config.js and no PostCSS plugin chain.
// The Vite plugin plus `@import "tailwindcss"` in src/styles/index.css is the whole setup
// (spec §16 flagged that v3 and v4 differ materially - this project is on v4).
export default defineConfig({
    plugins: [react(), tailwindcss()],

    resolve: {
        alias: {
            '@': path.resolve(import.meta.dirname, './src'),
        },
    },

    server: {
        port: 5173,
        // The API is same-site in development, so the refresh cookie (SameSite=Lax, scoped to
        // /api/v1/auth) is sent on cross-origin XHR from this origin. CORS on the API allows
        // this exact origin with credentials (spec §11.5).
        proxy: {
            '/api': {
                target: 'https://localhost:7003',
                changeOrigin: false,
                // The API runs on the ASP.NET development certificate, which is self-signed.
                // Node does not read the Windows certificate store, so it would reject the
                // upstream TLS handshake even after `dotnet dev-certs https --trust`.
                secure: false,
            },
        },
        watch: {
            ignored: ['**/.vs/**']
        },
    },

    build: {
        // Surfaces an accidental dependency bloat rather than silently shipping it.
        chunkSizeWarningLimit: 600,
    },
});
