# Authentication Integration Guide

## Purpose and status

This guide distinguishes the authentication integrations that exist today from
the approved direction for a future upstream credential boundary.

- Current password authentication is Local plus the configurable LegacyAuth
  HTTP adapter. It does not implement AD, LDAP, or another directory provider.
- Direct, deployment-configured AD/LDAP is the preferred future upstream
  credential provider. It is a specification only; no provider, package,
  configuration parser, migration, runtime behavior, or connected directory
  test is delivered by this document.
- A provider-neutral authentication/profile API adapter is optional and may be
  selected only when direct AD/LDAP cannot provide a documented required
  capability. It is not an automatic conversion of LegacyAuth.
- An opt-in, one-time legacy-proof-to-directory-credential migration ceremony
  is also future only. It does not change current Local plus LegacyAuth
  behavior, and it does not make LegacyAuth an AD/LDAP provider.

This boundary is generic OSS. It has no dependency on, or data contract for,
organization-specific identity synchronization systems, organization-specific
identity data stores, private APIs, schemas, identifiers, databases, or organizational
policy.

## Current behavior

`LoginService` first finds a local user by email or username. If a local user
is found, local credential and lifecycle checks determine the result. If no
local user is found, it calls `ILegacyAuthService`; `LegacyAuthService` is the
current configurable HTTP username/password compatibility adapter. Failed,
malformed, and unsuccessful LegacyAuth responses are not authenticated.

LegacyAuth is current compatibility behavior, not AD/LDAP support and not the
future generic provider contract. In particular, it does not by itself declare
the stable-key, matching-assurance, capability, or MFA-trust semantics required
of a future upstream provider.

For browser-based federation, the repository also has external-login flows
separate from password authentication. Existing durable external-login links
are considered before email matching. Google and Microsoft email handling use
provider-specific assurance rules; a missing or untrusted assurance signal must
not select an existing account or Person by email. Explicit external-account
linking remains a locally protected flow.

The current local seams include JIT provisioning and durable provider-key
links, `ApplicationUser` and Person lifecycle validation, IdP MFA, cookies,
`UserSession` lifecycle, claims, consent, and token issuance. They are the
baseline for a future provider integration, not evidence that AD/LDAP is
implemented.

## Future upstream credential boundary

### Provider selection and failure handling

Every password authentication attempt must select exactly one credential
authority explicitly. The selection is deployment and request-policy driven;
it is not inferred from a login-name pattern and does not probe multiple
authorities with the submitted password.

When an explicitly selected provider is unavailable, rejects credentials,
returns malformed or ambiguous data, or exceeds its timeout, the attempt is
denied. It must never silently fall through to Local, AD/LDAP, LegacyAuth, or
another provider. Local authentication is used only when Local was explicitly
selected for that attempt.

The preferred future provider is direct AD/LDAP, using deployment-configured
directory endpoints, TLS settings, credentials, searches, and attribute
mappings. An optional standardized authentication/profile API adapter may be
configured only after a documented required capability is unavailable through
the selected direct directory. The API adapter has a provider-neutral contract
and a deployment shape comparable to LegacyAuth, but is independently selected
and follows the same fail-closed rule.

### Authority split

For directory-sourced credentials, AD/LDAP owns credential validation,
enabled/disabled state, lockout, password expiration, password change, and
password policy. HybridIdP must not store, derive, or enforce directory
password history, nor create another IdP-side password-policy overlay for
directory credentials.

HybridIdP remains authoritative for its shadow `ApplicationUser`, one
Person-to-many-`ApplicationUser` relationship, durable account links, JIT
provisioning, local eligibility overlays, MFA, cookies, `UserSession`
lifecycle, OIDC/OAuth tokens, claims, and consent. Local policy may impose a
stricter eligibility denial than an upstream provider.

### Provider contract concepts

Future providers must describe these concepts before they can be selected:

