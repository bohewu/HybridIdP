# Phase 23: Generic Upstream Authentication Boundary

## Status and replacement disposition

This document replaces the former unchecked AD proposal. The prior proposal's
implicit Local-to-AD-to-Legacy credential fallback and IdP-side directory
password-history or policy overlay are rejected. They are not implementation
instructions.

Phase 23 defines a future, generic OSS boundary only. Direct configurable
AD/LDAP is the preferred credential provider, but it is not implemented in the
current product. Current password behavior remains Local plus the configurable
LegacyAuth HTTP compatibility adapter.

This plan has no dependency on organization-specific identity synchronization
systems, organization-specific identity data stores, private APIs, schemas,
identifiers, databases, or organizational policy. It requires no raw national identifier.

## Scope

The future boundary must:

- Support direct, deployment-configured AD/LDAP credential validation and
  profile retrieval as the preferred upstream provider.
- Deliver both the Stage 1 legacy-authenticated directory-binding/profile path
  and the Stage 2 credential-reset/directory-authentication path in the same
  implementation delivery, while keeping
  their deployment enablement independent and disabled by default.
- Permit a configurable, standardized, provider-neutral authentication/profile
  API adapter only when direct directory access cannot supply a documented
  required capability.
- Preserve HybridIdP as the local authority for shadow users, Person
  multi-account linkage, JIT, eligibility overlays, MFA, sessions, tokens,
  claims, and consent.
- Define safe provider-result, capability, matching, lifecycle, and operational
  requirements before any implementation is approved.

This plan does not authorize packages, provider code, migrations, configuration
parsing, runtime changes, test-code changes, connected AD/LDAP testing, or
changes to Local or LegacyAuth behavior.

It also does not implement the opt-in one-time migration contract below. That
contract is documentation only and does not authorize product, configuration,
package, migration, test, or connected-directory work.

## Current baseline

`LoginService` currently authenticates a found local user locally; when no
local user is found, it calls the configurable `ILegacyAuthService` HTTP
adapter. There is no AD/LDAP branch or directory dependency. LegacyAuth remains
an accurately documented compatibility adapter and must not be silently
upgraded, relabeled, or assumed to satisfy this future generic contract.

Existing seams include durable provider-key links, JIT provisioning, Person to
multiple-`ApplicationUser` linkage, local lifecycle checks on cookies and token
issuance, MFA, sessions, claims, and consent. Future work may use those seams
only after satisfying this specification's tighter contract and assurance
rules.

## Target boundary

### Explicit provider selection

Each password sign-in selects one configured credential authority explicitly.
The selected authority can be Local, direct AD/LDAP, or a separately configured
standardized API adapter. A login name must not cause heuristic provider
selection, and one submitted password must not be tried against multiple
authorities.

If the selected upstream is unavailable, rejects the credentials, returns an
ambiguous or malformed result, or times out, the sign-in is denied. It does not
fall back to Local, AD/LDAP, LegacyAuth, or any other provider. The optional
API adapter is not an AD-to-API fallback; it is an explicitly selected provider
whose use is justified by a recorded direct-directory capability gap.

### Provider types and responsibility

Direct AD/LDAP uses deployment-configured endpoint, transport/authentication
mode, service/user credential, search, and attribute choices. LDAPS or StartTLS
may be selected when the directory supports them, but TLS is not mandatory.
A Windows Active Directory deployment may instead select authenticated
Kerberos/NTLM Negotiate behavior, including signing or sealing when supported,
to match an existing controlled-domain integration. Anonymous and LDAP simple
binds that expose a password without channel protection are prohibited. The
directory owns validation of directory credentials and the directory account's
enabled/disabled state, lockout, password expiration, password changes, and
password policy.

HybridIdP must not replicate directory password history or impose another
password-policy overlay for directory credentials. HybridIdP retains authority
for its local shadow `ApplicationUser`, Person relationship, durable links and
JIT, local eligibility overlays, MFA, cookies, `UserSession` lifecycle,
OIDC/OAuth tokens, claims, and consent.

The optional API adapter is a new standardized, provider-neutral
authentication/profile contract with a deployment shape comparable to
LegacyAuth. It is used only when a required capability is unavailable directly
from the directory and must declare the same result and assurance concepts as
the directory provider. Existing LegacyAuth is not this contract.

