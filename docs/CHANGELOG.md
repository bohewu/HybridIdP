# Changelog

This file tracks notable changes and releases for HybridAuth IdP.

## [Unreleased]

No unreleased changes recorded.

## [1.7.1] - 2026-10-19

### Fixed

- Save a contract-valid Proof Email for new canonical users and fill a missing
  Email on the original User/Person at the next valid login. Email does not link
  identities or replace IDs, bindings, MFA, recovery, roles or grants.
- Keep Email confirmation default-off unless the exact provider namespace is
  listed in `ProviderProof.TrustedEmailProviderNamespaces` and Proof supplies
  explicit Email assurance. Without that opt-in, Email is saved without new
  confirmation; existing confirmations and enrolled Email OTP targets remain.

### Changed

- Administrative Claims preview now fetches current approved upstream
  properties without target login or cached/fresh Profile snapshot, and does not
  fall back to stale data when the upstream call fails. Preview writes no
  snapshot, session, consent, grant or token state. Normal login Profile
  persistence and issuance cache/freshness behavior are unchanged.
- Preserve the dedicated `claims.preview` permission, CSRF protection,
  no-store responses and metadata audit behavior.

### Deployment and compatibility

- No EF migration or deployment-script changes were added after 1.7.0. Preserve
  the existing `.env`, certificates, Data Protection keys, account bindings and
  existing accounts; no account deletion is required. The exact
  `ncut.authproxy` Email trust opt-in remains a local deployment choice and
  requires explicit Proof Email assurance; all other providers remain off by
  default.

## [1.7.0] - 2026-10-19

See the [release readiness notes](./RELEASE_READINESS.md) for upgrade and
verification boundaries. Release publication and production deployment are
separate operations.

### Added

- Added administrative Claims preview for a client, account and hypothetical
  allowed scopes, with separate ID Token, Access Token and UserInfo payloads
  and claim inclusion, omission and unknown reasons. Preview requires
  `claims.preview`, preserves existing state and records bounded audit metadata;
  it does not authenticate, issue tokens or refresh Profile snapshots.
- Added a guarded, operator-specific SQL Server cleanup script with preview,
  exact database/count confirmation, dependency blockers and transactional apply.
  It is a separate destructive data operation, never an automatic deployment step.

### Fixed

- Classified configured Proof and retained Legacy credentials as read-only
  organization accounts, while keeping optional browser federation separate.
  Trusted names are preferred for display without renaming existing accounts
  or replacing their identity bindings, MFA, roles or grants.
- Used the canonical upstream account name for new Proof users and denied
  ambiguous name collisions. Ordinary administrative deletion retires local
  identifiers and releases login bindings only after existing eligibility checks.
- Corrected Profile's nested Person name binding; localized the label as
  Name / 姓名 and empty values as Not provided / 未提供. Account display names
  remain separate. Improved first-password-change placeholders and validation
  summary visibility, Claims preview controls, search hints and tab spacing.
- Documented current PKCE S256, code response and client-secret authentication
  behavior without adding new per-client protocol switches.

### Deployment and compatibility

- No EF schema migrations or deployment-script changes were added after 1.6.2.
  Preserve the existing `.env`, certificates, Data Protection keys and bindings.
  Existing 1.6.2 deployments need no mandatory new setting.
- ApplicationManager does not receive `claims.preview` automatically. Claims
  preview remains protected by the dedicated administrative permission.
- Cleanup must follow its own reviewed maintenance procedure after deploying
  the corrected account flow. Existing account deletion is not required to
  upgrade, and existing accounts are not automatically renamed or removed.

## [1.6.2] - 2026-10-17

### Security and dependencies

- Updated transitive brace-expansion resolutions to 1.1.21 and 2.1.7 and applied
  a scoped ExcelJS override for the installed UUID dependency at 11.1.1.
- Removed unused jQuery, jQuery Validation, unobtrusive validation assets and
  their unused TestClient partial. Bootstrap and antiforgery protection remain.
- Retained the documented controls and reopening conditions for the scanner
  findings triaged in issues #2 and #3. Their closure does not mean all findings
  were remediated. ExcelJS's prebuilt browser bundle still contains older UUID
  code; the dependency override does not rewrite those embedded bytes.

### Authorization and administration

- Corrected consent POST validation to apply global and client Required scope
  rules only within the effective request, without adding unrequested scopes.
- Aligned consent, existing grant reuse, refresh narrowing and UserInfo claim
  behavior. Standard name claims require granted `profile`, including when
  custom claim mappings supply them.
- Restored sanitized SQL Server/PostgreSQL and enabled Redis dependency status
  to the permission-protected dashboard; public liveness stays shallow.