| Concept | Required boundary meaning |
|---|---|
| Provider namespace | A stable, namespaced identifier that identifies the configured provider instance and its key space. |
| Provider key | An immutable, provider-scoped stable account key returned on successful authentication and used with the namespace for durable linking. Login names, email addresses, display names, and directory DNs are not durable provider keys. |
| Authentication result | An explicit allow or deny result, a reason category safe for local handling, and only contract-declared identity/profile fields. It must distinguish unavailable, malformed, timeout, and ambiguous conditions from success. |
| Capability declaration | The provider's documented support for the required authentication and profile operations, declared assurance, and any bounded status-revalidation operation. It is used to justify optional API-adapter selection, not to enable fallback. |
| Stable-person key | Optional. It may be used only when the provider explicitly assures that it is stable for the person, immutable, unique within that provider, and suitable for the configured matching purpose. No raw national identifier is required or implied. |
| Field assurance | Provider-specific evidence that a profile or identity field is verified and fit for the specific local matching or mapping purpose. |

Provider-key matching is always first. Email or stable-person-key matching may
link to an existing `ApplicationUser` or Person only when its declared,
provider-specific assurance is accepted by local policy. Unassured values may
support isolated-account provisioning, but must not bind an existing account or
Person. The provider key, email, stable-person key, login name, and display
name have different roles and must not be substituted for one another.

### Profile, claims, and assurance

Upstream authentication and profile data is untrusted except for fields that
the selected provider contract declares and local policy accepts. An explicit
local allowlist is the only path for approved values to update local profile
state or appear in issued claims. Arbitrary upstream claims, credential
metadata, secrets, raw identifiers, and internal directory attributes must not
be logged, placed in audit detail, or made token-visible.

HybridIdP continues to enforce local MFA and assurance requirements. An
upstream MFA, AMR, ACR, or similar assertion does not satisfy local policy
unless a documented, provider-specific trust rule explicitly maps and verifies
it.

### Lifecycle, sessions, and tokens

Local terminal, deleted, inactive, locked, or locally ineligible
`ApplicationUser` and Person state overrides upstream success. It must deny the
operation before JIT creation or mutation, orphan auto-heal, principal
generation, token issuance, or session continuation. Upstream denial or
disablement denies a new authentication.

Current local state must continue to be checked on each Identity-cookie
validation and before new authorization-code, refresh, device, password, or
equivalent grant issuance. A future provider integration must define a bounded
upstream account-status revalidation or revocation response. Self-contained
access tokens already issued may remain valid until their expiry unless a
separately approved revocation design changes that policy.

### Operational security requirements

Each provider operation must use a bounded timeout, propagate cancellation,
and fail closed. Directory and API endpoints require authenticated TLS with
certificate and endpoint validation. Bind, client, and other provider
credentials come from secret configuration, never source or logs. Security and
audit events must be sanitized: no passwords, tokens, bind secrets, raw
identifiers, or unnecessary profile values.

## Future one-time legacy-proof-to-directory credential migration

