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
- **Sign-in policy**: Passkey sign-in is rejected when the current security policy disables passkeys.
- **Authentication assurance**: Passkey and user-presence AMR values are recorded for a successful passkey assertion; MFA is recorded only when validated authenticator data confirms user verification.

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

### Lifecycle Cookie Validation

Every ASP.NET Core Identity application-cookie validation checks the current
`ApplicationUser` state, independently of the normal security-stamp validation
cadence. A cookie is rejected when its user is inactive, soft-deleted, or
currently locked out. When the user has a linked Person, validation also fails
closed if that Person is missing or cannot authenticate: the Person is
soft-deleted, not `Active`, has a future `StartDate`, or has an expired
`EndDate`.

Eligibility-changing mutations rotate linked user security stamps in the same
EF Core save boundary: `DeactivateUserAsync` and `UpdateUserCoreAsync` when
`IsActive` changes; `UpdatePersonAsync` when Person eligibility changes; and
the Person lifecycle service's terminate, activate, suspend, status-change,
soft-delete, and scheduled-transition paths when eligibility changes.

The current-state check composes with the existing Identity security-stamp
validator, so eligible cookies retain the established security-stamp refresh
and impersonation behavior. It does not change the lifetime of already-issued
self-contained OpenIddict access tokens; they may remain usable until expiry.
This remediation adds no schema migration, UserSession redesign, endpoint, or
production certificate behavior change.

### Upstream Credential and Assertion Boundary

Current password authentication is Local plus the configurable LegacyAuth HTTP
integration; it does not implement AD/LDAP. Direct, deployment-configured
AD/LDAP is the preferred future credential source. A standardized,
provider-neutral authentication/profile API adapter is an optional future
integration only when a required directory capability cannot be supplied
directly.

Selection of an upstream provider must be explicit for every authentication
attempt. A selected provider that is unavailable, rejects the request, returns
malformed or ambiguous data, or times out fails closed. Submitted credentials
must not silently fall through to Local, AD/LDAP, LegacyAuth, or another
credential authority.

For directory credentials, the directory owns validation, enabled/disabled
state, lockout, password expiration and change, and password policy.
HybridAuth IdP keeps authority over its local shadow users, durable account
links and JIT provisioning, Person eligibility overlays, local MFA, cookies,
`UserSession`, token issuance, claims, and consent. Local terminal, deleted,
inactive, locked, or otherwise ineligible `ApplicationUser` or `Person` state
takes precedence over upstream success and denies JIT mutation, principal
generation, session continuation, and new token issuance.

An upstream link requires a namespaced provider identifier plus an immutable,
provider-scoped key; it must not use a mutable login, email, display name, or
directory distinguished name as the durable key. Provider-key matching occurs
before heuristic matching. Email and optional stable-person-key matching need
explicit provider-specific assurance; without it, the result can create an
isolated account but cannot bind an existing Person or `ApplicationUser`.

Upstream authentication and profile assertions are untrusted except for
contract-declared verified fields. A local claim allowlist is the only route
for approved upstream values into local profile state or issued claims.
Arbitrary upstream claims, credential metadata, secrets, raw identifiers, and
internal directory attributes are neither token-visible nor recorded in logs or
audit detail.

Future providers require authenticated TLS with certificate and endpoint
validation, secret-sourced credentials, bounded timeouts, cancellation
propagation, and sanitized security/audit events. Upstream MFA or assurance
does not meet local MFA, AMR, or ACR policy unless an explicit,
provider-specific trust and mapping rule is documented and verified. Cookie
validation and new authorization-code, refresh, device, password, and
equivalent grant issuance continue to independently check current local state.
The future implementation must define a bounded upstream revalidation or
revocation response; self-contained access tokens remain valid only to their
documented expiry unless a separately approved revocation design changes that
policy.

### Future One-Time Credential Migration

The following is a future, unimplemented, opt-in credential-migration
ceremony. It does not add an AD/LDAP provider, migration configuration,
directory operation, or test to the current product. It is a separate,
deployment-controlled migration mode and request policy, not a login-name
heuristic or an ordinary authentication fallback. A durable per-account
one-time eligibility/completion marker or registry authorizes and records the
ceremony; it is distinct from must-change-password, password-expiry, and local
password-policy state. The global migration switch and legacy proof path must
be disabled after the bounded migration window or an explicit operator cutoff.