- Unified Loading feedback, removed duplicate primary spinners, guarded linked
  account dialogs during submission, and completed audit event translations
  and interactive cursor feedback.

### Deployment and compatibility

- Fixed readiness probes to send the configured public Host header.
- Added the guarded one-time SQL Server Legacy-to-Provider-Proof binding script
  and corrected setup wizard, environment example and upgrade documentation.
- No EF schema migrations were added after 1.6.1. Preserve the existing `.env`,
  certificates and Data Protection keys. Legacy backfill is a separate approved
  data operation and is not automatically run during deployment.
- Existing clients do not require blanket re-registration for the consent fix.
  Clients displaying standard names must allow, request and obtain `profile`;
  a custom employee-ID scope does not imply it. `sub` and claim nesting are
  unchanged. Cumulative older upgrades retain their documented resource approval
  and provider-contract requirements.

## [1.6.1] - 2026-10-16

Security servicing patch for 1.6.0. Authentication policies, provider contracts
and database schema are unchanged from 1.6.0.

### Security and maintenance

- Updated the pinned .NET SDK and ASP.NET runtime images; the Linux amd64
  runtime is .NET 10.0.13. The Node 24 frontend build image is unchanged.
- Pinned System.Security.Cryptography.Pkcs to 10.0.13 in Infrastructure and
  updated System.Security.Cryptography.Xml to 10.0.13 in both migration projects.
- Updated the pinned image-workflow checkout action to 7.0.1.
- Corrected nullable annotations in the authorization-service session mock.
- Aligned the repository VERSION metadata with this release at 1.6.1.

### Deployment and verification

- No migrations were added after 1.6.0. A database already at the 1.6.0 target
  requires no new SQL for this patch. Upgrades from 1.5.x or 1.4.x must still
  follow the [1.6.0 upgrade requirements](./RELEASE_READINESS.md).
- The final dependency source passed an isolated solution build with no warnings
  or errors, 59 authorization-service tests and a NuGet vulnerability audit of
  16 projects with no reported vulnerabilities. Earlier local image/browser
  checks preceded the final cryptography pins; they do not verify the published
  1.6.1 image. Image publication and production deployment remain separate.

## [1.6.0] - 2026-10-16

This release includes the changes since 1.5.0. See the
[upgrade notes](./RELEASE_READINESS.md) before replacing an existing deployment.
Image publication and production deployment are separate operations.

### Added

- Added default-disabled recovery identity verification, recovery-email selection
  and Provider Lifecycle Status integration. Provider contracts remain independent
  and deployment-controlled.
- Expanded the Web TestClient with login, forced sign-in, consent/error scenarios,
  claims/token status, UserInfo, refresh, logout and account-management entry points,
  with links to the other samples.
- Added configurable recovery precheck hints and bottom notices, including resource
  keys, and localized recovery placeholders and passkey guidance.
- Added default-disabled Provider Profile 1.0, approved source snapshots on Person,
  local content hashes and configurable freshness, plus direct property mapping
  and bounded Boolean claim rules in the existing claim administration form.
- Added typed StringArray mapping and visual claim conditions using Equals,
  NotEquals, StartsWith, Contains, All (AND) and Any (OR). Available properties
  come from the deployment's AllowedProperties configuration.
- Added a unified account recovery-code entry while preserving existing TOTP,
  passkeys and usable legacy codes, with one-click copying of newly issued codes.

### Fixed

- Clarified authentication methods, MFA status and purpose-specific confirmation
  before security changes. Fixed Email OTP enrollment followed by TOTP setup,
  method switching, last-factor removal controls and passkey inventory refresh.
- Preserved CSRF validation while recovering stale logout confirmation after a
  sign-in state change, so users can explicitly confirm logout again.
- Fixed audit filtering, pagination, event localization and interactive cursor
  states. Removed misleading optional-cookie consent controls.
- Removed stale layout CSS references and guarded nullable authentication state.
- Isolated schema-only migration from website issuer and signing-certificate
  setup. The Windows helper restores caller environment/location and propagates
  failures; the deployment guides now explain PowerShell and Bash cleanup.

### Security

- Bound delegated client ownership to immutable application keys. Separated API
  catalog visibility from usage approval; restricted API scopes require resource
  owner or IdP administrator approval for the specific client/scope/resource.
- Hardened current consent and external-grant checks, consent-page encoding and
  cookie/bearer CSRF handling.
- Bound authentication assurance to the current account and security state.
  Enrollment alone does not establish performed MFA; sensitive factor removal,
  account linking and administrative reset operations require the appropriate
  current authority.
- Hardened OTP, TOTP, recovery-code and proof consumption, including concurrent
  update handling, recovery context preservation and reserved-proof revocation.
