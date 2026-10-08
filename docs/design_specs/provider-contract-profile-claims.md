# Unified provider contracts, profile properties and conditional claims

Status: design agreed and implemented in the local dev worktree on 2026-10-08.
The public Profile exchange is defined in [Provider Profile 1.0](../PROVIDER_PROFILE_CONTRACT.md).
Proof and Profile remain disabled by default. No production migration, account
transition, upstream producer implementation or publication is implied.

Read-only design review completed on 2026-10-08. Two findings were corrected:
initial Proof identity establishment versus subsequent tuple matching, and source
snapshot eligibility after account-to-Person unlinking/reassociation. A focused
recheck passed. This review does not establish implementation, runtime, test or
deployment acceptance.
The subsequent property-update and local-hashing section also passed a focused
design review. The proposed confirmation-age default remains deployment policy
to be selected, not an approved freshness SLA.

## User requirements

- Standardize upstream API authentication on versioned contracts rather than
  maintaining the old flat LegacyAuth response as an alternative login format.
- Distinguish two upstream integration families: contract API and direct AD/LDAP.
  Local password accounts remain a separate credential authority.
- Obtain extra profile properties after successful authentication and store
  accepted source data in Person JSON storage.
- Support direct claim mapping and simple configurable conditional claims.
- Keep organizational identifiers, student classifications and prefix rules in
  deployment/provider configuration rather than OSS business logic.

HybridIdP defines the public contracts and their consumer behavior. Independent
implementers choose their credential authority, data stores, internal protocols
and deployment topology. AuthProxy is one local implementation choice, not a
package, service or source-system dependency of HybridIdP. Implementers may host
Proof and Profile in the same service or separately; the contract does not
prescribe that arrangement.

Each contract has an independently configured full endpoint URI and service
authentication configuration. Endpoints may differ in route, host, deployment
and implementer. Shared hosting does not imply a shared secret or activation.
No endpoint is inferred from another endpoint's base URL. Initial Proof sends
account credentials and establishes an assured provider namespace and stable
subject through the selected authority. Check that tuple against an existing
durable binding when applicable. Subsequent tuple-addressed Profile/Metadata
responses must exactly match the requested tuple. Endpoint equality is not
identity evidence.

## Original implementation gap

Before this change, `LegacyAuthService` read a flat `authenticated` boolean while
the real upstream check returned Provider Proof 1.0. The existing Proof parser
was reusable, but its login route depended on Stage 1 directory integration.
The delivered route now selects Proof independently, and the flat reader and
its configuration are retired. Existing durable identity records are preserved.

These are response formats for an upstream service, not a requirement to operate
two credential authorities. The intended deployment can provide Proof and Profile
through the same service and select one credential authority for each attempt.

The previous claim mapping read approved local fields and mapped them to granted
scopes. The delivered extension adds approved provider properties, bounded
conditional rules and separate source snapshots in Person JSON storage.

Existing Provider Metadata 1.0 is an email-evidence contract for recovery. It
explicitly excludes affiliation/role/group authority and discards unknown members.
Do not reinterpret its existing version as a general profile-property response.

## Provider boundary

| Upstream family | Credential validation | Extra-property source |
| --- | --- | --- |
| Contract API | A deployment-selected implementation of Proof 1.0 | Its optional Profile contract endpoint |
| Direct AD/LDAP | Deployment-configured directory authentication | Approved directory-attribute mappings |

These are architecture families, not interchangeable formats or an automatic
fallback chain. Local accounts retain their local credential authority. API
implementers need not use AD, AuthProxy or any school-specific store. Direct AD
does not require an HTTP Proof service merely to validate a password.

Both families may feed the same approved-source property storage and claim
mapping. Source selection, identity binding and attribute mappings remain explicit.
Generic direct AD/LDAP login is future work outside the delivered Stage 1/Stage 2
rollout capability; this design does not claim both families already operate as
standalone production login modes. The current delivery scope is unified API
login, Profile properties and simple rules. Do not expand it into a new direct
AD implementation or change completed migration authority implicitly.

| Contract | Purpose | Relationship |
| --- | --- | --- |
| Provider Proof 1.0 | Validate credentials and return an assured stable provider identity | Reuse the existing versioned contract and hardened HTTP consumer |
| Provider Profile 1.0 | Obtain approved extra properties for that established identity without resending the password | Separate optional call to a deployment-configured endpoint |
| Provider Metadata 1.0 | Recovery email trust evidence | Keep its existing purpose and independent activation |
| Lifecycle and Password Sync | Status and credential-write capabilities | Keep their independent authority and activation boundaries |

Proof login must work without enabling directory lookup, migration or password
sync. It must preserve local account/Person eligibility, lockout, IdP MFA, sessions
and the authority of any completed credential-migration record. Required proof
actions cannot be discarded or treated as completed MFA.

Retire the old `authenticated` wire format and its LegacyAuth parser/configuration
surface rather than retaining a selectable compatibility mode. Update callers,
fixtures, samples and deployment guidance together. Historical provider/key link
records remain identity data and must not be deleted simply because the old wire
reader is removed.

