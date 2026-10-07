# Security Policy

## Overview
HybridAuthIdP is committed to maintaining a high level of security. This document outlines our multi-factor authentication (MFA) implementations, security hardening practices, and how to report vulnerabilities.

### Administrative API authority

Interactive administration uses the authenticated Identity application cookie.
User OAuth tokens carry application roles and do not inherit global IdP roles or
administrative permissions. Outstanding authorization codes are sanitized at
redemption. Ordinary and previously issued bearer tokens cannot authorize IdP
administration merely by containing a permission scope, role, or client name.

Administrative client-credentials access requires server-controlled provisioning
of `IdpAdministrationPermissions` on the application record. Client and scope
management APIs cannot create this approval. The Development/Test admin client
seeder provisions it only when privileged test-client seeding is enabled. This
is not a production bootstrap mechanism; production approvals require trusted
operator provisioning outside delegated client management.

Tokens bind that approval to the immutable application record. Authorization
checks the current approval, validated presenter, client subject, and requested
scope against the approved permission ceiling. Deleting and recreating a client
with the same name does not inherit approval. Delegated ownership does not grant
management of approved administrative clients. Reissue existing administrative
M2M tokens after upgrading; older tokens lack the record-bound approval claim.

### API scope usage approval

API resources default to restricted usage and a private catalog. Only the
current resource-owning Person or a full interactive IdP Admin can explicitly
enable `IsUsageOpen`, change `IsCatalogVisible`, or approve a client's scope for
that resource. Catalog visibility grants discovery only. `ScopeExtension.IsPublic`
retains its OIDC/user versus M2M classification; it is never usage approval.

Client creation, full permission updates, scope replacement and token issuance
evaluate every resource associated with a scope, combining `ApiResourceScope`
rows and OpenIddict scope resource names. Each restricted resource needs its own
approval. Owners can approve their own part independently in the existing API
Resource editor; that action does not add client permissions. A client write
preflights the complete requested set before saving any new approvals or
permissions. Standard identity scopes bypass approval only when no specific
API mapping exists. M2M clients still cannot use user-centric scopes.

Approval receipts live in the immutable OpenIddict application's server-owned
`IdpApiScopeUsageApprovals` property. Client DTOs cannot set or replace this
property. Receipts bind the scope ID/name, individual resource ID, current
owner, approving user and approval time. Ownership changes or new resource
mappings require applicable new approval. Unmapped custom scopes require their
scope owner or Admin; ownerless scopes and unregistered audience names require
explicit Admin approval, with unknown audience names bound as an exact set.
These approvals cannot widen `IdpAdministrationPermissions`. Administrative
bearers retain their current server-provisioned permission ceiling and never
become resource owners or full interactive administrators.

There is no legacy approval backfill. Existing scope permissions, client secrets
and other credentials remain stored, including seeded sample API clients. After
upgrading, unproven non-identity scope usage fails closed at new authorization
code, refresh, device and client-credentials token issuance (also password
issuance). An owner or Admin must explicitly approve those clients, or explicitly
open each resource. Already-issued self-contained tokens retain their ordinary
lifetime. Issuance rebuilds audiences from current mappings; cookie, consent,
grant and client permission checks still apply.

## Impersonation audit attribution

External-login removal is denied before any account mutation when the current
principal carries an impersonator marker or Actor. Ordinary self-service removal
retains its lifecycle checks, cookie refresh and audit behavior.

Impersonation retains the `Users.Impersonate` permission check, administrator
target denial and current account eligibility checks. Successful transitions
persist `ImpersonationStarted` and `ImpersonationStopped` through the existing
audit service before replacing the application cookie. Their `UserId` is the
original actor. Audit persistence failure prevents cookie replacement.

Direct authentication and session refresh/revocation records
retain their existing `UserId` and detail fields. During impersonation, `Details`
also contains `impersonation.actorUserId` and `impersonation.subjectUserId`,
resolved from the authenticated application identity and its preserved Actor or
`impersonator_id` claim. These are opaque account IDs; attribution does not add
usernames, email addresses or tokens. Existing name/email masking still applies.
Text details are retained under `details` when this JSON attribution is added.
Ordinary and system records keep their existing representation. Audit listing
and export retain these details, including after cookie restoration.