## Contract requirements

### Provider descriptor and capabilities

Each future provider descriptor must declare:

- a namespaced provider identifier;
- the required credential-validation and profile capabilities it supplies;
- whether it supplies bounded upstream account-status revalidation;
- each field it declares verified and the purpose for which that assurance is
  valid; and
- an optional stable-person key only when it is declared stable for the person,
  immutable, unique within the provider, and suitable for the configured
  matching purpose.

The descriptor must establish why a selected API adapter is needed when direct
directory access is not sufficient. It must not create a silent fallback path.

### Authentication and profile result

An upstream result must state an explicit allowed or denied outcome and a
safe-to-handle reason category. A successful result used for local linking must
include the provider namespace and an immutable, provider-scoped stable
provider key. Mutable usernames, email addresses, display names, and directory
distinguished names are not valid durable provider keys.

Results may contain only contract-declared profile fields and associated
assurance. An unavailable, malformed, ambiguous, or timed-out result is not a
success and cannot trigger another credential authority.

### Linking and JIT

Provider-namespace plus immutable provider-key matching precedes every
heuristic. Email or optional stable-person-key matching can bind an existing
Person or `ApplicationUser` only when the selected provider's documented
assurance meets local policy. An unassured email or person key may support
isolated-account provisioning only; it must not auto-link an existing account
or Person.

Local terminal, deleted, inactive, locked, or locally ineligible
`ApplicationUser` and Person state wins over an upstream success. Denial occurs
before JIT creation or mutation, orphan auto-heal, principal creation, token
issuance, or session continuation. Upstream denial or disablement denies new
authentication, and local eligibility overlays may be stricter.

### Data, claims, and MFA

Only an explicit local allowlist may transfer approved, contract-declared
upstream values into local profile state or issued claims. Arbitrary upstream
claims, credential metadata, secrets, raw identifiers, and internal directory
attributes are excluded from profiles, tokens, logs, and audit detail.

MFA, AMR, ACR, and assurance policy remain local. An upstream MFA or assurance
assertion cannot satisfy local MFA/AMR/ACR requirements unless a documented,
provider-specific trust and mapping rule has been explicitly verified.

### Sessions, grants, and revalidation

Current local `ApplicationUser` and linked Person eligibility must be checked
on every Identity-cookie validation and before new authorization-code, refresh,
device, password, or equivalent grant issuance. The future provider descriptor
must define a bounded upstream status-revalidation or revocation response.

Existing self-contained access tokens may remain valid until their expiry.
Changing that behavior needs a separate approved revocation design; upstream
status loss must not be described as retroactively invalidating an already
issued self-contained token without it.

### Transport, secrets, cancellation, and audit

Every provider declares its deployment-selected transport and peer-
authentication policy. HTTPS with certificate and endpoint validation remains
the portable cross-host default. A same-host private Docker-network API hop may
use HTTP only when the service is not host-published, authenticates the caller
with a high-entropy deployment secret, and the deployment records that trust
boundary. Directory operations may use LDAPS, StartTLS, or authenticated
Windows Negotiate according to directory capability; the selected non-TLS mode
must not use anonymous or simple password bind. The selected mode must pass a
connected read capability check before Stage 1 is enabled, then constrained
reset and independent-bind capability checks before Stage 2 is enabled.

All provider operations use bounded timeouts, cancellation propagation, and
fail-closed error handling. Provider credentials, including directory bind or
API credentials, come from secret configuration. Security and audit events are
sanitized and exclude passwords, tokens, bind secrets, raw identifiers, and
unnecessary profile values.

### Complete delivery with staged deployment

The implementation delivers both stages below. They are deployment stages for
a controlled organization credential transition, not separate optional development
deliveries.

Stage 1 keeps provider as the selected credential authority. After successful
provider authentication, HybridIdP performs a read-only directory lookup using
the assured canonical account returned by the provider. A unique managed match
may create the namespaced immutable `objectGUID` binding and refresh only
allowlisted non-empty display profile fields (`displayName`, `givenName`, `sn`,
`mail`, `department`, `title`, and `employeeID`). Directory lookup or display-
profile refresh failure does not overturn an otherwise successful Stage 1
provider sign-in, but it creates no binding and provides no authorization
evidence. HybridIdP roles and permissions remain local; AD group membership is
not automatically imported.