- Applied local password age/history rules at the credential authority boundary
  and protected enrolled email-MFA destinations. Hardened transactional account
  and role updates and audit attribution.
- Bounded provider responses and rejected redirects for credential-bearing
  provider calls. Added explicit provider transport and optional SMTP TLS controls.
- Hardened deployment secret, certificate, proxy and readiness handling, and
  restricted release publication to the intended main workflow. Updated
  source-map-js to the patched 1.2.2 release.

### Deployment and client impact

- SQL Server and PostgreSQL each have five migrations after 1.5.0. Use the
  backup-first, schema-only migration procedure with the exact deployment image.
- Existing IdP cookies without the new assurance marker require sign-in again.
  This change does not itself revoke already-issued self-contained access tokens.
- Existing delegated ownership rows need administrator-verified immutable-key
  binding. Existing scope permissions or end-user consent alone do not authorize
  restricted API scopes; missing resource approval can produce `invalid_scope`.
- Local accounts with unknown password-change age may enter the required-change
  flow when password expiration is configured.
- Upstream API login now uses Provider Proof 1.0 independently of directory
  integration. The flat LegacyAuth reader/settings are retired; verify existing
  adapter contracts and historical identity links before enabling the new path.
- Optional Proof, Profile, recovery, lifecycle and directory features remain
  disabled by default. Real AuthProxy integration and its Profile producer remain
  deployment work; see the release readiness evidence limits.

## [1.5.0] - 2026-09-21

This release finalizes the 1.5.0 source tree. Container publication is performed
by the tag workflow; production migration and deployment remain separate release
steps and are not claimed by this entry.

### Added

- Added native password recovery with recovery-email proof, OTP and
  administrator-assistance ceremonies, required-password-change handling,
  one-time temporary credentials, pending directory-operation settlement and
  configurable OSS-neutral recovery guidance.
- Added default-disabled directory credential migration and recovery paths with
  exact provider-scoped identity bindings, protected directory transports and
  operator-controlled schema migration support for SQL Server and PostgreSQL.
- Added Provider Proof Contract 1.0, the affiliation-free Provider Metadata
  Contract 1.0 with JSON Schema and fixtures, and the independently unversioned
  Legacy Password Sync aggregate contract.

### Changed

- Upgraded the .NET servicing, identity, observability, frontend and test
  dependency sets, including Tailwind CSS 4.3.
- Decoupled Provider Metadata from IdM and affiliation ownership. Withdrawn
  draft affiliation data remains quarantined and is neither imported nor used
  as identity, role, group or linking authority.
- Hardened scheduled Person lifecycle processing with cancellation propagation,
  bounded batches and durable recovery for incomplete token revocation.
- Reduced the published application dependency surface by referencing only the
  required OpenIddict server and validation packages and excluding EF Design
  runtime assets.

### Security

- Preserved default-off activation, fail-closed provider selection, bounded
  timeouts, durable claim-before-dispatch, replay suppression and the
  no-auto-retry barrier for unknown or possibly dispatched credential writes.
- Recovery and lifecycle transitions preserve local password-hash ownership,
  rotate security state and revoke sessions, authorizations and tokens at the
  established completion boundaries.
- A Person cannot be restored to Active while scheduled token-revocation
  recovery remains pending, including when the proposed activation date is in
  the future.

### Deployment notes

- This release contains additive migrations for both supported database
  providers. Production must use the documented backup-first, schema-only
  migration procedure with the exact image later selected for deployment.
- Directory integration, credential migration, Provider Metadata refresh and
  Legacy Password Sync remain disabled until their independent configuration,
  connected-validation and rollout gates are explicitly approved.
- Local tests and release-candidate image verification do not establish
  production deployment or external-provider interoperability.

## 2025-12-05 — Email Settings Feature — Completed ✅
- Implemented `IEmailService` using MailKit/MimeKit.
- Added Email Settings section in Admin UI (Host, Port, Credentials, SSL, From Address).
- Added "Test Email" functionality in Admin UI.
- Secured settings storage with `ISettingsService`.
- Added unit tests for EmailService logic.

## 2025-11-29 — Phase 9 — Scope Authorization & Management — Completed ✅
- Completed Phase 9.7: E2E tests and documentation for scope authorization flows
- Fixed critical OAuth consent POST bug (scope inputs inside form, resolved redirect loop)
- Improved tampering detection and audit logging for consent submissions
- Admin UI: Client Required Scopes configuration and UI improvements
- E2E tests: All 102 tests passing; feature-auth: 16/16 passing

> See `./archive/phases/phase-9-scope-authorization.md` and `./PROJECT_PROGRESS.md` for details.

---
