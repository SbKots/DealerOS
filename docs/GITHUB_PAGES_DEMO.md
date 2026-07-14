# DealerOS GitHub Pages visual demo

## Purpose

GitHub Pages hosts a frontend-only visual demonstration of DealerOS. It is not the deployable DealerOS system and it never connects to the .NET API, PostgreSQL, MinIO, or Docker Compose.

## Build contract

- Default `npm run build`: real API mode, base path `/`.
- `npm run build:pages`: loads `apps/web/.env.pages`.
- `VITE_DEMO_MODE=true`: renders `DemoApp` instead of the API-backed `App`.
- `VITE_BASE_PATH=/DealerOS/`: makes scripts, styles, and favicon work at the repository Pages path.

The two modes share visual components and styles, but they have separate data sources. The API-backed application remains the default.

## Data and interaction safety

- All Pages data is synthetic and clearly marked as demonstration data.
- No credentials, access tokens, personal data, or secrets are embedded in the static bundle.
- Demo actions update React state only and reset on reload.
- Vehicle card includes three generated synthetic inventory photos; cover, ordering, deletion and lightbox actions stay in browser memory only.
- The Pages bundle contains no `/api/` request paths.
- Navigation is state-based and does not create nested browser routes, so refresh always returns to `/DealerOS/` safely.
- `public/404.html` redirects accidental nested Pages URLs back to `/DealerOS/`, so an old or copied deep link does not strand the viewer on GitHub's 404 page.

## Deployment

`.github/workflows/pages.yml` uses the official GitHub Pages actions:

- `actions/configure-pages@v5`
- `actions/upload-pages-artifact@v3`
- `actions/deploy-pages@v4`

It runs on a push to `master` or through `workflow_dispatch`. The repository Pages source must be set to **GitHub Actions**. The expected URL is `https://sbkots.github.io/DealerOS/`.

The URL must not be reported as published until the deployment workflow has completed successfully and the page has been opened over HTTPS.
