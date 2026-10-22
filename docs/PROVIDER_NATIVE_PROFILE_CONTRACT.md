# Provider Native Profile contracts (1.0 and 2.0)

The original sections below describe the 1.0 compatibility mode.
For independently selected neutral queries, see [version 2.0](#neutral-query-version-20)
and the [common contract](NEUTRAL_QUERY_CONTRACT.md). Neither format is inferred
from a failed request; all original authority limits continue to apply.

## Neutral query version 2.0

The [common neutral-query contract](NEUTRAL_QUERY_CONTRACT.md) is normative for
selection, typed candidates, queryRule, approved mappings, comparison, unique
matching, transport, response correlation and mutation invalidation. Legacy
formats remain unchanged under explicit compatibility selection.

Neutral-v2 uses `native-profile/2.0`. Its availableCandidates are typed
`employeeId` (String), `nationalIdHash` (Sha256Hex), and `accountName` (String).
All seven nonempty sets in the common contract are eligible for explicit source
agreement; every supplied property must match the same unique native-profile
record. The common registry defines approved value-source mappings and exact
normalization/comparison. Neither a Proof login nor an existing Proof tuple is
required in this mode. The selected independently deployed native data source
must be enabled, trusted and unpaused for that exact namespace; a query selection
does not enable Proof or invent a native write owner.

Found returns `result: { profile, emailTrustOrigin }` with the same seven assured
fields, lengths and Email assurance semantics as 1.0. NotFound/Ambiguous return
no result; missing local inputs, source failure or invalid correlation block
preview/apply. All User/Person native values remain fill-only and independently
owned; populated/manual-fixed blanks, shared-Person restrictions, output mapping,
MFA-protected Email and independent Email trust stay authoritative.

Preview/apply binds the local account/Person, selected source, exact query/value
sources/approvals, configuration revision and full fresh diff. Apply refetches
using the same approved query and rejects any drift with `preview_stale`, including
query/configuration changes that happen to produce the same returned fields.
No query match creates a link, Person, login, security-stamp change or entitlement.

Wire definitions: [request](contracts/provider-native-profile/v2.0/contract.schema.json#/$defs/request)
and [response](contracts/provider-native-profile/v2.0/contract.schema.json#/$defs/response).
[Synthetic examples](contracts/provider-native-profile/v2.0/examples/) cover Found, NotFound,
Ambiguous, Unavailable, Unsupported, missing inputs and forbidden mixed shapes.
Their validity proves structure only; semantic, authority and live product checks
remain separate. queryRule is omitted in these unambiguous agreed-rule examples.

## Version 1.0 compatibility contract


This independently enabled capability reads a fresh native profile for one
existing exact provider identity. It supplies no credential proof, authentication,
eligibility, lifecycle, linking, role, recovery or password authority. It is
separate from Provider Proof login and the Provider Profile extra-property
snapshots used by claims. Implementers select their own source storage; HybridIdP
has no producer-specific package, database or identity-mapping dependency.

## Request and response

The consumer POSTs JSON to the complete configured endpoint, without appending
a path. A producer may expose `/api/profile/native`. Service authentication uses
`X-Internal-Secret`; provision secrets privately.

The request has exactly these fields:

```json
{
  "contractVersion": "1.0",
  "providerNamespace": "example.provider",
  "stableSubject": "source-account"
}
```

A successful HTTP 200 `application/json` response has exactly these root fields:

```json
{
  "contractVersion": "1.0",
  "providerNamespace": "example.provider",
  "stableSubject": "source-account",
  "profile": {
    "displayName": "Example Person",
    "givenName": "Example",
    "surname": "Person",
    "email": "person@example.test",
    "department": "Example department",
    "title": "Example title",
    "employeeId": "example-employee",
    "assuredFields": ["DisplayName", "GivenName", "Surname", "Email", "Department", "Title", "EmployeeId"]
  },
  "emailTrustOrigin": "Unknown"
}
```

The profile string members are optional and may be null or blank. Each nonblank
value requires its corresponding `assuredFields` member. `assuredFields` itself
is required, contains only the seven exact PascalCase names above and has no
duplicates. It provides field assurance, not local write permission.
`emailTrustOrigin` is exactly `Unknown`, `SourceVerified` or `PolicyTrusted`.
It describes the producer's email evidence; local confirmation additionally
requires independent exact namespace trust.

Version, names, enum values and the response tuple are case-sensitive.
Unknown or duplicate properties, numeric enum representations, malformed UTF-8,
control characters, invalid field types and unassured nonblank values reject
the entire response. Email must be a valid address without a display-name wrapper.
No value is truncated. Maximum lengths are UTF-16 code units:

| Field | Maximum |
| --- | --- |
| `providerNamespace` | 200 |
| `stableSubject` | 256 |
| `displayName` | 256 |
| `givenName`, `surname` | 100 each |
| `email` | 256 |
| `department`, `title` | 200 each |
| `employeeId` | 50 |

The response limit is 64 KiB and JSON depth is at most 8. HTTP 404 means an
unknown exact subject, HTTP 400 an invalid request, and HTTP 503 an unavailable
source. Other statuses, redirects and non-JSON successful bodies are rejected.
Responses should use `Cache-Control: no-store`. No login credentials, canonical
username, arbitrary extra properties or authentication result belong in this
exchange. The provider performs an exact lookup; username/email matching and
cross-source subject inference are not part of the contract.

## Consumer configuration

`ProviderNativeProfile` is independent of every other upstream capability:

| Option | Default / constraint |
| --- | --- |
| `Enabled` | `false` |
| `TrustedProviderNamespace` | Unset; exact established namespace |
| `Endpoint` | Unset; full absolute URL without user info, query or fragment |
| `SharedSecret` | Unset; private service authentication value |
| `Timeout` | `00:00:05`; positive and at most 30 seconds, covering send and body |
| `AllowPrivateNetworkHttp` | `false`; explicit deployment exception, with network placement enforced by the operator |
| `TrustedEmailProviderNamespaces` | Empty; exact ordinal namespace allowlist |

HTTPS uses platform certificate verification. Redirects and cookies are disabled;
there are no automatic HTTP retries or cached source responses. Enabling this
endpoint does not authorize native writes: the existing Proof native source must
also be enabled, trusted, unpaused and configured for the exact same namespace.
Invalid or incomplete settings report unavailable status and perform no write.
No producer, namespace or endpoint is enabled by the checked-in defaults.

## Manual synchronization

`/api/admin/profile-sync` requires both `users.update` and `persons.update`,
administrative authority and cookie CSRF protection. Responses are `no-store`.

- `GET status` returns `{ enabled, reason }`. It reports configuration/pause
  availability, not a connectivity probe.
- `POST users/{id}/preview` loads a detached User and linked Person and reads
  the fresh source. It never saves profile, audit, snapshots, tokens or sessions.
- `POST users/{id}/apply` accepts only `{ previewToken }`. The token expires
  after five minutes and is protected with the existing Data Protection keys.

The preview is `{ userId, userName, personId, providerNamespace, status, reason,
changes, previewToken }`. Status is `ready`, `no_changes` or `blocked`; only a
ready plan has a token. Each change contains `target`, `field`, `currentValue`,
`sourceValue`, `proposedValue` and `action`. The six native fields, contact Email
and User `EmailConfirmed` are shown with `fill`, `confirm`, `preserve`,
`manual_fixed`, `owner_mismatch`, `missing_source`, `mfa_protected`,
`shared_person`, `value_too_long`, `inconsistent_email`, `email_untrusted` or
`email_mismatch` or `mapping_disabled`. Without saved mapping configuration,
names map as Proof does: displayName to Nickname, givenName to
FirstName, surname to LastName, department to Department, title to JobTitle,
employeeId to EmployeeId.

Saved native mappings use the same six assured structured properties for both
normal login and manual sync. They are bound to the exact source and namespace;
an omitted mapping disables that target. Neither arbitrary Profile extras nor
Email trust are configurable mapping properties. The FirstName fallback reads
the original assured givenName/displayName, independent of Nickname remapping.
Remapped values exceeding a target's native length are skipped without truncation.

Binding must be exactly one existing `AspNetUserLogins` provider/key for the
configured namespace. There is no username/email match, new link or new Person.
The six native fields use the existing source policy and owner rules but are
always fill-only on User and Person independently, even with overwrite enabled.
Every populated value and manually fixed blank is preserved. A shared Person
requires a compatible explicit field owner. A displayName longer than 100 is
skipped for Person Nickname. The existing exact-source FirstName displayName
fallback remains default-off and uses only this fresh assured response.

Valid assured Email fills missing User contact Email with Identity's email
normalizer. Existing nonempty addresses are preserved. Email MFA enrollment
protects both the account address and confirmation state, including a missing
address. An inconsistent blank already-confirmed account address is preserved
for operator correction. Person Email fills only when missing and this is its
sole linked account. New account confirmation requires fresh `SourceVerified`
or `PolicyTrusted`, exact local email-namespace trust, and the persisted address
equal to that fresh source address. Unknown evidence can fill contact Email
without confirming it. This does not enroll MFA or change recovery addresses.

Apply fetches a fresh source before opening a bounded serializable database
transaction. It rechecks actor, account, tuple, Person link and linked-account
membership, native policy revision/owners/protection, MFA and email state,
configuration (including mapping revision even for an otherwise identical draft)
and the entire reviewed source/diff. Any drift rejects with
`409 preview_stale`; it never applies an unseen changed value. Identity and
native-state concurrency tokens protect the save; failed writes are discarded
and never automatically retried. User/Person changes and the administrative audit
commit together. The audit contains field names, local IDs and counts only.
Apply advances the existing User concurrency stamp without changing its security
stamp. Duplicate apply requires a fresh preview.

Blocked reasons include `disabled`, `configuration_invalid`, `source_disabled`,
`source_paused`, `source_namespace_mismatch`, `binding_missing`,
`binding_ambiguous` and `person_missing`. Invalid/expired/actor-mismatched tokens
return `409 preview_invalid`; invalid stored native authority returns 409.
Source failure returns `503 source_unavailable` or `source_invalid_response`;
unknown source/account returns `404 source_not_found` / `user_not_found`.
A successful apply returns `{ status: "applied", changedUserFields,
changedPersonFields }`. Batches are sequential per-account UI operations, not
a background synchronization job.

No schema migration is added. Existing native-field authority migrations must
already be present. Normal login synchronization, Claims Preview, Profile
snapshots, usernames, roles, lifecycle, identity documents, credentials and
session/token behavior retain their existing flows.
