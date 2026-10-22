# Neutral non-login query contract 2.0

Status: contract-first definition for consumer and independent producer implementation.
This document defines the new wire format; it does not claim deployed support.
It applies to Native Profile, Profile properties, Metadata, Lifecycle, Recovery
Identity Verification (RIV) and Legacy Password Sync. Proof/login remains 1.0.

## Identity and version selection

Authentication identity remains the exact authentication namespace plus opaque
subject in the existing durable login binding. Local UserId, PersonId, links,
credentials and authentication authority never change because of a query match.
Version 2.0 selects an independent data source and queries its approved attributes;
it requires neither previous Proof/SSO login nor an SSO UUID mapping database.
An account authenticated by Legacy API, AD, Google or local credentials can use
the same query when its required trusted values are available. This adds no AD
authentication, IID reader, SCIM service or discovery protocol.

Each capability selects an explicit local wire mode: `legacy` or `neutral-v2`.
Legacy means the existing 1.0 format, or the existing unversioned Password Sync
format, with its original tuple/mapping prerequisites and parser tolerances.
Their schemas, fixtures and serializers remain unchanged. The sections describing
1.0/unversioned behavior in the six capability documents describe that mode.
Their new 2.0 sections and this document describe neutral-v2.

Neutral-v2 requires exactly `contractVersion: "2.0"`, the capability's
`contractType`, and `X-Provider-Contract: <contractType>/2.0`. Header and body must
agree. There is no discovery, auto-upgrade, downgrade, alternate-source retry or
retry with a legacy body. A shared endpoint authenticates and classifies one
complete shape before source work. Unknown versions, duplicate/conflicting
selectors, mixed v1/v2 fields, or more than one candidate are rejected with 400.
In particular v2 forbids `stableSubject`, `binding` and `accountIdentity`.
Legacy traffic carries no new v2 fields/header; legacy Lifecycle keeps its
original `lifecycle-status/1.0` header. Distinct configured URLs are also allowed.

## Typed availableCandidates and value sources

`availableCandidates` is a value-free server-owned registry for the existing
protected administration source/mapping UI, not a producer discovery endpoint.
The registry has the following complete candidate vocabulary for all six APIs:

| Attribute | dataType / wire type | Candidate and reserved value-source vocabulary | Comparison |
| --- | --- | --- | --- |
| `employeeId` | `String` / JSON string, 1-50 UTF-16 units | `person.employeeId` = Person.EmployeeId; `user.employeeId` = ApplicationUser.EmployeeId; `runtime.proof.employeeId`; `runtime.directory.employeeId` | Trim outer whitespace, then ordinal case-sensitive equality; preserve zeros, interior spaces, punctuation and case. |
| `nationalIdHash` | `Sha256Hex` / JSON string, exactly 64 uppercase ASCII hex digits | `person.nationalId` = Person.NationalId, after provenance/storage-algorithm approval | Canonical uppercase hex; compare decoded SHA256 bytes (equivalently ordinal uppercase hex). Never hash the stored hash again. |
| `accountName` | `String` / JSON string, 1-256 UTF-16 units | `user.userName` = ApplicationUser.UserName; `runtime.proof.canonicalAccount`; `runtime.directory.canonicalAccount` | Trim outer whitespace, then ordinal case-sensitive equality; no domain stripping, email interpretation, prefix inference or case folding. |