Do not detect formats from response shape, retry with a different credential
authority or probe another endpoint with the submitted password. Any retirement
of old code/configuration must be explicit in deployment documentation and tests.

Route an existing upstream-linked shadow account back to its selected provider;
finding its local record must not imply that it owns a local password. Preserve
explicit local credential authority for actual local-password accounts.

## Profile exchange and storage

The profile request carries a contract version and the exact
`providerNamespace + stableSubject` established by Proof or a verified durable
link. It uses authenticated server-to-server HTTP and carries no user password.
An illustrative success body is:

```json
{
  "contractVersion": "1.0",
  "providerNamespace": "example.provider",
  "stableSubject": "opaque-subject",
  "extraProperties": {
    "is_student": true,
    "student_id": "4-example",
    "is_graduate_student": true
  }
}
```

The academic names above are deployment examples, not required contract fields.
A provider may calculate the final classification, or supply approved inputs for
a locally configured rule. A source-reported property does not itself establish
role, lifecycle, recovery or credential authority.

Implementation constraints:

- Validate the version and exact response identity tuple before storing data.
  Keep the existing fixed-endpoint, no-redirect, bounded-response and timeout
  patterns; endpoint trust and secrets remain deployment-controlled.
- Import only configured keys and expected types. Start with scalar strings and
  booleans sufficient for this request, with bounded key count/value/body sizes.
- Add nullable Person JSON storage through both supported migration assemblies.
  Existing rows start without extra properties; do not infer their values.
- Retain the source tuple and fetch state with each accepted property set. One
  Person can have multiple login accounts; the last login must not overwrite
  another source's properties. Claim definitions select their source explicitly.
- Before projecting a stored set into newly issued claims, verify that its exact
  provider tuple remains durably linked to an account currently associated with
  that Person. Unlinking or reassociating the source account makes the old Person's
  snapshot ineligible. Historical JSON may remain as last-known data but cannot
  preserve disclosure authority. A new Person receives accepted properties only
  through a refresh for its verified current link, not by copying the old snapshot.
- Treat a successful property set as a replacement for that selected source,
  including removal of keys no longer supplied. Initially no source data is
  accepted. A valid refresh marks that source accepted; a failed refresh marks
  it unavailable while preserving the prior set only as last-known data.
  Mapping requires an enabled, configured source and its latest refresh to be
  accepted. Disabled, failed or never-fetched sources cannot supply claims.
  Fetch time records receipt only and must not be represented as a source
  observation or academic-status verification time.
- Confirm source data after an upstream login and before applicable issuance or
  token refresh when its configured confirmation age has expired. Perform this
  once per selected source before enrichment, not from individual claim rules.
  Use the confirmation policy below; previously issued tokens keep
  their normal lifetime.
- Keep optional display-profile failure separate from credential-proof failure.
  Claims that require unavailable source data are omitted; profile failure must
  not create an authentication fallback or mutate local eligibility.
- Keep imported source data out of ordinary self-service profile writes. Explicit
  claim mappings, not the entire JSON object, determine token disclosure.

## Property changes, local hashing and confirmation

Source identity/classification changes require the extra-property snapshot and
newly issued derived claims to change. A local hash comparison alone cannot
detect an upstream change; the consumer must contact its selected source.

Keep the first exchange simple: the source reads its selected current data and
returns the full property set. It need not persist a revision, track an updated
flag, monitor source changes or implement a `NotModified` outcome. There is no
`profileVersion`, conditional-request protocol or shared JSON digest algorithm
in this first Profile contract.

After validating and filtering a full response, HybridIdP may compute a local
SHA-256 content hash over its deterministic typed representation: keys in ordinal
order, exact string values and Boolean values, including key removals. The hash
is an unchanged-write optimization only. Matching hashes do not avoid querying
the source and do not validate trust, identity association or source freshness.

Track these distinct local values with the snapshot:

| Value | Meaning |
| --- | --- |
| `contentHash` | Optional deterministic local digest of accepted typed properties, used to avoid unchanged value writes |
| `confirmedAtUtc` | Consumer time of the latest validated full response |
| Source tuple, association and acceptance state | Whether the snapshot may be used for this Person and configured source |

Source-confirmation time is not a source observation, proof of current enrollment
or cryptographic identity proof. Successful unchanged confirmation still updates
confirmation metadata; it may skip property replacement but not association,
eligibility, scope or rule checks. Changed rule definitions are reevaluated even
when the source property hash is unchanged.

Always confirm after upstream login. Before issuance or token refresh that needs
source properties, reconfirm if the maximum confirmation age is exceeded.
Proposed deployment default: five minutes; zero means confirm on every applicable
issuance. This default is a design proposal, not a user-approved freshness SLA.
The configured age must be finite and nonnegative. Expired data cannot supply
new claims unless reconfirmation succeeds. Failure leaves source-dependent
claims unknown/omitted while optional profile failure remains separate from
credential authentication.

Changes to endpoint trust, accepted keys/types or source identity invalidate the
cached acceptance configuration and require a new fetch. Unlink/reassociation
still invalidates disclosure authority even when hashes match. No source writes,
push notifications, background polling or new token-validation endpoint are
introduced by this first design. Existing JWTs do not change in place; the chosen
confirmation interval plus token lifetime remains part of deployment policy.