Administrative user, client, role and scope mutation records separate the
performer from the affected resource. `UserId` identifies the validated local
actor (the original actor during impersonation); `Details.actor` records its
type and stable ID, and `Details.target` records the resource type and ID.
Administrative M2M records have a null `UserId` and a typed client actor bound
to the validated client subject and immutable application ID. Background
operations record a system actor. The existing masked change description is
retained in `Details.message`. Allowed/required client scope replacements and
setting and security-policy updates also create durable records; setting and credential values
are omitted. Existing historical records are not rewritten.

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
- **Anonymous options**: Submitted usernames are ignored; public options have no account-specific credential descriptors. Anonymous login uses discoverable credentials.
- **Historical keys**: Account-specific descriptors are available only for the server-resolved application cookie or temporary two-factor cookie after password/external proof. TOTP and email challenges link to the existing MFA method selector. Assertions are bound to that protected subject and options are consumed on each attempt.
- **Inventory limits**: Stored credentials do not record discoverability. Registration date, credential type and authenticator model cannot identify non-discoverable keys reliably. Existing credentials are preserved; do not bulk-delete them based on those fields.
- **Credential retirement**: `UserCredentials.DisabledAtUtc` disables an individual key while retaining its record. Disabled keys cannot authenticate, satisfy enrollment or privileged-role MFA checks, consume the active-key limit, or appear in active-key lists and WebAuthn descriptors. Verification also fails if the credential is disabled before its usage update commits.
- **Rebinding**: Retirement does not disable the account or external login. Users authenticate through another working method, complete any existing TOTP/email MFA, and register a new discoverable credential under current policy. The old credential never becomes active through sign-in or registration; new registration uses a new credential ID. If `RequireMfaForPasskey` is enabled, TOTP/email enrollment is required first. Operators must confirm another usable sign-in method before retiring a user's last key. Existing sessions and issued tokens follow their existing lifetime policy.
- **Authentication assurance**: Passkey and user-presence AMR values are recorded for a successful passkey assertion; MFA is recorded only when validated authenticator data confirms user verification.
- **MFA requirements**: Authorization, device approval, token redemption, refresh, and factor management require the performed `mfa` evidence. A hardware-key (`hwk`) claim alone does not satisfy MFA.

Device approval and device-code redemption independently recheck current global
mandatory MFA, even when the client does not require it. The browser cookie and
persisted device principal must carry performed `mfa` evidence respectively.
An existing password-only cookie or approval is not exempt after activation,
including during enrollment grace. The existing per-client MFA, one-time
approval intent, current lifecycle/migration and scope checks still apply;
redemption failures use the normal machine-readable OAuth `invalid_grant` JSON.

Authorization-code redemption also checks the current global mandatory MFA
policy and the client's MFA requirement. A password-only code issued before
activation cannot redeem afterward, even during enrollment grace. Password
grants cannot perform a passkey assertion: an active passkey enrollment does
not satisfy mandatory MFA, so enrolled accounts must use an interactive flow.
The existing enrollment grace for accounts without active factors is retained.

### Factor removal and one-time proof consumption

Recovery-code regeneration also requires fresh performed-MFA management proof,
followed by the existing password or TOTP confirmation. Email enrollment through
either API requires the same enrollment authority. Initial enrollment stores the
current security stamp in both its server session and temporary cookie; every
pending two-factor sign-in rejects missing or retired stamps. Only authorized
TOTP seed creation can carry that exact stamp transition into enrollment proof.
Resetting or retiring TOTP removes custom and Identity recovery codes. Password
confirmations share the configured account lockout budget and login rate limit.
Atomic email-code attempt writes advance the Identity concurrency stamp and
reload the tracked user so later Identity writes cannot restore an old budget.

