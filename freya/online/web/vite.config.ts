// SPDX-License-Identifier: MIT
// Freya Online -- web SPA build config.
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// No SSR: this is a pure client-rendered SPA. The Go service serves the built
// assets from dist/ and proxies /api/* in production. In dev, Vite proxies
// /api and the legacy auth endpoints to the Go service on :8080.
export default defineConfig({
  plugins: [react()],
  css: {
    // Per-screen styles are authored as *.module.css and scoped by Vite. BEM
    // class names (`mbx__list`, `ahrow--active`, `l-stars`) are exposed to TSX
    // as camelCase locals only (`s.mbxList`, `s.ahrowActive`, `s.lStars`), so a
    // stray global string can never silently match a screen-local rule.
    modules: { localsConvention: 'camelCaseOnly' },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:8080',
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
});
