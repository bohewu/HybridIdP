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