TOTP, email MFA and passkey removal require a completed MFA reauthentication
within five minutes, bound to the same account and its current security stamp.
The existing `/api/account/mfa/reauthenticate` interactive flow produces this
server-session proof only after the intended account completes sign-in with
MFA. Cookie and validated bearer callers must also carry performed `mfa`
evidence; mixed identities must agree on the subject and assurance. A bearer
may omit `auth_time` and present the fresh server-session proof. The browser
session and antiforgery requirements of the reauthentication and passkey APIs
still apply. This path does not depend on recovery-email rollout switches.
Successful removal consumes the management proof. Expired, password-only,
initial-enrollment or security-stamp-mismatched proofs cannot authorize removal.
The optional `forRemoval=true` reauthentication intent selects a fixed Profile
return after sign-in; the default enrollment return remains MFA Setup. Neither
intent changes the sign-in requirements or automatically repeats a removal.

Removal checks current account/Person eligibility and computes the qualifying
methods remaining after any required passkey cascade, including policy-disabled
methods. Mandatory MFA cannot be satisfied by a key that the same operation
will retire. Only active affected keys receive `DisabledAtUtc`; previously
retired records retain their original timestamps. Factor state and dependent
retirements persist together through the user's Identity concurrency update.

TOTP verification records the actual accepted 30-second step within Identity's
current-2 through current+2 window. Enrollment consumes that proof as well.
TOTP and custom/Identity recovery codes succeed only after their consumption
commits. Conflicts fail authentication, and failed user/token changes are
discarded before the login failure branch can perform another save.

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

### Antiforgery for account and administrative APIs

Cookie-authenticated mutations require a valid antiforgery cookie and request
token. The shared API filter exempts only successful OpenIddict bearer
authentication without a participating application or temporary two-factor
cookie identity. An Authorization header or authentication-type label alone
does not grant an exemption; mixed bearer/cookie principals still require CSRF
validation.

Passkey registration, assertion, options and deletion are browser-session
operations and always require antiforgery validation, including anonymous
passkey login. The login, MFA selector and enrollment pages publish an encoded
request token for the existing fetch interceptor to send as `X-XSRF-TOKEN`.
Missing or invalid tokens are rejected before ceremony processing. Interactive
MFA reauthentication and recovery-code regeneration also require antiforgery,
because their authority is the application cookie.

The recovery-email selection flow retains its additional cookie/session-bound
validation. When that feature is disabled, existing high-assurance bearer-only
recovery clients remain supported; cookie requests use the shared CSRF filter.

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

The current LegacyAuth, Provider Proof and Provider Metadata clients deny
automatic redirects and bound response consumption before JSON parsing at
64 KiB, 64 KiB and 16 KiB respectively. Oversize cannot authenticate or refresh
usable metadata; metadata body-read failures invalidate prior evidence. These
controls preserve in-limit `2xx` JSON, cancellation/deadline and authority rules,
and add no retry or alternate provider. `LegacyAuth__RequireHttps=false` retains
HTTP compatibility; `true` rejects HTTP before credential/shared-secret dispatch.

