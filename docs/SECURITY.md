# Security Policy

## Overview
HybridAuthIdP is committed to maintaining a high level of security. This document outlines our multi-factor authentication (MFA) implementations, security hardening practices, and how to report vulnerabilities.

## Supported Multi-Factor Authentication (MFA)

We support three primary MFA methods to ensure account security:

### 1. TOTP (App-based)

- **Standard**: RFC 6238 compliant.
- **Compatible Apps**: Google Authenticator, Microsoft Authenticator, Authy, etc.
- **Features**: Recovery codes (10 backup codes), rate-limiting on verification attempts.

### 2. Email OTP

- **Standard**: 6-digit one-time code sent via email.
- **Generation and storage**: Codes use a cryptographically secure random-number generator and are stored only as password hashes.
- **Verification budget**: Each pending code permits at most five verification attempts. The fifth failed attempt invalidates that code; sending a replacement code starts a new budget.
- **Features**: Background queue processing (non-blocking), send rate-limiting, and a 10-minute expiry.

### 3. Passkey (WebAuthn)

- **Standards**: FIDO2 / WebAuthn.
- **Authenticators**: Biometrics (Windows Hello, Touch ID, Face ID) and hardware keys (e.g., YubiKey).
- **Security Policy**: Configurable "Strong MFA Prerequisite" (requires existing TOTP/Email MFA before registering a Passkey).

---

## Security Hardening Implementation

We implement several defense-in-depth measures:

### Security Headers
The system enforces strict security headers via `SecurityHeadersMiddleware`:
- **Content-Security-Policy (CSP)**: Strict policy blocking inline styles/scripts (`unsafe-inline` is prohibited in production).
- **HSTS**: Strict Transport Security enforced for 1 year.
- **X-Frame-Options**: Set to `DENY` to prevent clickjacking.
- **Permissions-Policy**: Disables camera, microphone, and geolocation by default.

### Cookie Security
All authentication and session cookies are configured with:
- `HttpOnly`: Prevents access from JavaScript.
- `Secure`: Transmitted only over HTTPS.
- `SameSite`: Set to `Lax` or `Strict` for CSRF protection.

### Client Administration Ownership

Client-management permissions grant access to the administrative surface but
do not grant cross-owner object access. Person-backed ApplicationManagers can
list their owned clients; object-specific reads, scope validation, and
mutations require that exact ownership. Callers without a Person cannot use
those object-specific routes. The full IdP Admin role can operate across
owners. The fixed administration automation exception is limited to the
explicitly enabled Development/Test fixture and has no effect in Production.

### Scope Administration Ownership

Scope-management permissions grant access to the administrative surface but
do not grant cross-owner mutation rights. ApplicationManagers may view the
scope catalog, where scopes they do not own are marked read-only, but update,
delete, and claim-mapping operations require exact Person ownership. Callers
without a Person cannot mutate scopes. Standard OIDC scopes remain writable
only by the full IdP Admin role. The fixed administration automation exception
applies only to custom scopes when its Development/Test fixture is explicitly
enabled and has no effect in Production.

### Sensitive Administrative Settings

Exact-key settings reads never echo a non-empty value whose key is classified
as a password or secret. They return the existing `(set)` presence marker;
an empty sensitive value remains empty so the UI can distinguish unset state.
Mail prefix responses apply the same rule to both the effective value and the
configuration-backed `defaultValue`; those fields expose only `(set)` or an
empty value for the SMTP password. Non-sensitive Mail defaults remain visible
so administrators can still distinguish configuration from database overrides.
This response masking does not alter internal settings resolution: authorized
server-side consumers can still decrypt protected values. Replacing or
clearing a setting continues to require `settings.update`, and submitting the
mask marker preserves the existing secret rather than storing the marker.

### Custom Claim Source Boundary

Custom and standard scope-mapped claims can read only an explicit set of
profile properties from `ApplicationUser` and its linked `Person`. Credential,
MFA, recovery, lockout, lifecycle, navigation, and audit internals are not
claim sources. Claim issuance uses explicit accessors rather than reflection,
so an unsupported path already stored in the database is skipped and logged
without its value. Claims create and update APIs reject unsupported paths
before persistence; no database migration is required.

The approved set preserves the seeded OIDC mappings and the administration
UI's documented profile paths, including the intentionally hashed
`Person.NationalId`. Adding another source property requires an explicit policy
and test change; adding a property to an entity does not make it token-visible.

### Person Hard-Delete Account Termination

A hard delete physically removes the `Person`, but retains every linked
`ApplicationUser` as an inactive and deleted terminal denial record. The
operation rotates each linked user's `SecurityStamp` and revokes its active
local `UserSession` records atomically with Person removal in a relational
Serializable transaction. External-login and passkey bindings remain attached
to the terminal user as denial bindings; accounts and credentials are not
physically deleted, and this change adds no migration.

Terminal users are rejected before claims or a base principal can be created,
before orphan auto-heal, and before JIT provisioning can create, mutate, or
link a Person. Eligible active orphan auto-heal and legitimate JIT provisioning
remain supported. The existing DELETE `204`/`404` behavior, post-commit audit
placement, unrelated users, and soft-delete/status behavior are unchanged.
There is no Person restore feature.