Before a password is submitted, the future ceremony must resolve exactly one
eligible local account and one managed directory object through an assured
mapping from a stable, namespaced legacy subject to that directory object's
immutable key. Mutable login names, email, display names, directory
distinguished names, unassured fields, and raw national identifiers are not
substitutes. Each attempt explicitly selects exactly one legacy-proof provider
and exactly one directory credential authority. Lookup, proof, reset,
verification, provider, timeout, malformed, ambiguous, or denial failures
must fail closed: they must not fall back to Local, LegacyAuth, AD/LDAP, or any
other credential authority. Pre-proof and failure responses must be uniform
enough to avoid revealing account, eligibility, mapping, marker, directory, or
provider state.

After successful legacy proof, the server must create a short-lived,
server-side migration ticket whose stored representation is protected or
hashed. The ticket must bind the selected provider, stable legacy subject,
immutable directory object, local account, ceremony and browser context,
expiry, and state. It must be atomically single-use and protected against
replay; rate limiting and CSRF/browser binding are mandatory. No application
principal, session, cookie, token, or grant continuation may be created before
migration completion.

The preferred future credential mutation is a least-privilege directory
password-reset capability limited to eligible managed accounts. A separately
configured generic credential-management capability is permitted only when a
documented direct-directory capability gap prevents that operation; it follows
the same explicit-selection and no-fallback rules. The directory remains
authoritative for credential policy, history, expiry, lockout, and state. A
submitted password exists in memory only for the minimum ceremony duration and
must not be stored, derived, logged, audited, claimed, or passed to local
password-policy enforcement.

The completion marker may be recorded only after the reset has an independent
directory bind verification and eligible-status check. The required future
order is verified directory commitment, marker completion, local
shadow-user/Person/JIT finalization subject to local lifecycle eligibility,
local MFA, then session/cookie/token issuance. Interrupted, conflicting, or
uncertain reset or marker states remain denied; they do not reuse a ticket or
guess success and may proceed only through an idempotent, state-aware,
fail-closed recovery/reconciliation path. Once completed, the account's next
login uses only the explicitly selected AD/LDAP path, not legacy proof.

Migration audit and security records must contain only sanitized reason
categories and correlation data. They must exclude passwords, migration
tickets, reset secrets, bind credentials, raw national identifiers,
unnecessary profile values, and other secrets or personally identifiable
information.

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
Configuration-backed SMTP passwords are also routed through the protected
settings writer when first seeded. On startup, a legacy database value is
re-protected only when it still exactly matches the configured SMTP password;
an independent database override is never replaced by configuration seeding.

### Localized Login Notices

Configured login-notice localization values are translated plain text, not
HTML. The shared Razor partial encodes the resolved value at its final render
boundary; localization storage, resolution, and administrative permissions are
unchanged.

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

Application-cookie current-state validation rejects lifecycle-ineligible
sessions on their next validation, independently of the configured
security-stamp interval. Already-issued self-contained access JWTs may remain
usable until expiry, although terminal user state blocks new code, refresh,
device, and password issuance.

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

### Real-Time Monitoring Authorization

The `/monitoringHub` SignalR endpoint requires `monitoring.read` through the
same Identity-cookie or OpenIddict bearer authentication paths supported by
the monitoring HTTP APIs. Anonymous negotiation receives HTTP 401, while an
authenticated principal without that permission receives HTTP 403; hub
requests never redirect to the interactive login page.

Authorized connections join the existing `monitoring` group and retain the
current client event names. Clients cannot submit monitoring DTOs or invoke
broadcast operations on the Hub. Only trusted server-side services publish
updates through `IHubContext<MonitoringHub>`.

### Production Deployment Inputs and Network Boundary

Production compose requires operator-managed, non-empty database connection strings, database initialization passwords for modes with internal databases, certificate passwords, and a fixed public OIDC origin. The setup scripts generate the required values; production compose has no built-in MSSQL or PostgreSQL password fallback.

For newly generated external database settings, the setup scripts require authenticated TLS peer verification: SQL Server uses `Encrypt=True;TrustServerCertificate=False`, and PostgreSQL uses `Ssl Mode=VerifyFull` with an explicit system-trust or `deployment/certs` CA-file choice. A supplied PostgreSQL CA is referenced only as the mounted Linux-container path `/app/certs/<filename>`; unsafe or malformed external TLS input fails before a new configuration is written. This does not alter existing operator-managed connection strings or the internal Docker database trust behavior.

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
