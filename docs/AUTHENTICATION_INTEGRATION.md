# Authentication Integration Guide

## Purpose and status

This guide distinguishes the delivered, default-disabled credential-migration
capability from the approved direction for a broader future upstream credential
boundary.

- With its default deployment switches disabled, standard password
  authentication is Local plus the configurable LegacyAuth HTTP adapter.
- The Stage 1 binding/profile and Stage 2 credential-migration and
  completed-account directory-authentication paths are implemented, but remain
  disabled until an approved staged deployment enables their switches.
- Direct, deployment-configured AD/LDAP is the preferred future upstream
  credential provider outside the delivered staged migration capability.
- A provider-neutral authentication/profile API adapter is optional and may be
  selected only when direct AD/LDAP cannot provide a documented required
  capability. It is not an automatic conversion of LegacyAuth.
- The implementation includes both the Stage 1 read-only binding/profile path
  and Stage 2 migration/directory-authentication path. Until they are
  explicitly enabled through deployment switches, Local plus LegacyAuth
  behavior remains unchanged and LegacyAuth is not treated as an AD/LDAP
  provider.

This boundary is generic OSS. It has no dependency on, or data contract for,
organization-specific identity synchronization systems, organization-specific
identity data stores, private APIs, schemas, identifiers, databases, or organizational
policy.

## Current behavior

With the staged switches disabled, `LoginService` first finds a local user by
email or username. If a local user is found, local credential and lifecycle
checks determine the result. If no local user is found, it calls
`ILegacyAuthService`; `LegacyAuthService` is the current configurable HTTP
username/password compatibility adapter. Failed, malformed, and unsuccessful
LegacyAuth responses are not authenticated.

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
`UserSession` lifecycle, claims, consent, and token issuance. The delivered
staged capability uses those seams; it does not by itself establish a general
AD/LDAP provider outside that bounded deployment mode.

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
directory endpoints, transport/authentication settings, credentials, searches, and attribute
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
and fail closed. HTTPS with certificate and endpoint validation is the portable
cross-host default. A same-host, non-published private Docker-network API may
use authenticated HTTP when that deployment trust boundary is explicit and a
high-entropy service credential is still required.

Directory transport is deployment-selectable. LDAPS and StartTLS are supported
but not mandatory. A Windows Active Directory deployment may use authenticated
Kerberos/NTLM Negotiate behavior compatible with its existing controlled-domain
integration, including signing or sealing when supported. Anonymous and LDAP
simple binds that expose passwords without channel protection are prohibited.
Before Stage 1 is enabled, connected capability validation must prove exact
lookup against the intended managed directory boundary. Before Stage 2 is
enabled, it must additionally prove constrained reset and independent
new-password bind.

Bind, client, and other provider credentials come from secret configuration,
never source or logs. Security and audit events must be sanitized: no passwords,
tokens, bind secrets, raw identifiers, or unnecessary profile values.

### Complete delivery and deployment stages

The implementation delivers both stages; the split controls deployment rollout,
not implementation scope.

In Stage 1, provider remains the selected credential authority. After a
successful provider result, HybridIdP uses the assured canonical account for a
read-only exact directory lookup. A unique managed result may create the
provider-namespaced immutable `objectGUID` binding and refresh allowlisted,
non-empty display profile values. Lookup or display-refresh failure does not
overturn the successful provider sign-in, creates no binding, and supplies no
authorization evidence. HybridIdP roles and permissions remain local; directory
groups are not imported automatically.

In Stage 2, the same delivered product enables credential migration, constrained
directory password commitment, independent new-password bind, and normal
directory authentication for completed accounts. provider remains the sole
selected proof authority for unmigrated accounts and is never tried after a
directory-authentication failure. A late unbound account may establish its
immutable binding from an assured successful provider result and unique
managed-directory lookup before reset is authorized.

`DirectoryIntegration.Enabled`,
`DirectoryIntegration.AuthenticationEnabled`, and
`CredentialMigration.Enabled` are typed deployment settings that default to
`false`. Stage 1 enables only directory integration; Stage 2 enables all three.
Migration enabled without directory integration and directory authentication is
an invalid startup configuration. Effective values, endpoints, base DNs, and
secrets come from deployment environment/secret configuration, require a
deployment restart, and are not mutable Admin UI settings.

Stage 2 recovery proof has three additional deployment switches under
`CredentialMigration`; all default to `false`:

- `RecoveryEmailEnabled` enables independent recovery-address enrollment and
  verification.
- `MigrationEmailOtpEnabled` enables migration-purpose email OTP and requires
  both `CredentialMigration.Enabled` and `RecoveryEmailEnabled`.
- `RecoveryAdminAssistanceEnabled` enables bounded administrator assistance and
  requires both `CredentialMigration.Enabled` and `RecoveryEmailEnabled`.

