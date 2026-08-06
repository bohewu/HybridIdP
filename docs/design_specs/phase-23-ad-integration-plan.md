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

Direct AD/LDAP uses deployment-configured endpoint, TLS, service/user
credential, search, and attribute choices. It owns validation of directory
credentials and the directory account's enabled/disabled state, lockout,
password expiration, password changes, and password policy.

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

Provider calls require authenticated TLS with certificate and endpoint
validation, bounded timeouts, cancellation propagation, and fail-closed error
handling. Provider credentials, including directory bind or API credentials,
come from secret configuration. Security and audit events are sanitized and
exclude passwords, tokens, bind secrets, raw identifiers, and unnecessary
profile values.

## Future implementation gates

An implementation proposal must, before code is approved:

1. Specify the selected provider and document any direct-directory capability
   gap that justifies the optional API adapter.
2. Define provider descriptor, result, field-assurance, stable-key, timeout,
   cancellation, TLS, secret, and sanitized-audit behavior.
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
| REQ-11 | Transport, secrets, cancellation, and audit defines TLS, endpoint validation, timeouts, cancellation, secret sourcing, and sanitization. |
| REQ-12 | Linking and JIT gives local terminal and eligibility state precedence before mutation, sessions, principals, or tokens. |
| REQ-13 | Sessions, grants, and revalidation defines local checks, bounded upstream response, and the self-contained token caveat. |
| REQ-14 | Data, claims, and MFA keeps MFA assurance local unless a verified provider-specific mapping exists. |
| REQ-15 | Scope and Future implementation gates make this a documentation-only delivery. |

## Scenario traceability

| Scenario | Acceptance disposition |
|---|---|
| SCN-01 | Current baseline and Status state that AD/LDAP is future only and Local plus LegacyAuth is current. |
| SCN-02 | Explicit provider selection denies unavailable, rejected, malformed, ambiguous, or timed-out selected upstreams without fallback. |
| SCN-03 | Scope and Provider types permit the API adapter only for a documented directory capability gap and keep selection fail closed. |
| SCN-04 | Authentication and profile result plus Linking and JIT require namespace/key-first resolution and assurance-gated matching. |
| SCN-05 | Linking and JIT denies local terminal or ineligible state before JIT, principals, sessions, or grants. |
| SCN-06 | Data, claims, and MFA permits only allowlisted values and prohibits sensitive or unapproved token/log/audit data. |
| SCN-07 | Sessions, grants, and revalidation requires local checks and a bounded upstream response while preserving the expiry caveat. |
| SCN-08 | Data, claims, and MFA rejects upstream MFA or assurance unless a verified provider-specific mapping is documented. |
