# Provider Profile Contract 1.0

This contract supplies display/profile properties for an already established
provider identity. HybridIdP defines the contract; independent implementers
choose their data sources and topology. It grants no role, lifecycle, password
or recovery authority. Provider Metadata 1.0 retains its separate email-evidence
purpose. Provider Proof 1.0 establishes the identity before Profile is called.

## Transport and request

POST JSON to the independently configured full Profile endpoint. Authenticate
the caller with `X-Internal-Secret`. Use HTTPS; a deployment may explicitly allow
HTTP on its protected private network. No redirects are followed. Proof and
Profile may have different hosts, paths, secrets and implementers. The password
is never forwarded to Profile. Each request has all three required fields:

```json
{
  "contractVersion": "1.0",
  "providerNamespace": "example.provider",
  "stableSubject": "opaque-subject"
}
```

Namespace and subject are opaque, nonempty strings, at most 200 and 256
characters respectively. Compare them exactly, without trimming or case folding.
The tuple must come from successful Proof or an existing verified durable link,
never from a caller's email or guessed account name.

## Complete success response

Return HTTP 200 with `Content-Type: application/json` and all four fields:

```json
{
  "contractVersion": "1.0",
  "providerNamespace": "example.provider",
  "stableSubject": "opaque-subject",
  "extraProperties": {
    "is_student": true,
    "student_id": "4-example",
    "categories": ["student", "graduate"],
    "affiliations": []
  }
}
```

Version and tuple must exactly match the request. `extraProperties` is the complete
current set, including an empty object when no properties remain. Missing keys
remove their old values. Property names contain 1-64 ASCII letters, digits,
underscore, hyphen or dot. At most 32 properties are permitted. Values are JSON
booleans, strings, or string arrays. These types coexist per property; scalars
are never converted to arrays. Strings are at most 1024 UTF-16 code units,
including each array element (a supplementary Unicode character counts as two).
Arrays contain at most 32 raw elements BEFORE deduplication. Null, number,
object, mixed/non-string or nested-array values invalidate the entire response,
even on an unapproved key. Duplicate JSON member names are invalid. Consumers
ignore unknown envelope members and import only deployment-approved keys with
the exact configured Boolean/String/StringArray type. No academic property name
or prefix is built into the OSS. JSON Schema maxLength counts Unicode code points;
the stricter UTF-16 runtime limit is normative.

StringArray has set semantics: validate raw limits, then deduplicate and sort with
ordinal comparison. Preserve case, whitespace and empty strings. `[]` is a
confirmed empty set; a missing key is unknown and removes its previous value.

Use HTTP 404 for an unknown subject, 401/403 for failed service authorization, and
503 for unavailable data. Consumers accept only 200 JSON success and treat every
other result, wrong identity, malformed body, timeout or oversized body as a
failed confirmation. The consumer's body limit is 64 KiB and default timeout is
five seconds. Do not return a partial set as success when a source lookup fails.

There is no producer revision, hash, update flag, ETag, NotModified or conditional
request in version 1.0. The implementer reads current data and returns the full
set. Schema: [response schema](contracts/provider-profile/v1.0/response.schema.json).

## Consumer storage and claims

HybridIdP stores each approved source separately in nullable Person JSON, with
the exact tuple, source account, confirmation state/time and an IdP-local SHA256
hash. The hash uses ordinal key ordering and typed, normalized JSON values. Array
order and duplicates do not change it; changed members, scalar/array type changes
and removed keys do. An AllowedProperties type change invalidates cached source
confirmation and requires a fresh fetch; an old scalar is never reused as an array.
It detects local
value changes; it proves neither source freshness nor authorization. An unchanged
hash still advances confirmation time and re-evaluates the current claim rules.
Failed confirmation leaves historical values in storage but prevents projection.

Before projection the source account must still belong to that Person and retain
the exact durable provider link. Ambiguous links, disabled sources, stale or
never-confirmed snapshots supply no new claims. A successful upstream login
forces confirmation. Applicable token issuance/refresh re-confirms expired data;
the configurable initial maximum age is five minutes (zero means every issuance
request). This is a deployment policy, not a guaranteed freshness SLA. Existing
JWTs remain unchanged until normal expiry.

A custom claim selects one configured source. It maps one approved property
directly, or evaluates a Boolean rule with Equals, NotEquals, StartsWith, Contains,
All and Any. Direct StringArray mappings emit actual JSON arrays in tokens and
UserInfo for zero, one or many elements, never CSV or a JSON-encoded string.
After cryptographic token validation, the consumer restores approved array shape
from the validated payload (the inner token for JWE); it neither guesses an empty
array from absent claims nor fetches current Profile data to rewrite issued tokens.
Missing/unconfirmed direct properties remain absent even with AlwaysInclude.
Rules
have at most four levels, 32 nodes and eight children per group. Every referenced
input must be available and correctly typed before evaluation; unknown inputs
omit the claim rather than asserting false. Comparisons are ordinal. Known
results are JSON booleans in tokens and UserInfo. Granted scopes still control
disclosure; protected protocol/security claims cannot be mapped.

| Operator | Inputs and meaning |
|---|---|
| Equals / NotEquals | Typed String or Boolean equality / inequality; no array equality or implicit conversion. |
| StartsWith | String with a nonempty String prefix. |
| Contains | String with a nonempty substring, or StringArray with an exact String member (including an empty string). |
| All | AND: every child condition must match. |
| Any | OR: at least one child condition must match. |

All/Any contain condition nodes in `children`, with optional nested groups.
Every referenced input must be known before evaluating any group, even when an
OR could short-circuit. A known empty array with Contains evaluates false; a
missing array is unknown and omits the claim. NotEquals never negates unknown.
These rules are profile projection, not automatic IdP role assignment.

In Admin Claims, select a configured Profile source and its approved property for
a direct mapping; its data type is selected automatically. For a derived Boolean,
use the visual condition builder with typed values and nested AND/OR groups, or
the synchronized Advanced JSON mode. Choices come from deployment AllowedProperties
through the Claims.Read-protected `GET /api/admin/claims/profile-sources`. This
endpoint returns only Enabled, source names and property-key/type pairs. It never
returns endpoints, secrets, provider identities or profile values. The UI does not
edit deployment policy. Missing configuration and load errors remain visible;
server approval/type/rule validation remains authoritative on every save.

```json
{
  "providerProfileSource": "campus",
  "dataType": "Boolean",
  "condition": {
    "operator": "All",
    "children": [
      { "operator": "Equals", "property": "is_student", "value": true },
      { "operator": "StartsWith", "property": "student_id", "value": "4" }
    ]
  }
}
```

No expression code, regex, SQL or network call is executed by a rule.
