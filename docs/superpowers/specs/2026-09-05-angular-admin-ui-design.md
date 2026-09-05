# AI Router Angular Admin UI Design

**Date:** 2026-09-05

## Goal

Ship a small Angular admin console inside the existing `AiRouter.Server` container. It should reuse AI Studio's compact provider-management interaction style while remaining an independent application with no runtime dependency on AI Studio.

## User experience

The console lives at `/admin/` and opens on an admin-key unlock screen. The key is held only in browser session storage and attached as a bearer token to management calls.

After unlock, the UI has three areas:

1. **Providers** — CRUD, enable/disable, priority, health, connectivity test, model discovery, common OpenAI-compatible settings, and advanced endpoint/header fields.
2. **Routes** — CRUD for fallback and round-robin routes, including ordered provider/model targets.
3. **Import / Export** — schema-versioned JSON backup and restore. Export redacts API keys by default. Import supports merge and replace.

The layout is responsive and follows system light/dark appearance, with an optional local override.

## Server integration

Angular is built in a Node stage in the existing Dockerfile, then copied to `AiRouter.Server/wwwroot/admin` before `dotnet publish`. ASP.NET Core serves static files and maps an `/admin/{*path:nonfile}` SPA fallback.

The standalone server only maps management APIs when `AIROUTER_ADMIN_KEY` is non-empty. Static admin assets can be loaded without the key, but all useful data/actions remain protected by the existing bearer-key authorization.

## Configuration migration API

`AIRouter.AspNetCore` adds optional endpoints:

- `GET /config/export?includeSecrets=false`
- `POST /config/import?mode=merge`
- `POST /config/import?mode=replace`

Export schema version is `1`. Redaction is the default. `includeSecrets=true` must be explicit.

Merge adds or updates matching provider/route ids and leaves unrelated entries unchanged. Replace performs the same upserts, then deletes existing ids absent from the imported document. Provider updates with `apiKey: null` retain the existing stored API key through `ProviderManager.UpdateAsync`.

## Testing

- Existing .NET CI remains the primary backend gate with its 100% line-coverage requirement.
- New configuration endpoint tests cover authorization, redacted/secret export, merge, replace, invalid input, and secret preservation.
- Angular pure-form utilities are covered with Vitest.
- The Docker build runs Angular tests and a production build, so existing container CI and release builds gate the admin application without a second workflow.