This is an authoritative future contract for a separately approved,
deployment-controlled ceremony. It is unimplemented: this guide neither
enables a migration mode nor delivers a provider, schema, configuration,
package, test, or connected-directory operation. It supplements, and is
traceable in, the [Phase 23 plan](design_specs/phase-23-ad-integration-plan.md#future-one-time-legacy-proof-to-directory-credential-migration).

The ceremony proves a legacy credential once, establishes a new credential at
the selected directory authority, and then completes local finalization. It is
not an ordinary sign-in route and must not be used as Local, AD/LDAP, or
LegacyAuth fallback.

### Eligibility, mode, and pre-proof lookup

The deployment must explicitly enable a bounded migration mode and request
policy for a defined migration window. The policy must select this ceremony
before any password is accepted or submitted; it must not infer it from a login
name or use ordinary authentication fallback. When the mode is disabled or
expired, an operator cutoff applies, or the account has no durable migration
eligibility record, the request must not enter legacy proof or directory reset.

Each eligible account must have a durable, queryable one-time migration record
that records `Required` eligibility and later `Completed` status. The record
must contain a pre-approved, assured mapping from the eligible local account
and stable, namespaced legacy subject to one namespaced immutable directory key
or directory object GUID. It is a migration authorization and completion
control, not a must-change-password flag, password-expiry state, or local
password-policy state. Email, login name, display name, directory DN, an
unassured field, or a raw national identifier must never substitute for that
mapping.

Before requesting or submitting a legacy password, the service must use that
approved immutable mapping to look up and resolve exactly one managed directory
object and its eligible local account. An absent, ambiguous, malformed,
unavailable, timed-out, or ineligible lookup fails closed with a uniform
response. It must not submit the password to LegacyAuth, Local, the directory,
or any other authority, and it must not disclose account, marker, mapping,
directory, or provider state.

### Legacy proof and continuation ticket

A valid ceremony selects exactly one hardened legacy proof provider and exactly
one directory authority. The legacy proof provider must use authenticated
transport, bounded timeouts, cancellation propagation, a safe explicit
allow/deny category, and no credential logging. On success it returns an
assured, stable, namespaced legacy subject. That subject must exactly match the
pre-approved mapping to the immutable directory key/object; ambiguity or
insufficient assurance denies the ceremony. The current LegacyAuth adapter is
not presumed to satisfy this contract merely because it is configured today.

Directory lookup, proof, reset, verification, timeout, malformed response,
unavailability, ambiguity, and denial are all terminal for that attempt. In
particular, an AD/LDAP failure must not fall back to LegacyAuth, Local, a second
directory, or another authority. A legacy proof is evidence for this ceremony,
not an authenticated application session.

Only after successful proof may the server create an opaque, short-lived,
single-use continuation ticket. Its protected or hashed server-side record must
bind the selected legacy provider, assured legacy subject, immutable directory
key/object, eligible local account, browser/session context, expiry, and
current ceremony state. It must enforce atomic consumption, replay protection,
CSRF binding, and rate limits. The ticket value, password, reset secret, bind
credential, raw national identifier, and unnecessary profile values must never
be logged, audited, claimed, or made token-visible. User-facing pre-proof and
failure responses remain uniform enough to resist enumeration; records may use
only sanitized reason categories and correlation data.

No application principal, cookie (including a partial sign-in cookie),
`UserSession`, OIDC/OAuth token, or grant continuation may be created before
the directory credential and local finalization portions below have completed.

### Directory credential commit and ordered finalization

The preferred credential mutation is a least-privilege LDAPS password-reset
capability, restricted to the managed directory objects authorized by the
migration record. A separately configured, standardized credential-management
API is permitted only when a documented direct-directory capability gap
prevents the required operation. It is an explicit authority selection, never
a runtime fallback after an LDAPS failure.

The directory owns password policy, history, expiry, lockout, and credential
state. Passwords exist only in memory for the minimum proof or reset operation;
they must not be persisted, derived, logged, audited, claimed, or passed to
local password-policy enforcement. After a successful reset, an independent
new-password directory bind and managed-account eligibility/status check must
succeed before the migration is recorded as complete. A directory failure or
policy rejection denies the attempt uniformly and does not invoke any fallback.

The durable state sequence is:

`Required` -> `ProofValidated` -> `DirectoryCredentialCommitted` -> `LocalFinalized`

- `Required` is the durable one-time eligibility record before proof.
- `ProofValidated` records that the bound ticket was issued after assured proof;
  it is not a session and cannot be replayed.
- `DirectoryCredentialCommitted` is reached only after reset plus independent
  bind/status verification. At this point, and never before, the durable
  `Required` marker may be cleared or transitioned to the durable `Completed`
  marker.
- `LocalFinalized` is reached only after shadow-user, Person, and JIT work has
  passed existing local lifecycle and eligibility checks.

Only after `LocalFinalized` may local MFA run; only after successful local MFA
may a cookie, session, claim set, consent continuation, or token be issued.
Directory or legacy MFA/AMR assertions do not satisfy local MFA without a
separately documented trust rule.

The state and its sanitized recovery outcome must be queryable through an
authorized, idempotent recovery path. An interruption, conflict, or uncertain
reset, verification, or marker commit must fail closed: the service must not
guess success, issue a session, reuse a ticket, or automatically replay legacy
proof. Recovery may reconcile known state with the selected directory authority
or require explicit action, but may advance only when the required committed
outcome is independently established.

After completion, the next sign-in for that account must select the explicit
direct AD/LDAP path, not legacy proof. The deployment must disable migration
mode and legacy proof at the bounded migration-window sunset or explicit
operator cutoff. This contract remains generic OSS and introduces no
organization-specific API, schema, identifier, directory layout, or data
contract.

### Migration contract traceability

| Migration requirement | Contract location |
|---|---|
| REQ-01 | Purpose and status plus this section state that the current Local plus LegacyAuth behavior is unchanged and that direct AD/LDAP and migration are future only. |
| REQ-02 | Eligibility, mode, and pre-proof lookup requires explicit deployment/request selection and the distinct durable `Required`/`Completed` migration record. |
| REQ-03 | Eligibility and Legacy proof require lookup before submission, exactly one proof provider and directory authority, and no authority fallback. |
| REQ-04 | Eligibility and Legacy proof require assured stable-subject-to-immutable-directory-key/object mapping and reject mutable or raw-identifier substitutes. |
| REQ-05 | Legacy proof and continuation ticket requires a server-side, bound, short-lived, atomically single-use ticket with replay, CSRF, and rate-limit controls. |
| REQ-06 | Directory credential commit requires managed-account least-privilege LDAPS reset, constrained API use, directory-owned policy, and in-memory-only passwords. |
| REQ-07 | Directory credential commit and ordered finalization defines the four durable states, independent verification, marker ordering, local finalization, and fail-closed recovery. |
| REQ-08 | Ordered finalization and sunset preserves local lifecycle/MFA/issuance authority, requires subsequent direct directory sign-in, and ends the migration window. |

| Migration scenario | Acceptance disposition |
|---|---|
| SCN-01 | Disabled, expired, cutoff, or ineligible migration requests do not enter proof or reset. |
| SCN-02 | A pre-proof lookup that cannot resolve one assured managed object returns a uniform denial without password submission. |
| SCN-03 | Assured proof creates only the bound, protected/hashed server-side ticket; replay, expiry, CSRF, or rate-limit failure denies. |
| SCN-04 | Reset requires independent bind/status verification before the completion marker; directory failure has no Local or Legacy fallback. |
| SCN-05 | Uncertain or interrupted commits stay denied and use only queryable, idempotent, fail-closed recovery. |
| SCN-06 | Verified marker completion precedes local finalization, then local MFA, then any cookie, session, or token. |
| SCN-07 | Completed accounts use the selected direct directory path; sunset disables migration and legacy proof. |

## External browser federation

OAuth/OIDC browser federation remains a separate integration pattern from the
future password-provider boundary. It uses a provider redirect and callback,
then existing durable links before any assurance-gated automatic matching.
Provider-specific email assurance remains required for automatic linking;
explicit linking is protected by the local user session and callback binding.

## Verification and follow-on work

Future implementation must be separately approved and include provider
contracts, configuration validation, package choices, migrations if needed,
runtime behavior, tests, and any connected AD/LDAP validation. Relevant
existing verification anchors are `LoginServiceTests`,
`JitProvisioningServiceTests`, `ExternalSignInCoordinatorTests`,
`ApplicationCookieCurrentStateValidatorTests`, `TokenServiceTests`, and
`SessionServiceTests`.

The replacement Phase 23 specification contains the requirements and scenario
traceability for this boundary. It does not mark any directory integration as
complete.