Security-stamp cookie rejection occurs at the configured validation interval.
Immediate broad lifecycle-cookie invalidation remains pending
`csf_31193ff88cb59c04e6ff7815`. Already-issued self-contained access JWTs may
remain usable until expiry, although terminal user state blocks new code,
refresh, device, and password issuance.

### External Login Email Binding

External authentication proves control of the provider account, but an email
claim is used for local identity binding only after the configured provider has
established email assurance. Google contributes its `verified_email` result.
The built-in Microsoft handler contributes assurance only for the email it maps
from the authenticated Microsoft Graph user when that email equals the
account's verified-domain `userPrincipalName`. A different Graph `mail` alias
does not receive binding assurance. The assurance is carried as an internal
external-cookie claim and is not accepted from unsupported providers.

Without that assurance, JIT provisioning may still create an isolated external
account, but it does not match an existing `Person` or `ApplicationUser` by
email, does not copy the email to `Person`, and does not mark the account email
confirmed. Future provider integrations must explicitly establish equivalent
assurance before enabling email-based binding. Existing provider-key links do
not depend on this matching step.

`ExternalLoginCallback` signs in through an existing durable provider-key link
before considering email-based matching; this established-link path does not
depend on the current email assurance result. When no such link exists,
automatic matching-email account selection or linking checks the applicable
provider-specific assurance policy before any existing-account lookup. Explicit
linking protected by local credentials remains a separate path and is
independent of automatic email matching.

### Production Deployment Inputs and Network Boundary

Production compose requires operator-managed, non-empty database connection strings, database initialization passwords for modes with internal databases, certificate passwords, and a fixed public OIDC origin. The setup scripts generate the required values; production compose has no built-in MSSQL or PostgreSQL password fallback.

The public origin is configured as `OpenIddict__Issuer` plus its matching `PUBLIC_AUTHORITY`. Production derives the ASP.NET Core Host allowlist from the issuer. Repository Nginx gateways overwrite the upstream Host with the configured authority and do not trust a request-supplied Host or `X-Forwarded-Host`. External reverse proxies must provide the same fixed Host and the effective HTTPS scheme.

Validation rejects missing or empty required inputs before image pull, local build, or service startup. Its diagnostics name the missing variable but never print the configured value. Store and provide these values through the operator's approved secret-management process.

MSSQL, PostgreSQL, and Redis have no host-published ports in the default production compose configuration. `deployment/docker-compose.local-ports.yml` is the explicit, loopback-only diagnostic override for the internal data services; it must not be treated as a production default.

### One-Time Operational First Administrator

`OperationalAdminBootstrap` is absent in effect until an operator explicitly enables it; it is disabled by default and is only for a genuinely fresh deployment. It is not a replacement for the fixed Development/Test privileged test fixture or for normal authenticated administrator management after sign-in.

The only accepted capability is a 43-character base64url token supplied in the `X-HybridAuth-Bootstrap-Token` request header over HTTPS. Configuration contains only its hex-encoded SHA-256 digest and an absolute UTC expiry (`OperationalAdminBootstrap__TokenSha256Digest` and `OperationalAdminBootstrap__ExpiresAtUtc`), plus the explicit `OperationalAdminBootstrap__Enabled` switch. The raw token must never appear in configuration, source control, a URL or query string, logs, shell history, or a process list.

When TLS terminates at a proxy, forwarded HTTPS is trusted only through the existing `Proxy__Enabled` and `Proxy__KnownProxies` trust model: specific proxy IPs or CIDRs become the forwarding middleware's known-proxy/known-network set. The caller source IP is only a rate-limiting partition and is never authorization. The completion marker is system-owned, so the ordinary settings API cannot alter it. See [the deployment workflow](DEPLOYMENT_GUIDE.md#one-time-operational-first-administrator) for the required first-use and cleanup procedure.

---

## Reporting a Vulnerability

If you discover a security vulnerability within this project, please report it to us as soon as possible.
- **Email**: [security@hybridauth.local](mailto:security@hybridauth.local) (placeholder)
- **Response Time**: We aim to acknowledge reports within 48 hours and provide a timeline for fixes.

Please do not disclose the vulnerability publicly until we have had a chance to address it.

---

## Security Hardening (詳解)

### Content Security Policy (CSP)
系統實施嚴格的 CSP 策略以防止 XSS 攻擊：
- 禁用 `unsafe-inline` 樣式與腳本。
- 僅允許來自信任 CDN (jsdelivr, Cloudflare Turnstile) 的資源。

### Secure Cookies
所有認證與會話 Cookie 皆具備：
- `HttpOnly`: 防止 JS 存取。
- `Secure`: 僅限 HTTPS 傳輸。
- `SameSite=Lax/Strict`: 防止 CSRF 攻擊。

---

## Scope-Based Authorization (範圍授權服務)

### 概述
HybridIdP 實作 OAuth 2.0 範圍授權，透過 `RequireScope:` 策略模式保護 API 端點。

### 使用方式
在 Controller 或 Action 上方加入屬性：
```csharp
[Authorize(Policy = "RequireScope:api:company:read")]
```

### 強制性範圍 (Required Scopes)
管理者可設定特定 Client 必須具備的 Scope（如 `openid`），使用者在授權頁面無法取消勾選這些範圍。

---
**Last Updated**: 2025-12-19
