# Phase 13: OAuth Flow Enhancement

**Status:** 📋 Planned (sequential: 13.1 → 13.2 → 13.3 → 13.4 → 13.5)
**Priority:** High for 13.2 (M2M); medium for others
**Goal:** Add robust OAuth 2.0 support (refresh token, client credentials with scope visibility controls, device flow), align with OAuth 2.1 guidance, and remove deprecated implicit flow from UI.

## Scope
- In scope: Refresh Token flow, Client Credentials flow, Scope visibility/validation (IsPublic), Device Authorization flow, documentation & rate limiting, UI cleanup (remove implicit).
- Out of scope: Password grant, implicit flow support, federation changes, prod secret rotation policy (tracked separately).

## Architecture & Security Notes
- Token endpoint passthrough is required for custom handlers: enable `.EnableTokenEndpointPassthrough()` when adding client credentials/device handlers.
- Refresh tokens: use rolling tokens; configurable lifetime (default 14 days) can be surfaced later via `ISettingsService`.
- Scope visibility: new `IsPublic` flag on scopes (default `false`). Migration seeds OIDC scopes (`openid`, `profile`, `email`, `roles`) as `true`; API scopes remain `false`.
- M2M (client credentials) must not receive user-centric scopes (`openid`, `profile`, `email`). Enforce at client creation; optionally reinforce at token request.
- Device flow: respect 5s polling interval; rate-limit device polling and token requests.

## Sub-Phases (sequential)

### 13.1 Refresh Token Flow (1-2h)
- Enable `.AllowRefreshTokenFlow()` + `.UseRollingRefreshTokens()` in `Web.IdP/Program.cs`.
- Verify existing `IsRefreshTokenGrantType()` handler in `Web.IdP/Pages/Connect/Token.cshtml.cs`.
- Update test client SQL (`create-testclient*.sql`) to include `gt:refresh_token`.
- Add audit logging of `grant_type` for refresh events.
- E2E: `e2e/tests/refresh-token-flow.spec.ts` (login, capture refresh token, rotate, assert new AT/RT issued).
- Acceptance: tokens rotate; audit logs show `refresh_token`; E2E passes.

### 13.2 Client Credentials + Scope Visibility (priority, 4-6h)
- Enable `.AllowClientCredentialsFlow()`, `.SetIntrospectionEndpointUris("/connect/introspect")`, `.SetRevocationEndpointUris("/connect/revoke")`, `.EnableTokenEndpointPassthrough()` in `Program.cs`.
- Implement `IsClientCredentialsGrantType()` handler in `Token.cshtml.cs` (service account principal, subject=client_id, audiences from scopes via `IApiResourceService`).
- Add `IsPublic` (default `false`) to scopes with migrations (Postgres/SQL Server). Seed OIDC scopes to `true`, API scopes to `false`.
- Enforce in `Infrastructure/Services/ClientService.cs`: clients with `gt:client_credentials` cannot request `IsPublic=true` scopes; validate at creation/update.
- Add confidential test client scripts: `create-testclient-m2m-postgres.sql`, `create-testclient-m2m-mssql.sql` (permissions: `ept:token`, `ept:introspection`, `ept:revocation`, `gt:client_credentials`, API scopes only). Hash secrets with existing `IPasswordHasher`.
- Add Razor Pages: `Connect/Introspect`, `Connect/Revoke` (OpenIddict patterns, passthrough enabled).
- E2E: `e2e/tests/client-credentials-flow.spec.ts` (token, introspect, revoke).
- Acceptance: M2M token works; user scopes blocked; introspect/revoke succeed; migrations applied.

### 13.3 Device Authorization Flow (6-8h)
- Enable `.AllowDeviceAuthorizationFlow()`, `.SetDeviceEndpointUris("/connect/device")`, `.EnableDeviceEndpointPassthrough()` in `Program.cs`.
- Add `Device.cshtml` + `Device.cshtml.cs` (GitHub-style Tailwind UI: 6-8 char code, auto-uppercase, hyphen after 4th char, device info, 10-min countdown, error states).
- Implement `IsDeviceAuthorizationGrantType()` handling in `Token.cshtml.cs`.
- Seed public test client scripts: `create-testclient-device-postgres.sql`, `create-testclient-device-mssql.sql` (`gt:device_code`, `ept:device`, `ept:token`, OIDC scopes as needed).
- Rate-limit polling (token endpoint) ~5 req/min per device_code/IP; align with OpenIddict 5s interval.
- E2E: `e2e/tests/device-flow.spec.ts` (device request → user authorize → polling success).
- Acceptance: end-to-end device flow passes; rate limits respected; UI matches existing auth styling.

### 13.4 Documentation & Cleanup (2-3h)
- Remove implicit flow option from `ClientForm.vue` (no implicit support).
- Create `docs/OAUTH_FLOWS.md` (flow comparison, sequences, examples).
- Update `DEVELOPMENT_GUIDE.md` with testing steps for each flow.
- Add rate limiting middleware for `/connect/token` (10 req/min per client) and `/connect/device` (5 req/min per IP/polling) using `IMemoryCache`.
- Update cleanup scripts to remove `testclient-m2m`, `testclient-device`, and `e2e_*` clients.
- Acceptance: doc complete; implicit removed; rate limits in place; cleanup scripts updated.

### 13.5 Tracking & Closeout
- Update `PROJECT_PROGRESS.md` and `TODOS.md` with Phase 13 status and sub-phases.
- Confirm migrations applied in both providers.
- Ensure E2E suites green for new flows.

## Success Criteria
- Client credentials, refresh token, and device flows all functional and tested.
- M2M clients cannot obtain public/OIDC scopes; scope visibility enforced by `IsPublic` flag.
- Device flow UI usable and consistent; polling rate-limited.
- Documentation and cleanup completed; implicit flow option removed from UI.

## Risks & Mitigations
- Missing token passthrough: explicitly listed in 13.2/13.3 to avoid handler bypass.
- Scope migration edge cases: seed defaults (OIDC=true, API=false); document review step.
- Device polling abuse: rate limiting and 5s interval alignment.
- Secret hashing cost: reuse existing hasher; rotation policy deferred.