SMTP ordinary and test-send share `EmailSettings__SmtpRequireTls=false` and
`EmailSettings__SmtpValidateServerCertificate=true` defaults. SSL-on requires
implicit TLS. SSL-off uses mandatory STARTTLS when RequireTls is true and
opportunistic STARTTLS otherwise. Localhost/SSL-off does not disable certificate
validation; false explicitly accepts any certificate if TLS is used. Stored
mail/DTO transport settings cannot override these global policy flags. See the
[deployment policy combinations](DEPLOYMENT_GUIDE.md#optional-legacyauth-smtp-and-recovery-hint-policies)
for the supported operator choices; no internal CA is mandatory.

Recovery precheck hints default ON and intentionally retain eligible guidance
and a masked destination. `ForgotPasswordRecovery__PrecheckHintsEnabled=false`
uses uniform public precheck/restoration/Send guidance without eligibility or
mask disclosure. Precheck sends no mail, and explicit Send, proof, OTP,
CSRF/context, selection, lifecycle and current server policy remain mandatory.
Authenticated `/connect/logout` GET/POST ingress retains the application cookie
until valid browser/user-bound local confirmation; OIDC ingress and validated
protocol completion remain supported. These local controls do not establish
external SMTP/provider, browser or production acceptance.

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

The public provider boundaries remain independent. Provider Proof 1.0 may
return only assured identity/profile data after credential validation. Provider
Metadata 1.0 carries no credential and no affiliation, role, group, directory
placement or linking authority. Legacy Password Sync is a separate,
unversioned, default-disabled aggregate password-write endpoint. It exposes no
internal targets or routing topology and never automatically resends an unknown
or possibly dispatched write. See
[Provider Proof](PROVIDER_PROOF_CONTRACT.md),
[Provider Metadata](PROVIDER_METADATA_CONTRACT.md), and
[Legacy Password Sync](PASSWORD_SYNC_CONTRACT.md).

Affiliation requires an independent upstream owner. This implementation has no
affiliation-owner decision surface, and missing owner evidence is not inferred
from email, namespace, subject, extension members or retained legacy data;
applicable policy fails closed. The fresh email snapshot cache imports no
affiliation-bearing draft rows. Existing draft tables are retained unmapped for
data preservation, not trusted, copied or reactivated. A new validated metadata
refresh is the only entry into the current cache.

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

### Deployment-Controlled One-Time Credential Migration

The Stage 2 credential-migration ceremony and its recovery-proof gate are
implemented but default disabled. They are a bounded deployment mode, not a
login-name heuristic, ordinary password change, password-expiry handler,
general forgot-password flow, per-client route, or organization-role policy. Enabling
the feature still requires the approved operator-controlled database migration
and connected-environment validation; local implementation evidence alone does
not establish production readiness.

The recovery email is a separate security record. It is never inferred from
`ApplicationUser.Email`, `Person.Email`, or another profile/contact field, and
it is distinct from permanent email MFA. Authenticated self-service change and
revocation require the same account subject plus MFA or hardware-key AMR;
password-only AMR is denied. The current authorizer does not independently
enforce authentication age or freshness. A newly enrolled or replaced address
cannot be used until a code sent to that destination verifies possession.
Legacy credential proof plus a caller-supplied new address is never sufficient
to authorize reset.

When effective policy requires migration email OTP, the code is sent only to a
verified recovery destination. Codes are cryptographically generated, stored
only as hashes, purpose-, continuation-, and browser/CSRF-context-bound,
attempt/expiry/cooldown limited, and atomically single-use. Missing, invalid,
expired, exhausted, replayed, unavailable, or mismatched proof is denied before
the migration continuation is consumed or any directory reset is attempted.

Administrator assistance requires `Permissions.Users.Update`, the current
actor identity, and MFA or hardware-key AMR. It permits only verified-address
resend, identity-checked and reasoned address replacement followed by new
destination verification, or a reasoned, short-lived, single-use reset approval
bound to one active ceremony. The administrator does not receive or transfer an
approval token and does not reset the credential on the user's behalf. Missing,
expired, replayed, cross-ceremony, unavailable, or ambiguous state fails closed.

After OTP or approval consumption, the server atomically consumes the existing
continuation, performs exactly one reset attempt, independently binds with the
new password and verifies eligible managed status, records
`DirectoryCredentialCommitted`, then completes local binding/profile and
eligibility work before `LocalFinalized`. Existing local MFA and issuance guards
remain afterward. A failed or uncertain reset, bind, status check, or marker
write does not infer completion, retry the credential against another
authority, or fall back to Local or legacy proof.

OTP success and administrator assistance are ceremony-scoped only. Neither
creates permanent proof, completes migration directly, satisfies local MFA, nor
sets `EmailMfaEnabled`. Recovery audit records contain sanitized event category,
correlation ID, target account ID, and actor account ID only; they exclude
addresses, codes, credentials, proof/approval tokens, provider details, reason
text, and identity-check evidence.

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

### Person Association and Ownership Transfer

Account linking and unlinking require both `persons.update` and `users.update`,
including when a Person currently has no roles or assets. Association grants
durable Person membership and can affect ownership of assets created later.
When either the linked account or the source account has IdP roles, the operation
also requires `roles.update`. Protected-role operator MFA policy applies, and
new protected-role recipients must meet the configured target MFA policy.
Unlinking preserves legitimately assigned roles; it cannot bypass these checks.

Linking, unlinking and asset transfer resolve the authenticated administrative
authority before mutating tracked Person IDs, roles or owners. For each asset
type present, the actor needs its update permission plus exact current Person
ownership or the full interactive IdP Admin role. Delegated owners cannot use
these operations to manage approved administrative clients or standard OIDC
scopes. Transfer requires existing source and destination Persons; the exact
source owner may transfer to another existing Person under `persons.update`.
An audit user ID or a bearer role claim is never evidence of ownership or Admin
authority. Bearer permissions remain bounded by the live administrative grant.

Role assignment preflights every account receiving a protected role, including
same-Person siblings missing a role already held by the selected account,
before any role removal or addition. Full user updates also preflight before
profile writes. Target MFA enforcement remains optional; when enabled, retired
passkeys never qualify and active passkeys count only when configured.

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

Existing-account association is committed only after required local MFA.
Explicit password confirmation shares the configured per-IP login budget
with the normal login page. Verified-email automatic matching and authenticated
profile linking use the same completion boundary; provider AMR is not local MFA.
A pending link expires after five minutes and binds the subject, security stamp,
provider/key and a nonce in the protected temporary MFA cookie. Successful TOTP,
email OTP, recovery or user-verified passkey completion marks only that request
for association. Cookie refresh, unrelated login and factor-management
reauthentication do not complete an abandoned intent. Completion rechecks current
eligibility, collision and provider limits. An authorized initial TOTP seed
creation carries only its own security-stamp change through the same intent.
Automatic email matching selects an account; association still requires an
existing local factor ceremony, or explicit successful password confirmation.
New-factor enrollment may finish a pending association only after that password
confirmation, never on the strength of the candidate provider or an old cookie.

Authenticated profile linking additionally requires a new five-minute operation
bound to the current subject, security stamp and selected provider. Only a
successful existing password, enabled TOTP/email factor, recovery code or
user-verified existing passkey ceremony grants it. Old cookie AMR, provider AMR,
cookie refresh and factor enrollment do not grant it. The provider callback must
carry the same nonce; completion consumes the operation. Passwordless accounts
may use an existing factor or passkey. An external-only account without an
established local factor cannot use its existing SSO cookie to add a login.
Ordinary sign-in through an existing provider-key association is unchanged.
Passkey enrollment also requires performed reauthentication before creation.
Generic cookie refresh cannot mint an enrollment proof. Assertion `id` and
`rawId` must decode to the same credential at the shared verification boundary;
operation proofs use that credential's canonical ID.

Administrative TOTP/email MFA reset requires `users.reset_mfa`, an interactive
operator cookie, a fresh existing-MFA ceremony bound to the target account, and
a nonempty reason of at most 500 characters. M2M and impersonated operators are
denied. Protected-role targets require the operator's full active IdP Admin role
and current Admin membership. Reset rotates the security stamp, revokes
UserSessions and stored OpenIddict authorizations/tokens, and records actor,
target and reason. Passkeys remain registered. Cookie stamp checks use their
configured validation interval (one minute by default); offline validation of
issued JWTs remains subject to the client's token validation and lifetime.

Role-detail members require `users.read` for account metadata in every
detail/create/update response. Otherwise members contain only `id` and
`displayName`; an email-shaped username is not a display-name fallback.

The metrics HTTP client does not follow redirects. Configured internal HTTP
endpoints remain supported. Outside Development/Test, explicit signing and
encryption PFX files are required at startup. Operator-managed self-signed PFX
files remain supported; missing files never select development credentials.

MFA Setup normalizes its return destination on the server before rendering it
or handling skip. All enrollment methods use that local destination, including
authorization URLs with `request_uri` query values; unsafe or missing values
fall back to `/`.

JIT account creation also enforces `AutoLinkMatchingEmail`. A local username
collision is rejected before profile mutation unless automatic linking is
enabled and the assured external email matches that account's email. Users
can use the explicit linking flow with local credentials instead.

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