## Claim mapping and first conditional rules

Extend the existing claim-definition and scope-mapping flow with two sources:

1. Direct property mapping, including an approved provider property.
2. A Boolean condition over approved profile/provider properties.

Initial operations: `Equals`, `StartsWith`, `All` (AND) and `Any` (OR). Conditions
must be bounded and typed. `StartsWith` is a literal, ordinal string operation;
`Equals` preserves the configured value type. Do not coerce `"true"` into a
Boolean or evaluate JavaScript, C#, regexes, SQL or API calls.

An illustrative proposed rule, not an accepted current configuration format:

```json
{
  "claimType": "is_graduate_student",
  "dataType": "Boolean",
  "source": "ConfiguredProfileSource",
  "condition": {
    "all": [
      { "property": "is_student", "operator": "Equals", "value": true },
      { "property": "student_id", "operator": "StartsWith", "value": "4" }
    ]
  }
}
```

The local deployment must confirm that this classification rule is correct.
`User` is a standard IdP role, not a student signal. No Student/Graduate role is
introduced by this design.

Missing, rejected or wrong-type inputs produce an unknown result and omit the
claim. Known inputs yield a Boolean true/false result. Initially require all
referenced inputs to be available before evaluating a condition, including
`All`/`Any`; this avoids silently converting missing source data into a definite
classification. A malformed rule is rejected when saved, not interpreted as false.

Rules are controlled by the same trusted claim-administration boundary and
property allowlists as direct mapping. They cannot issue protected claims such
as `sub`, `role`, `app_role`, `permission`, `amr` or `auth_time`. Dynamic claims
continue to require their granted scope. Boolean serialization must agree
across ID tokens, access tokens and UserInfo. Token issuance/refresh evaluates
the applicable mapping; UserInfo reflects the issued token's claims.

## Implementation order and compatibility

1. Unify the Proof login route independently of directory integration, including
   explicit handling of existing upstream shadow accounts and required actions.
2. Define Profile 1.0 and implement gated retrieval, approved-source Person JSON
   storage and migrations for SQL Server/PostgreSQL.
3. Extend claim definitions, DTO validation, enrichment and the existing Admin UI
   for property/rule sources. Update both English and Traditional Chinese text.
4. Update sample flows, deployment settings, contracts and focused tests. Exercise
   real upstream Proof login followed by Web TestClient callback, UserInfo,
   refresh/logout and requested/unrequested derived-claim scopes.

Retiring the legacy upstream wire format is an intentional integration change.
Inventory existing upstream adapters and durable links before rollout. Existing
Legacy provider/key pairs cannot be renamed or mapped to Proof subjects using a
mutable login name or email; establish verified equivalence or use the protected
account-linking process. Repeated login must not create duplicate accounts.

Downstream clients continue to use OIDC/OAuth. New custom claims are exposed only
through their approved mapping/scopes. This statement is not a blanket guarantee
that existing client policy or upstream account linkage needs no upgrade work.

Required verification remains proportional: focused parser/routing/link tests,
property tuple/type/replacement tests, rule true/false/unknown tests, scope and
Boolean serialization checks, changed/unchanged properties and expiry/failure
checks, and the real producer-to-Web-TestClient journey.
No new validation framework or production operation is authorized by this design.

## Delivery and verification on 2026-10-08

- Defined Profile 1.0, typed DTOs and response JSON Schema before the consumer.
  Added explicit source configuration, approved-property storage, local canonical
  hashes, freshness checks, SQL Server/PostgreSQL migrations and bounded rules.
- Updated the existing claim form and both locales. Retired the flat login
  reader while preserving local credential authority and exact provider links.
- The latest backend unit run passed 3,074 cases. Focused integration checks,
  the frontend suite/component checks and Vite build also passed.
- A headed browser completed Proof login, consent, callback, UserInfo and rotated
  refresh with directory integration disabled. Changing only the configured rule
  changed the derived Boolean from true to false at refresh. This used a generic
  local fixture and a fresh isolated SQL Server LocalDB, not AuthProxy or production.
- Read-only implementation review and focused blocker rechecks passed. Reviewer
  model, role and effective effort could not be verified through the trace tool;
  the reviewer did not independently rerun the tests.

AuthProxy's Profile producer and the real AuthProxy-to-IdP-to-Web-TestClient
journey remain separate deployment work. The initial five-minute confirmation
age is configurable and is not an approved freshness SLA. PostgreSQL migration
generation does not establish connected PostgreSQL or production acceptance.

## References

- [Authentication integration](../AUTHENTICATION_INTEGRATION.md)
- [Provider Proof 1.0](../PROVIDER_PROOF_CONTRACT.md)
- [Provider Metadata 1.0](../PROVIDER_METADATA_CONTRACT.md)
- [Security expectations](../SECURITY.md)
- [Database migration guidance](../DATABASE_CONFIGURATION.md)
- [Release readiness](../RELEASE_READINESS.md)