Stage 2 enables the credential-migration ceremony, directory password commit,
independent new-password bind, and normal directory authentication for accounts
whose durable migration state is complete. provider remains the one selected
legacy proof authority for unmigrated accounts; it is never an AD-failure
fallback. Accounts without an existing binding may establish one from the same
successful assured provider result and a unique managed directory lookup before
any reset is authorized.

Typed settings define the switches, while deployment environment configuration
supplies their effective values. `DirectoryIntegration.Enabled`,
`DirectoryIntegration.AuthenticationEnabled`, and
`CredentialMigration.Enabled` default to `false`. Stage 1 enables only the
first switch. Stage 2 enables all three. Enabling migration without directory
integration and authentication is an invalid startup configuration. Provider
endpoints, base DNs, and secrets are deployment configuration; secrets do not
belong in committed settings. These security-sensitive switches require a
deployment/restart and are not mutable Admin UI settings.

## Future one-time legacy-proof-to-directory credential migration

This authoritative future contract is a narrow, deployment-controlled ceremony
that is separate from ordinary sign-in. It is not present in the current
product: current password behavior remains Local plus the configurable
LegacyAuth HTTP adapter, while direct configurable AD/LDAP remains future. It
does not relabel current LegacyAuth as a directory provider or introduce a
fallback route. The companion [Authentication Integration Guide](../AUTHENTICATION_INTEGRATION.md#future-one-time-legacy-proof-to-directory-credential-migration)
contains the same integration contract.

### Mode, durable authorization, and binding resolution

The ceremony is opt-in only through explicit deployment-controlled migration
mode and request policy, bounded by a migration window and explicit operator
cutoff. The selected path is fixed before the password is submitted: completed
accounts select directory authentication; unmigrated accounts select provider
proof. A submitted password is never attempted against both. Disabled, expired,
cutoff, revoked, or completed migration does not enter legacy proof or reset.

Each migration needs a durable, queryable one-time record with `Required`
eligibility before reset and a completed outcome afterward. For an account
already bound during Stage 1, request policy may establish or confirm `Required`
before proof. For a late account without that binding, an assured successful
provider result supplies the stable provider subject and canonical account used
for one exact, read-only managed-directory lookup. Only a unique eligible object
may atomically create or confirm both the immutable `objectGUID` binding and the
`Required` record. That write must complete before the same proof advances state
to `ProofValidated`; no reset is permitted before then. Email, display name,
directory DN, unassured fields, or raw national identifiers cannot substitute
for the provider subject or immutable directory key.

Lookup ambiguity, absence, malformed data, unavailability, timeout, or
ineligibility creates no binding, performs no reset, and fails closed with a
uniform response. `pwdLastSet=0` may be a routing or supporting directory-state
signal, but is never durable eligibility, progress, or completion state.

### Proof, ticket, and credential authority

One ceremony attempt selects exactly one hardened legacy proof provider and one
directory authority. The proof provider must use its declared authenticated transport,
bounded timeouts, cancellation propagation, safe explicit allow/deny results,
and no credential logging. Its successful result supplies the assured,
namespaced stable subject and canonical account used to confirm or create the
immutable directory mapping.
Any directory or proof failure, denial, ambiguity, malformed response,
unavailability, or timeout ends the attempt; AD/LDAP failure must never become
a LegacyAuth or Local fallback.

After proof plus durable binding and `Required` confirmation, the server may
atomically advance to `ProofValidated` and issue an opaque, short-lived,
single-use migration ticket. A protected or hashed server-side ticket record binds the
selected provider, legacy subject, immutable directory key/object, eligible
local account, browser/session context, expiry, and state. Atomic consumption,
replay protection, CSRF binding, and rate limits are mandatory. No principal,
cookie (including a partial sign-in cookie), `UserSession`, OIDC/OAuth token, or
grant continuation is allowed before completion.

The mutation uses a least-privilege directory credential and the deployment-
selected, capability-verified directory transport, limited to managed accounts
recorded as eligible. LDAPS is supported but is not required; a deployment
may use the same authenticated Windows Secure/Negotiate behavior as its existing
directory integration. A generic, standardized
credential-management API may be selected only for a documented direct-
directory capability gap; it is not a runtime fallback. The directory owns
password policy, history, expiry, lockout, and credential state. Passwords
remain in memory only and must not be stored, derived, logged, audited,
claimed, or sent through local password-policy enforcement.

### Durable state, verification, recovery, and issuance

The recoverable, queryable state model is:

`Required` -> `ProofValidated` -> `DirectoryCredentialCommitted` -> `LocalFinalized`

`Required` is established either before proof for an already-bound account or
atomically after successful proof and unique managed-directory resolution for a
late-unbound account. In both cases it exists durably before `ProofValidated`.
`ProofValidated` represents the bound ticket after proof. A reset must be
followed by an independent new-password directory bind and managed-account
eligibility/status verification. Only after both establish the directory
credential commitment may the state advance to
`DirectoryCredentialCommitted` and the durable `Required` marker be cleared or
transitioned to `Completed`. `Required` is never cleared or transitioned, and
`Completed` is never written, before the credential commit and its independent
verification.

Only then may local shadow-user, Person, and JIT finalization run, subject to
existing local lifecycle and eligibility checks, advancing to `LocalFinalized`.
Only after that finalization may local MFA run, and only a successful local MFA
may precede cookie, session, claims, consent, token, or grant issuance.
Directory or legacy MFA/AMR does not satisfy local MFA without a separately
documented trust rule.

Interrupted, conflicting, or uncertain reset, verification, or marker outcomes
are denied and require an idempotent, state-aware recovery query or explicit
action. Recovery must fail closed, must not guess success, issue a session,
reuse a ticket, or automatically replay legacy proof. After completion, the
next sign-in uses the explicitly selected direct AD/LDAP path, not legacy proof.
Migration mode and legacy proof are disabled at the migration-window sunset or
operator cutoff.

All pre-proof and failure responses must be uniform enough to prevent account,
eligibility, mapping, marker, directory, or provider enumeration. Audit and
security records may contain only sanitized reason categories and correlation
data; they exclude passwords, tickets, reset secrets, bind credentials, raw
national identifiers, unnecessary profile values, and token-visible sensitive
data. This remains a generic OSS contract with no organization-specific API,
schema, identifier, directory layout, or data contract.

## Future implementation gates

An implementation proposal must, before code is approved:

1. Specify the selected provider and document any direct-directory capability
   gap that justifies the optional API adapter.
2. Define provider descriptor, result, field-assurance, stable-key, timeout,
   cancellation, transport/authentication mode, secret, and sanitized-audit behavior.
3. Define explicit per-attempt provider selection and prove no submitted
   credential can fall through to another authority.
4. Define allowlisted profile and claim mappings, local lifecycle precedence,
   JIT/linking behavior, local MFA treatment, and bounded upstream status
   response.
5. Add separately approved unit, integration, and connected-validation plans.

Relevant existing verification anchors are `LoginServiceTests`,
`JitProvisioningServiceTests`, `ExternalSignInCoordinatorTests`,
`ApplicationCookieCurrentStateValidatorTests`, `TokenServiceTests`, and
`SessionServiceTests`. Connected directory validation is future work and is not
represented as completed by this plan.

## Requirement traceability

| Requirement | Boundary disposition |
|---|---|
| REQ-01 | Status and Current baseline distinguish current Local plus LegacyAuth from future preferred direct AD/LDAP. |
| REQ-02 | Status, Scope, and Target boundary require a generic OSS boundary with no organization-specific dependency. |
| REQ-03 | Scope and Provider types constrain the optional API adapter to a documented direct-directory capability gap. |
| REQ-04 | Explicit provider selection requires exactly one authority and fail-closed handling with no fallback. |
| REQ-05 | Provider types reserve all directory credential and password policy responsibilities to AD/LDAP. |
| REQ-06 | Provider types preserve HybridIdP ownership of local identity, eligibility, MFA, sessions, tokens, claims, and consent. |
| REQ-07 | Authentication and profile result requires namespace plus immutable provider-scoped key and rejects mutable substitutes. |
| REQ-08 | Provider descriptor makes stable-person keys optional and assurance-gated, without raw national identifiers. |
| REQ-09 | Linking and JIT requires provider-key-first matching and assurance before binding existing local identities. |
| REQ-10 | Data, claims, and MFA requires explicit local allowlisting and excludes sensitive or arbitrary upstream values. |
| REQ-11 | Transport, secrets, cancellation, and audit defines deployment-selected TLS or authenticated Negotiate behavior, peer authentication, timeouts, cancellation, secret sourcing, and sanitization. |
| REQ-12 | Linking and JIT gives local terminal and eligibility state precedence before mutation, sessions, principals, or tokens. |
| REQ-13 | Sessions, grants, and revalidation defines local checks, bounded upstream response, and the self-contained token caveat. |
| REQ-14 | Data, claims, and MFA keeps MFA assurance local unless a verified provider-specific mapping exists. |
| REQ-15 | Scope and Future implementation gates make this a documentation-only delivery. |

## Credential-migration requirement traceability

The following migration requirement labels are intentionally scoped to this
ceremony and do not replace the upstream-boundary requirement labels above.

| Migration requirement | Acceptance disposition |
|---|---|
| REQ-01 | Status, Scope, and the migration introduction distinguish current Local plus LegacyAuth from future AD/LDAP and the future ceremony. |
| REQ-02 | Complete delivery with staged deployment and binding resolution require independently gated Stage 1 binding/profile behavior and Stage 2 migration/directory authentication, with a one-time `Required`/`Completed` record distinct from ordinary password state. |
| REQ-03 | Binding resolution and Proof, ticket, and credential authority require one selected authority per submitted password, exact managed-directory resolution before reset, and no fallback. |
| REQ-04 | Binding resolution and Proof require assured stable subject plus canonical account to immutable directory key/object mapping and prohibit mutable or raw-identifier substitutes. |
| REQ-05 | Proof, ticket, and credential authority requires a protected/hashed server-side ticket with bound context, atomic single use, replay, CSRF, and rate-limit controls. |
| REQ-06 | Proof, ticket, and credential authority assigns password policy to the directory, requires a constrained capability-verified reset transport without mandating LDAPS, limits API selection to a proven gap, and keeps passwords memory-only. |
| REQ-07 | Durable state, verification, recovery, and issuance requires independent bind/status verification, post-commit marker ordering, recoverable states, and fail-closed recovery. |
| REQ-08 | Durable state, verification, recovery, and issuance preserves local finalization/MFA/issuance authority and directs post-completion sign-in and sunset behavior. |

## Scenario traceability

| Scenario | Acceptance disposition |
|---|---|
| SCN-01 | Current baseline and Status state that Local plus LegacyAuth remains active until the complete delivered AD/LDAP feature is explicitly enabled through its staged deployment switches. |
| SCN-02 | Explicit provider selection denies unavailable, rejected, malformed, ambiguous, or timed-out selected upstreams without fallback. |
| SCN-03 | Scope and Provider types permit the API adapter only for a documented directory capability gap and keep selection fail closed. |
| SCN-04 | Authentication and profile result plus Linking and JIT require namespace/key-first resolution and assurance-gated matching. |
| SCN-05 | Linking and JIT denies local terminal or ineligible state before JIT, principals, sessions, or grants. |
| SCN-06 | Data, claims, and MFA permits only allowlisted values and prohibits sensitive or unapproved token/log/audit data. |
| SCN-07 | Sessions, grants, and revalidation requires local checks and a bounded upstream response while preserving the expiry caveat. |
| SCN-08 | Data, claims, and MFA rejects upstream MFA or assurance unless a verified provider-specific mapping is documented. |

## Credential-migration scenario traceability

| Migration scenario | Acceptance disposition |
|---|---|
| SCN-01 | Disabled, expired, cutoff, or ineligible migration requests never enter proof or reset. |
| SCN-02 | A successful proof whose canonical account cannot resolve one assured managed object creates no binding, performs no reset, and returns a uniform denial. |
| SCN-03 | Assured proof yields only the bound server-side ticket; replay, expiry, CSRF, and rate-limit failures deny. |
| SCN-04 | Reset success requires independent directory bind/status verification before the completion marker, with no Local or Legacy fallback on directory failure. |
| SCN-05 | Uncertain or interrupted commits remain denied and may use only queryable, idempotent, fail-closed recovery. |
| SCN-06 | Verified marker completion precedes local finalization, then local MFA, then any cookie, session, or token. |
| SCN-07 | Completed accounts use the selected direct directory path and sunset disables migration and legacy proof. |