Current HybridIdP implementation exposes only `person.employeeId`,
`user.employeeId`, `person.nationalId` and `user.userName` as selectable value
sources. The runtime entries remain internal/reserved vocabulary until real
product callers supply the required validated evidence; current callers do not
supply those objects. See the [supported subset and configuration guide](DEPLOYMENT_GUIDE.md#neutral-non-login-query-configuration).

Runtime Proof values must be from a freshly validated, correctly scoped Proof
result (`EmployeeId` must be assured). Runtime directory values must be from
the existing protected exact-account directory result (`Profile.EmployeeId`
must be assured). These are optional value sources, not login prerequisites.
Their absence never triggers a new login, directory scan or fallback. A source
approval must explicitly permit cross-source use; an assured value from another
authority is not automatically equivalent in the queried source's key space.

Persisted fields can be edited and are not inherently trusted because present.
An authorized administrator must approve their source semantics, current value
and account/Person association for the selected data source, or existing trusted
field provenance must establish that equivalence. For `user.userName`, explicit
approval is essential: local renaming and Email naming mean it need not be the
original source account. Changing an approved value/association invalidates that
approval; a mapping checkbox alone cannot trust arbitrary future self-edits.
Stored Person/User employee IDs remain independent; never silently substitute
one for the other. A shared Person does not select one of its linked accounts.

[PidHasher](../Core.Application/Utilities/PidHasher.cs) computes
`SHA256(UTF8(raw.Trim().ToUpperInvariant()))` and emits uppercase 64-hex.
`Verify` compares hex without case sensitivity; `IsHashed` checks shape only.
Person.NationalId stores that document hash, not raw IID. A hash-shaped historical
row alone proves neither origin, algorithm nor identity verification. Approve
the algorithm/provenance before selecting it. The producer can compare an
approved stored digest or compute the same digest over its trusted raw source
document internally. Never reverse a hash, infer raw IID, assume another hash
scheme, or copy it into submitted RIV evidence. No raw IID candidate exists in
2.0. Passport/resident-certificate fields, Email, names, birthdate, claims and
browser-posted lookup values are outside this registry. AD exposes assured
employeeID and sAMAccountName in the existing transport, not IID.

## Accepted attribute sets and resolution

Every capability uses the same vocabulary and the following possible nonempty
sets: `[employeeId]`, `[nationalIdHash]`, `[accountName]`,
`[employeeId, nationalIdHash]`, `[employeeId, accountName]`,
`[nationalIdHash, accountName]`, `[employeeId, nationalIdHash, accountName]`.
A deployment must agree which of those exact sets its source supports and expose
only that subset as `acceptedAttributeSets`. Schema-valid is not source-approved.
The selected set must be supported by both consumer configuration and producer;
there is no universal requirement to implement every possible set.

All supplied attributes are AND predicates on the same eligible source record.
Normalize both source and request as specified above before ordinal comparison;
the consumer emits normalized values. Reject empty-after-trim, control characters,
invalid Unicode, unknown attributes, nulls, arrays, duplicate keys, numeric IDs,
oversize values and unsupported sets. No fuzzy/substring matching, database
default-collation dependence, attribute dropping or weaker retry is allowed.
The producer maps these properties to its own storage internally using safe
parameterized access; neither SQL, column names nor routing expressions are wire
inputs. Zero matches is NotFound; more than one distinct eligible source target
is Ambiguous even when fields happen to agree. Never choose the first match or
auto-create/link an account. Account-specific Lifecycle, RIV and Password Sync
must resolve exactly one source account authorized for that capability; a person
match alone cannot select among several accounts or password destinations.

`queryRule` is OPTIONAL. Omit it when the selected namespace/capability/attribute
set selects one agreed rule set. If those selectors would otherwise select
multiple agreed rule sets, configure an exact opaque `queryRule` on both sides
to choose one (1-128 ASCII letters, digits, dot, underscore or hyphen).
Missing required disambiguation, unknown keys and unsupported sets are
Unsupported without source work. Do not invent a mandatory key or use keys for
table names, authorization, source probing or authentication identity.

## Request, response and transport

For example, a Profile request with no queryRule is:

```json
{
  "contractType": "profile",
  "contractVersion": "2.0",
  "requestId": "11111111-1111-4111-8111-111111111111",
  "providerNamespace": "example.provider",
  "query": { "attributes": { "employeeId": "SYNTH-0001" } }
}
```

Here `providerNamespace` selects the configured data authority/key space, not the
account's authentication binding. It is an exact nonblank ordinal string of
1-200 UTF-16 units; never normalize it. `requestId` is a nonzero lowercase UUID
in D form, newly generated per call; it provides correlation, not authorization.
RIV additionally sends its original `scheme` and independent submitted `evidence`.
Password Sync additionally sends `operationId` and `password`.

An admitted Profile response is:

```json
{
  "contractType": "profile",
  "contractVersion": "2.0",
  "requestId": "11111111-1111-4111-8111-111111111111",
  "providerNamespace": "example.provider",
  "queryOutcome": "Found",
  "result": { "extraProperties": { "example_flag": true } }
}
```

Every admitted response echoes contractType, version, requestId and selected data
namespace exactly. Password Sync also echoes operationId. It never echoes query
values, submitted evidence, password or an authentication binding. `result` is
required only for Found and forbidden otherwise, including null. Result fields
are capability-specific and defined in the six schemas. A Found lookup does not
imply RIV Verified, Password Sync Success, trusted Email or Lifecycle Enabled.

| HTTP | queryOutcome | Meaning |
| --- | --- | --- |
| 200 | Found | Exactly one eligible target resolved; evaluate the capability result and independent local authority. |
| 200 | NotFound | Authoritative supported lookup completed with zero matches; no data or write. |
| 200 | Ambiguous | Multiple matches; no data or write. For RIV use 503 instead to preserve unavailable ambiguity semantics. |
| 503 | Unavailable | Missing source/data/authority, uncertain mapping, outage or source deadline; no accepted evidence. |
| 400 | Unsupported | Valid v2 envelope but unsupported namespace, rule key or attribute set; no source work. |

Malformed/unsupported-version/mixed-shape admission uses 400 with no accepted
contract result; 401/403 authentication/admission, 413 size, 415 media and 429 rate
failures likewise carry no accepted contract evidence. Never parse framework
errors as result. A valid request's response must have the exact status/outcome
pair above. Timeout, transport/body/shape/correlation failure or other HTTP status
is a local failure. Missing required local attributes is locally `MissingInput`:
make no call, invalidate applicable evidence, and block protected operations.
Anonymous recovery presents uniform failure regardless of these internal causes.

Each capability retains its complete fixed independently configured endpoint,
secret (`X-Internal-Secret`), HTTPS/certificate validation, admission, deadline,
redirect/cookie/retry policy and response-byte ceiling from its existing contract.
Neutral-v2 bodies are strict UTF-8 JSON objects without BOM, duplicate/unknown
members, comments, trailing commas, invalid Unicode or numeric enums. Use
`application/json`, optionally `charset=utf-8`, and `Cache-Control: no-store`.
The v2 request ceiling is 8 KiB (including Password Sync); Lifecycle's v2 request
ceiling increases from its legacy 4 KiB to 8 KiB. Response ceilings remain Native
64 KiB, Profile 64 KiB, Metadata 16 KiB, Lifecycle 8 KiB, RIV 4 KiB, Password Sync
64 KiB. Maximum depth is 8. Runtime UTF-16 limits apply where schemas count Unicode
code points. Schema cannot prove duplicates, matching, provenance or authorization.
No inputs, hashes, bodies, secrets or source identifiers belong in logs, URLs,
tokens or general browser state. Protected administration exposes only necessary
configuration labels; sanitized correlation/outcomes may be audited.

## Configuration, correlation and invalidation

**Settings -> External data sources -> Manage query fields** configures lookup inputs
for these six non-login services. **Settings -> Profile sync output fields -> Manage field
mappings** separately configures supported returned native properties and their
User/Person targets. Input approval establishes lookup provenance, not profile
write ownership, Email trust or authentication authority. Source availability
in protected settings describes deployment/trust/pause/configuration eligibility;
it is not a connection-health probe. Unavailable saved selections stay visible
and blocked, with safe explanations rather than secrets or endpoint details.

The protected UI selects only enabled/trusted deployed capability sources and
their supported `availableCandidates`, `acceptedAttributeSets`, and value-source
mappings. It cannot add endpoints, secrets, trust ceilings, arbitrary expressions,
SQL or undeployed sources. `.env`/protected deployment configuration retains those
ceilings and each capability's independent feature/authority/cohort gates.
The registry contains attribute, dataType and available value-source identifiers,
never actual account values. It is scoped per deployed capability/source; hide
unsupported candidates, and show an existing unavailable saved selection as
blocked rather than silently replacing it. Selecting v2 Native Profile needs an
independent enabled/trusted/unpaused native data-source policy for its namespace;
it does not require enabling or logging in through Provider Proof.

Example local mapping (configuration, not an endpoint request):

```json
{
  "capability": "profile",
  "sourceId": "example-source",
  "wireMode": "neutral-v2",
  "providerNamespace": "example.provider",
  "mappings": [ { "attribute": "employeeId", "valueSource": "person.employeeId" } ]
}
```

Before dispatch, bind requestId to an immutable local context: local account,
Person association, exact deployed source/namespace/wire mode, normalized typed
attribute set/values, optional queryRule, value-source mappings/approvals and their
revision, deployment policy revision and relevant account/security versions.
Accept a response only against that same pending context; never correlate merely
by a Person or by the most recent request to a source. Do not persist sensitive
query material solely for diagnostics. Existing protected receipts may retain
only necessary protected/fingerprinted state under their existing retention rules.

Changing any query value, mapping/value source/approval, source namespace, rule
key, endpoint/trust policy, wire mode, configuration revision (even a re-saved
identical draft), account/Person link or applicable security version invalidates
old source confirmation/cache entries, Native preview receipts and RIV grants.
Invalidate before reuse/projection and recheck immediately before commit/action;
never apply an old response under a new mapping. Complete Profile snapshots stay
source/account scoped; failed confirmation removes eligibility for new claims.
Metadata failure invalidates Email evidence without changing independently verified
recovery addresses. Lifecycle retains request-only reuse and all authority,
finite freshness/interval and local-denial checks. No failure enables fallback.

Password Sync reserves the selected query/configuration and independently
authorized target scope with the durable operation before dispatch. Recheck
mapping/account/security concurrency before the single send. Any mutation blocks
an unsent attempt; after a send might have happened, preserve its original
reservation and Unknown barrier rather than retargeting or resending. The
producer durably binds operationId to the original query/rule and resolved internal
target/write scope before writing. An exact duplicate returns the stored aggregate
receipt without executing again; conflicting reuse is rejected without a new
write, never replaced by a new target. Do not persist plaintext passwords.

## Direct directory Native Profile adapter

The optional AD/LDAP adapter applies only to administrator manual Native Profile
sync. It uses the existing internal `neutral-v2` selection and approved persisted
values, without an HTTP endpoint, header, shared key or legacy body. HTTP transport
rules above continue to apply to HTTP producers. The protected source catalog
identifies this source with `sourceType: "directory"`; existing sources default
to `"http"`.

`DirectoryNativeProfile` is deployment-controlled and disabled/untrusted by default.
Its explicit `SamAccountNameQueryAttribute` selects `accountName` with `user.userName`,
or `employeeId` with `person.employeeId` and/or `user.employeeId`. Either choice
uses the selected approved value as exact sAMAccountName, without alias or candidate
substitution. The employeeId-input form requires explicit deployment agreement;
returned employeeId still comes only from AD employeeID. No national-ID or runtime
candidate is added. See [configuration and safe setup](DEPLOYMENT_GUIDE.md#direct-ad-source-for-manual-profile-synchronization).

Saving query settings or mappings performs no AD read or next-login scheduling.
Only explicit manual preview and apply perform fresh read-only lookups. Data lookup
needs no previous login binding and grants no login, linking, credential-write or
Email verification authority. Existing Stage 1 binding/login and HTTP legacy/v2
consumers retain their boundaries. Directory policy, connection/scope, approvals
and resolved object identity participate in preview freshness; stale or failed
apply requires a fresh preview without fallback.

## Consumer and producer handoff

Implement common typed query resolution/correlation once, then retain each
capability's result parser, evidence policy and write boundary. No authentication
binding is minted from a result. Native fills preserve populated/manual-fixed
values and independent field owners. Profile imports only approved property types;
Metadata retains independent Email trust/current-period recovery policy. Lifecycle
must prove account-specific authority and real source observation freshness;
a producer without independent authority continues returning Unavailable.
RIV first resolves the configured target, then independently verifies submitted
evidence using the unchanged identity-identifier comparison; lookup never verifies
identity. Verified still needs the consumer's durable restricted grant, OTP,
destination/eligibility checks and authorized reset ceremony. Password Sync still
requires a completed authorized source operation, explicit destination approval,
cohort gates, single-send reservation and durable Unknown settlement rules.

Independent producers implement the
v2 selectors and properties against their own data stores. They must document
supported sets, exact normalization, uniqueness, account/destination authority
and queryRule only where needed. No consumer reads producer databases, installs
producer assemblies or assumes an SSO UUID join. Keep both old and v2 shapes
separately selectable. The schemas and synthetic examples below are structural
agreement artifacts, not evidence of live authority, production operation or
completed joint E2E.

The [optional AuthProxy integration recipe](integrations/authproxy.md) describes
one producer's adapter setup and Password Sync receipt prerequisite. It adds no
mandatory producer, package, database or deployment topology to this core contract.

Common definitions: [schema](contracts/neutral-query/v2.0/common.schema.json).
The six capability documents link their request/response definitions and examples.
