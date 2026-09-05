# AI Router Angular Admin UI Implementation Plan

> Execute both phases on one feature branch and present one final PR for review.

## Phase 1 — Providers and admin unlock

1. Add an Angular 22 standalone application under `src/AiRouter.Admin`.
2. Add session-scoped admin-key storage and a functional HTTP interceptor.
3. Add unlock flow that validates the key against `/providers`.
4. Build provider cards and add/edit/delete/enable/test/model-discovery interactions.
5. Keep edit API-key input blank by default so the request sends `apiKey: null` and preserves the stored secret.
6. Add responsive light/dark styling.
7. Build Angular in the Dockerfile and serve its output from `/admin/`.

## Phase 2 — Routes and configuration migration

1. Add fallback/round-robin route list and editor.
2. Add schema-versioned configuration export endpoint, redacting API keys by default.
3. Add merge/replace configuration import endpoint.
4. Add Import / Export UI with a secrets warning, file preview, and destructive replace confirmation.
5. Add backend tests for every new configuration endpoint path required by the existing 100% line-coverage gate.
6. Add Vitest coverage for provider form serialization, especially null-secret preservation and TimeSpan conversion.
7. Update README/container usage.

## Verification

Run through GitHub CI on the completed feature branch/PR:

- `dotnet restore`
- release build
- all .NET tests
- 100% line-coverage gate
- package smoke tests
- Docker build, which runs `npm test` and Angular production build
- container health/live smoke tests

Fix failures on the feature branch until all required checks are green, then leave the PR ready for the user's single review.