A non-disabled `EmailOtpPolicyFloor` also requires
`MigrationEmailOtpEnabled`. Invalid combinations fail startup validation. OTP
lifetime is configurable from one through ten minutes, attempts from one
through five, and resend cooldown has a sixty-second minimum. Reset approval
lifetime is configurable from one through ten minutes. The committed defaults
are ten minutes, five attempts, sixty seconds, and ten minutes respectively.

## Provider Proof Contract 1.0 and Legacy Password Sync

The implementer-facing wire guidance is [Provider Proof Contract 1.0](PROVIDER_PROOF_CONTRACT.md)
and the [Legacy Password Sync contract](PASSWORD_SYNC_CONTRACT.md). These are
independent boundaries, not a combined legacy API version.

`ProviderProofProvider` implements `IProofProvider` using `ProviderProof`
configuration: `Endpoint`, `SharedSecret`, `Timeout` (five seconds by default),
and `AllowPrivateNetworkHttp` (false by default). Directory integration requires
a protected endpoint and a secret. The configured endpoint receives an
authenticated POST with `X-Internal-Secret`; the existing endpoint path is
`/api/authenticate/login`. The JSON request carries `accountName`, `password`,
`contractVersion` (`1.0`) and `requestedEmailOtpPolicy`.

`ProviderProofContract` defines the independent 1.0 request/result validation.
Successful results require an assured provider namespace, stable subject and
canonical account. Non-success results cannot carry identity or profile data.
The adapter preserves existing timeout, cancellation, response normalization
and failure handling. An explicit unsupported version is invalid; the existing
proof DTO default for an omitted version remains 1.0. This differs from metadata's
required version field and does not imply shared version negotiation.

