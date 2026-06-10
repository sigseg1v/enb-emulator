# Freya Online

New, self-contained code (Freya / MIT -- see `LICENSES/Freya`) for the public
"Freya Online" web presence and the modern login service. No Net-7 / tada-o
compile dependency.

Phase AQ. See `plans/44-phase-aq-freya-online.md` for the full plan and the
#1 architectural risk (the offline-guard decision) before touching any
per-character inventory/credit mutation path.

## Layout

```
freya/online/
  web/        React 18 + TypeScript + Vite SPA (the site: login, mailbox, AH)
  design/     The imported Claude Design source (reference; not built/shipped)
  server/     Go login service + JSON API  (AQ-1/2/3 -- not yet built)
```

## web/ -- the SPA

Pure client-rendered (no SSR). Recreated faithfully from the Claude Design
artifact "Freya Online.html": the design's `tokens.css` / `app.css` /
`screens.css` are copied verbatim; every `.jsx` prototype became a typed `.tsx`
component.

```
cd web
npm install
npm run dev      # Vite dev server on :5173, proxies /api -> :8080
npm run build    # tsc -b && vite build -> web/dist/
npm run typecheck
```

By default the SPA talks to the real Go JSON API (dev: Vite proxies `/api` ->
`:8080`; prod: the Go binary serves the built `dist/`). To render standalone
against `src/mock.ts` with no backend, set `VITE_MOCK=1`.

### Notes

- **Rarity is derived, not stored.** `src/lib/rarity.ts` computes rarity from an
  item's quality, capped by its level (L1-3 -> uncommon max, L4-6 -> rare max,
  L7+ -> epic). The Go service must mirror this exactly; the client copy exists
  for the dev mock and any optimistic render.
- **Time remaining is opaque.** Listings expose only a `low | med | high` band,
  intentionally not precisely ordered within a band (anti-snipe / anti-abuse).
- `npm audit` reports the esbuild dev-server advisory (GHSA-67mh-4wv8-2f99). It
  affects only the Vite **dev server** (cross-origin requests to :5173) and is
  not present in the shipped static build. The only fix is a breaking `vite@8`
  upgrade, deferred deliberately.
