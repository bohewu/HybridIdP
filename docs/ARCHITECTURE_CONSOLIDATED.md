# Architecture — Consolidated

This file consolidates architecture guidance and key design decisions. It is a curated, shorter reference combining the most important content from `ARCHITECTURE.md`, `AUTHENTICATION_INTEGRATION.md`, `PERSON_MULTI_ACCOUNT_ARCHITECTURE.md`, and `SSO_ENTRY_PORTAL_ARCHITECTURE.md`.

## Overview
- HybridIdP uses a hybrid SSR (Razor Pages) + SPA (Vue/Tailwind) architecture for the admin portal.
- Core services and APIs are implemented in ASP.NET Core; identity flows use OpenIddict.

## Design Principles
- Separation of concerns: Layout (Bootstrap/Razor), Content (Vue), Data/API (ASP.NET Core Web API).
- Security-first: server-side authorization with `[Authorize]`, defense-in-depth, CSP and secure cookies.
- Progressive enhancement: pages work without JS; Vue adds interactivity.

## Key Components
- Session & Refresh Token lifecycle: rotation, reuse detection, chain revocation, audit events.
- Authentication: OIDC flows, token issuance, introspection, revocation endpoints.
- Person/Account model: multi-account support and account switching (Phase 10/11 design).

## Operational Concerns
- Database migrations (EF Core) and multi-provider support (MSSQL/Postgres). See `DATABASE_CONFIGURATION.md`.
- Monitoring & audit: background monitoring service, audit events and UI viewer.

## Security Summary
- CSP, secure cookies, cookie flags, token storage best practices.
- Turnstile (Cloudflare) optional protection on login/register flows.

## Where to look for details
- Full architecture content: `docs/ARCHITECTURE.md` (archived longer reference)
- Authentication integration examples: `docs/AUTHENTICATION_INTEGRATION.md`
- Person & identity details: `docs/phase-10-person-identity.md`

---
_Generated as part of docs refactor — see `docs/archive/` for archived full documents._