Legacy Password Sync remains independently configured under
`LegacyPasswordSync` and disabled by default; see the
[configuration and activation gates](DEVELOPMENT_GUIDE.md#hidp-13-legacy-password-sync-configuration).
The request sends exactly `operationId`, opaque `accountIdentity` and
`password` to the one configured `/api/password-sync` endpoint using POST and
`X-Internal-Secret`. The response contains exactly the correlated `operationId`
and aggregate `outcome`. Target names, target collections, internal identifiers,
routing decisions and endpoint topology are private and excluded from the
public boundary. The wire contract is unversioned; `MappingVersion` is local
mapping state, not an API version. Metadata changes neither alter this contract
nor authorize a password write.

The caller still requires an authorized durable source, exact current binding
and mapping, current account/security stamps and a durable single-send claim
before dispatch. For a legacy-only dispatch, that source is an authorized
required-change attempt, a consumed native proof challenge, or a consumed
migration continuation. These sources authorize the already-approved password
operation; the legacy endpoint does not independently authorize a reset. HTTP
redirects and cookies are disabled. Only a bounded HTTP 200 JSON response with
the matching operation and exact aggregate outcome is classified.
`PartialSuccess`, `CommitUnknown`, possible dispatch and untrusted responses
retain the durable `Unknown` barrier and cannot be automatically retried.
Recovery proof, expiry, replay and writeback preconditions remain independently
enforced before this optional step.

Directory/AD and the one Legacy Password Sync endpoint are independently
switchable destinations. With Directory off and Legacy on, an eligible source
produces zero Directory writes and one Legacy dispatch. Directory on and Legacy
off produces one Directory write and zero Legacy dispatches. When both are on,
the Directory write occurs before the Legacy dispatch. When both are off,
neither destination is called.

## Optional provider metadata refresh

The password-free wire boundary is [Provider Metadata Contract 1.0](PROVIDER_METADATA_CONTRACT.md).
It contains only provider identity, email and email trust evidence; affiliation,
directory roles/groups, identity linking and observation ownership are excluded.
Its version is independent of Provider Proof and Legacy Password Sync.

Affiliation and directory placement belong to a separately configured upstream
owner, such as the deployment's identity-management or directory workflow. The
metadata producer must not infer, mirror or synthesize them. This candidate does
not implement such an owner or expose an affiliation-owner decision surface;
missing owner evidence fails closed instead of accepting metadata extensions or
legacy cached affiliation state.

`ProviderMetadataRefresh.Enabled` independently enables the password-free
metadata refresh after successful staged password authentication; it defaults
to `false`. Stage 1 refreshes only a provider namespace and stable subject with
a confirmed durable binding. Completed Stage 2 accounts use the binding in
their migration record after directory authentication and local eligibility
checks succeed, even when legacy proof or migration is disabled.

Failed, inactive, deleted, locked, and unbound attempts do not refresh metadata.
Metadata failures do not change the credential-authentication result, and caller
cancellation propagates. The service receives no submitted password, login
name, or email. It updates only the provider metadata snapshot, not the recovery
email, MFA, permissions, or credentials. This hook does not apply to local-only
login, browser federation, or the migration reset ceremony and does not
implement recovery-email trust or period policy. Apply the generated metadata
snapshot migration through the operator-controlled migration workflow before
enabling the feature. This delivery did not apply migrations or run connected
provider or directory validation.

## One-time legacy-proof-to-directory credential migration

This is the authoritative contract for the implemented, deployment-controlled
ceremony. It is disabled by default and requires an approved staged deployment;
this document does not enable it or claim a connected-directory run. It remains
traceable in the [Phase 23 plan](design_specs/phase-23-ad-integration-plan.md#future-one-time-legacy-proof-to-directory-credential-migration).

The ceremony proves a legacy credential once, establishes a new credential at
the selected directory authority, and then completes local finalization. It is
not an ordinary sign-in route and must not be used as Local, AD/LDAP, or
LegacyAuth fallback.

### Eligibility, mode, and binding resolution

The deployment explicitly enables a bounded migration mode and request policy
for a defined migration window. The selected path is fixed before password
submission: a completed account selects directory authentication; an unmigrated
account selects provider proof. The same submitted password is never tried
against both. Disabled, expired, cutoff, revoked, or completed migration does
not enter legacy proof or reset.

Each migration has a durable, queryable one-time record that records `Required`
eligibility before reset and later completion. For an account already bound in
Stage 1, request policy may establish or confirm `Required` before proof. For a
late account without that binding, successful assured provider proof supplies
the stable subject and canonical account used for one exact read-only directory
lookup. Only a unique eligible managed directory object may atomically create or
confirm both the immutable `objectGUID` binding and `Required`. That write must
complete before the same proof advances state to `ProofValidated`; reset is not
permitted before then. Email, display name, directory DN, an unassured field, or
a raw national identifier must never substitute for the provider subject and
immutable directory key.

An absent, ambiguous, malformed, unavailable, timed-out, or ineligible directory
lookup creates no binding, performs no reset, and fails closed with a uniform
response. `pwdLastSet=0` may be supporting state or a routing hint, but is never
the durable eligibility, progress, or completion record.

### Legacy proof and continuation ticket

A valid ceremony selects exactly one hardened legacy proof provider and exactly
one directory authority. The legacy proof provider must use authenticated
transport, bounded timeouts, cancellation propagation, a safe explicit
allow/deny category, and no credential logging. On success it returns an
assured, stable, namespaced legacy subject plus an assured canonical account.
Those values must confirm an existing immutable binding or resolve exactly one
managed directory object from which the binding is created; ambiguity or
insufficient assurance denies the ceremony. The current LegacyAuth adapter is
not presumed to satisfy this contract merely because it is configured today.

Directory lookup, proof, reset, verification, timeout, malformed response,
unavailability, ambiguity, and denial are all terminal for that attempt. In
particular, an AD/LDAP failure must not fall back to LegacyAuth, Local, a second
directory, or another authority. A legacy proof is evidence for this ceremony,
not an authenticated application session.

Only after successful proof plus durable binding and `Required` confirmation may
the server atomically advance to `ProofValidated` and create an opaque,
short-lived, single-use continuation ticket. Its protected or hashed server-side record must
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

### Recovery email and pre-reset proof

When the effective Stage 2 policy requires email OTP, legacy proof and the
server-held migration continuation are not sufficient to reset the directory
credential. The server sends a migration-purpose OTP only to an independently
persisted, already verified recovery email. `ApplicationUser.Email`,
`Person.Email`, and other profile or contact addresses are never inferred to be
the recovery destination. A legacy credential plus an arbitrary new address is
not sufficient recovery proof, and the migration page accepts no address field.

An authenticated account may enroll, change, verify, or revoke its recovery
address through the account recovery-email API and Account/MFA settings. The
current subject must match the target account and the current principal must
carry MFA or hardware-key AMR evidence. Password-only AMR is denied. This check
does not add a separate authentication-age or freshness requirement. A new or
changed destination remains unusable until its destination-verification code
succeeds. Recovery-email verification is separate from permanent email MFA and
does not set `EmailMfaEnabled`.

Migration OTP codes are cryptographically generated, stored only as hashes,
bound to their purpose, recovery address, continuation, and browser/CSRF
context, and atomically single-use. Before the continuation is consumed or a
directory reset is attempted, Stage 2 must consume either a successful OTP
proof or a reset approval bound to the same ceremony. Missing, invalid,
expired, exhausted, replayed, unavailable, or mismatched proof denies the
attempt and performs no reset.

Administrator assistance is limited to three operations under the existing
`Users.Update` permission and the same MFA or hardware-key AMR requirement:

- resend the migration OTP only to the persisted verified destination;
- after a documented identity check and reason, replace the destination and
  return it to unverified state, after which the user must verify the new
  destination before a migration OTP is sent; or
- after a documented identity check and reason, issue a short-lived,
  single-use approval bound to one active reset ceremony.

The server fails closed unless it resolves exactly one eligible active
continuation for the target. No approval secret is transferred to the
administrator or user. Expired, replayed, cross-ceremony, missing, or ambiguous
approval state authorizes no reset. Audit events retain only event category,
correlation ID, target account ID, and actor account ID; addresses, codes,
credentials, proof tokens, provider details, reason text, and identity-check
evidence are excluded.

### Directory credential commit and ordered finalization

The credential mutation uses a least-privilege reset capability over the
deployment-selected and connected-validated directory transport, restricted to
managed directory objects authorized by the migration record. LDAPS is
supported but not mandatory; a deployment may use authenticated Windows
Secure/Negotiate behavior consistent with its existing directory integration.
A separately configured, standardized credential-management
API is permitted only when a documented direct-directory capability gap
prevents the required operation. It is an explicit authority selection, never
a runtime fallback after a directory-operation failure.

The directory owns password policy, history, expiry, lockout, and credential
state. Passwords exist only in memory for the minimum proof or reset operation;
they must not be persisted, derived, logged, audited, claimed, or passed to
local password-policy enforcement. After a successful reset, an independent
new-password directory bind and managed-account eligibility/status check must
succeed before the migration is recorded as complete. A directory failure or
policy rejection denies the attempt uniformly and does not invoke any fallback.

The durable state sequence is:

`Required` -> `ProofValidated` -> `DirectoryCredentialCommitted` -> `LocalFinalized`

For an OTP-required ceremony, successful OTP or approval consumption occurs
after `ProofValidated` and before the continuation is atomically consumed for
the single reset attempt. The reset is followed by an independent new-password
bind and managed-status verification. Only then may the service record
`DirectoryCredentialCommitted`, perform binding/profile refresh and local
eligibility finalization, and record `LocalFinalized`. Existing issuance and
local MFA guards remain after finalization.

- `Required` is established either before proof for an already-bound account or
  atomically after successful proof and unique managed-directory resolution for
  a late-unbound account. It always exists durably before `ProofValidated`.
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

This delivered migration proof does not add per-client routing, organization-role
policy, ordinary password-change or password-expiry behavior, or a general
forgot-password route. Those remain separate future work requiring their own
authorization and routing design. The approved direction for that separate
work is the [native forgotten-password recovery source of truth](design_specs/hidp-20260907-1-native-password-recovery.md).

### Migration contract traceability

| Migration requirement | Contract location |
|---|---|
| REQ-01 | Purpose and status plus this section state that current Local plus LegacyAuth behavior remains active until the completely implemented AD/LDAP and migration paths are enabled through staged deployment switches. |
| REQ-02 | Complete delivery and deployment stages plus binding resolution require independently gated Stage 1 binding/profile behavior and Stage 2 migration/directory authentication with a distinct durable `Required`/completed record. |
| REQ-03 | Binding resolution and Legacy proof require one selected password authority, exact managed-directory resolution before reset, and no authority fallback. |
| REQ-04 | Binding resolution and Legacy proof require assured stable subject plus canonical account to immutable-directory-key/object mapping and reject mutable or raw-identifier substitutes. |
| REQ-05 | Legacy proof and continuation ticket requires a server-side, bound, short-lived, atomically single-use ticket with replay, CSRF, and rate-limit controls. |
| REQ-06 | Directory credential commit requires a managed-account least-privilege reset over a capability-verified configured transport without mandating LDAPS, constrained API use, directory-owned policy, and in-memory-only passwords. |
| REQ-07 | Directory credential commit and ordered finalization defines the four durable states, independent verification, marker ordering, local finalization, and fail-closed recovery. |
| REQ-08 | Ordered finalization and sunset preserves local lifecycle/MFA/issuance authority, requires subsequent direct directory sign-in, and ends the migration window. |

| Migration scenario | Acceptance disposition |
|---|---|
| SCN-01 | Disabled, expired, cutoff, or ineligible migration requests do not enter proof or reset. |
| SCN-02 | A successful proof whose canonical account cannot resolve one assured managed object creates no binding, performs no reset, and returns a uniform denial. |
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

Any expansion beyond the delivered staged capability must be separately
approved and include provider contracts, configuration validation, package
choices, migrations if needed, runtime behavior, tests, and any connected
AD/LDAP validation. Relevant existing verification anchors are `LoginServiceTests`,
`JitProvisioningServiceTests`, `ExternalSignInCoordinatorTests`,
`ApplicationCookieCurrentStateValidatorTests`, `TokenServiceTests`, and
`SessionServiceTests`.

The Phase 23 specification contains requirements and scenario traceability for
this boundary. It does not authorize enabling deployment switches or a
connected-directory operation.
