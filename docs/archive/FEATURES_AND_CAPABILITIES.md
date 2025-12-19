# Features & Capabilities — Consolidated

This document summarizes the project's key features and capabilities. It combines `FEATURES.md`, `MONITORING_BACKGROUND_SERVICE.md`, `SCOPE_AUTHORIZATION.md`, and phase-9 scope notes into a single reference.

## Core Capabilities
- Identity Provider (OIDC) with OpenIddict integration
- Admin UI for Users, Roles, Clients, Scopes, and Consent
- Session management, refresh token rotation, and revocation
- Audit logging and Audit UI viewer
- E2E test harness (Playwright) and CI integration

## Security & Hardening
- MFA (TOTP) planned; Turnstile CAPTCHA optional integration
- CSP, secure cookies, secure headers, and rate limiting guidance

## Authorization & Scopes
- Fine-grained scope and scope-authorization patterns
- Consent UI with multi-language support and resource text management

## Monitoring & Background Services
- Background monitoring service for metrics and anomaly detection
- SignalR-based real-time dashboard (implemented)

## Future Enhancements (short)
- Dynamic Client Registration (RFC 7591)
- Token introspection & revocation endpoints (RFC 7662, RFC 7009)
- Admin API bulk operations and HR sync integrations (Phase 12)

## Reference
- For details and examples, see `docs/FEATURES.md` and phase-specific docs in `docs/phase-*.md`.

---
_Generated as part of docs refactor — keep this concise summary for quick reference._
