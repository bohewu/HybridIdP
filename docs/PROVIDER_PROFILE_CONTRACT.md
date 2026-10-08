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
  "extraProperties": { "is_student": true, "student_id": "4-example" }
}
```

Version and tuple must exactly match the request. `extraProperties` is the complete
current set, including an empty object when no properties remain. Missing keys
remove their old values. Property names contain 1-64 ASCII letters, digits,
underscore, hyphen or dot. At most 32 properties are permitted. Values are JSON
strings (at most 1024 characters) or booleans; nulls, numbers, arrays and objects
are invalid. Duplicate JSON member names are invalid. Consumers ignore unknown
envelope members and import only deployment-approved keys with the configured
String/Boolean type. No academic property name or prefix is built into the OSS.

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
hash. The hash uses ordinal key ordering and typed JSON values. It detects local
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
directly, or evaluates a Boolean rule with Equals, StartsWith, All and Any. Rules
have at most four levels, 32 nodes and eight children per group. Every referenced
input must be available and correctly typed before evaluation; unknown inputs
omit the claim rather than asserting false. Comparisons are ordinal. Known
results are JSON booleans in tokens and UserInfo. Granted scopes still control
disclosure; protected protocol/security claims cannot be mapped.

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
