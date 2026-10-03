# Provider Lifecycle Status Contract 1.0 (review draft)

Status: neutral, unpublished wire contract, 2026-10-03. The HybridIdP consumer
is implemented and default disabled; independent final review passed after one side-effect ordering repair.
No producer is selected and no runtime enablement is established. The API answers
read-only account lifecycle questions for an existing trusted provider binding.
It does not authenticate credentials.

## Authority and identity

The request selects the exact opaque `providerNamespace + stableSubject` from
an existing server-owned binding for the specific ApplicationUser. Compare both
ordinally, case sensitively, without trimming, normalization or reinterpretation.
No browser-selected tuple, email, name, document ID, account prefix or directory
path may substitute for that binding. These values identify an account, not a
Person. No cross-provider universal person key is introduced.

Before deployment, the producer must document its approved source authority,
supported exact namespaces, authoritative lifecycle data and exact managed
mapping to a unique immutable source account. The consumer must approve that
authority and mapping version for the binding. A service secret alone proves
caller admission, not lifecycle authority. A missing/unverified mapping or
unreadable source is Unavailable; multiple source matches are Ambiguous;
NotFound means an authoritative exact lookup completed with no match. Never
guess a join or probe another authority. Unsupported namespaces are Unsupported.

HybridIdP retains local ApplicationUser eligibility, Person.CanAuthenticate(),
links, provisioning, MFA, sessions, tokens, claims and consent. One Person can
have multiple ApplicationUsers, each with its own IsActive. A non-Active,
deleted, future-started or expired Person denies its linked accounts under
current local rules. An Enabled response cannot override any local denial.
Version 1.0 deliberately has no affiliation fields: it cannot set Person.Status,
grant roles/groups, assert teaching eligibility or authorize directory placement.

## Endpoint, dispatch and transport

Each capability independently selects one complete fixed URL, service credential
and deadline. This API uses HTTP even when co-located. No common BaseUrl, route,
host, producer SDK, producer assembly loading or shared database is required.
Per-namespace/multiple-lifecycle-endpoint routing remains backlog.

Send POST to the complete configured lifecycle URL with `Content-Type:
application/json`, `X-Internal-Secret` from protected secret configuration and
`X-Provider-Contract: lifecycle-status/1.0`. Exactly one discriminator header is
required. The body also requires `contractType: "lifecycle-status"`. Endpoint
selection and authority approval are independent; this document adds no settings.

The same service and the same literal URL may serve existing contracts. A shared
URL producer must authenticate/admit the request, bound and parse the body, then
classify contract candidates before any source dispatch. The lifecycle candidate
requires the exact header and body discriminator. Missing/duplicate/conflicting
discriminators, mixed contract signatures or more than one candidate are rejected
as 400 without source work; an unsupported lifecycle header version is 400
Unsupported. For example, lifecycle plus `accountName`/`password`, RIV `scheme`
and `evidence`, or Password Sync `operationId` is a collision, never a reason to
try that other contract. Without the lifecycle header, never dispatch lifecycle.
Legacy candidates use their existing documented request signatures; if signatures
overlap or cannot be distinguished, reject the request or configure distinct URLs.
Candidate classification is not trial execution. Do not modify existing envelopes,
append discriminators to their bodies or use parser order, a namespace prefix,
credential failure or a producer lookup as a routing decision.

Require HTTPS, normal server certificate/hostname verification, trusted fixed
endpoint validation and no URI userinfo/query/fragment. No HTTP relaxation is
defined for this new capability. Disable redirects, cookies, automatic retries
and response decompression; reject nonempty Content-Encoding. Never forward
credentials or a body to another endpoint. Default consumer deadline is five
seconds; a future implementation may independently configure 1-10 seconds. It
covers send, headers and complete bounded body read, using a monotonic deadline.
Source work must fit a shorter producer budget. Propagate caller cancellation.

Request limit: 4 KiB; response limit: 8 KiB; JSON nesting depth: 8 (root counts
as one). Count UTF-8 wire bytes including whitespace; enforce streamed size even
without Content-Length. Require valid UTF-8 without BOM, one JSON object, exact
case-sensitive member/enum names, no comments/trailing commas, duplicates
(including escaped equivalent names), unknown members or null required values.
Reject malformed Unicode/unpaired surrogates and Unicode control characters in
opaque strings; require at least one non-whitespace scalar without trimming it.
Strings below use Unicode scalar/code-point lengths. JSON media type is exactly
application/json (case-insensitive media token), optionally charset=utf-8 only;
other parameters/charsets and +json types are rejected. Send Cache-Control:
no-store on all responses. Do not expose raw tuples, source identifiers, results,
secrets or bodies in URLs, browser state, claims or logs. Sanitized requestId and
coarse outcome may be recorded without identity payloads.

## Wire fields

[Schema](contracts/provider-lifecycle/v1.0/lifecycle-status.schema.json) validates
responses at the root and requests at `#/$defs/request`. No discovery, downgrade,
retry negotiation or shared envelope is defined. Incompatible changes require
an agreed new version; no deprecation date is set for this unpublished draft.

| Request field | Required type and limits |
| --- | --- |
| contractType | String, exactly lifecycle-status |
| contractVersion | String, exactly 1.0 |
| requestId | Nonzero lowercase D-format UUID string; new correlation per call, no authorization/replay value |
| providerNamespace | Opaque string, 1-200 code points |
| stableSubject | Opaque string, 1-256 code points |

Every response requires contractType, contractVersion, requestId and outcome.
requestId echoes the exact request. Only Malformed permits null when input had
no valid correlation; consumers never accept null for their own valid request.

| Found-only field | Required type and limits |
| --- | --- |
| binding | Object containing exactly providerNamespace and stableSubject, same limits as request |
| accountState | String: Enabled, Disabled, Retired, Superseded or Unknown |
| evidence | Object with exactly the six fields below |
| evidence.sourceAuthority | Opaque string, 1-200 code points; consumer-approved authority ID |
| evidence.mappingVersion | Opaque string, 1-128 code points; exact approved managed mapping revision |
| evidence.snapshotVersion | Opaque string, 1-128 code points; source snapshot/revision, not an API version |
| evidence.observedAt | Actual source observation time; timestamp format below |
| evidence.effectiveFrom / evidence.effectiveUntil | Required timestamp bounds for that account state; finite half-open interval |
| successor | Optional object with exactly providerNamespace and stableSubject; allowed only for Retired/Superseded |

The evidence object has six fields (sourceAuthority, mappingVersion,
snapshotVersion, observedAt, effectiveFrom, effectiveUntil). All timestamps are
UTC strings `YYYY-MM-DDTHH:mm:ss.SSSZ`, years 0001-9999, valid Gregorian dates,
no leap seconds. No offsets, date-only or offsetless forms are accepted.
SnapshotVersion identifies the source record/snapshot actually read, including
an explicitly documented immutable source revision. Do not generate a random
version or observedAt at response time to make cached/unknown evidence look
current. A producer without supported source freshness evidence is Unavailable.
Snapshot versions are opaque, not numerically ordered or anti-rollback proof.

All non-Found responses contain only the four envelope fields; binding,
accountState, evidence and successor are forbidden, including null. Found
Unknown represents a resolved account whose authoritative state cannot be
classified; it never permits a covered operation. Unknown differs from a lookup
or transport failure.

## HTTP outcomes

| HTTP | Wire outcome/body | Meaning and consumer disposition |
| --- | --- | --- |
| 200 | Found | Unique account and source evidence; only locally accepted Enabled can permit a covered operation |
| 200 | NotFound | Exact authoritative lookup found no account; deny, no retirement inference |
| 200 | Ambiguous | Multiple candidates; deny, no identity disclosure or auto-link |
| 503 | Unavailable | Disabled/missing source capability, uncertain mapping, source timeout/failure; deny |
| 400 | Unsupported | Unsupported version/namespace/contract selector; no source lookup, deny |
| 400 | Malformed | Invalid request/shape or dispatch collision; no source lookup, deny |
| 401/403 | Existing service auth/network admission body | AuthenticationFailed locally; no source lookup, never parse as evidence |
| 413/415 | Admission body | Oversize/invalid media type; no source lookup, deny |
| 429 | Admission body | Rate limited; deny, no automatic retry |
| Any other status, including 3xx/other 2xx/5xx | No accepted contract result | Operational failure, deny |

A consumer must verify exact HTTP/outcome pairing, JSON/media/size/shape/version,
requestId and requested tuple before considering Found evidence. Invalid,
unsupported or mismatched responses are local failures, never rewritten into
Found Disabled. Consumer transport failure is Unavailable; its deadline is
Timeout; malformed response is Malformed. These local diagnostic categories
are not additional wire outcomes. Errors never authorize fallback or mutation.

## Freshness and local acceptance

Structural schema validity does not prove correlation, authority, mapping,
freshness, source integrity, time ordering or local eligibility. For each future
policy-covered operation, validate at one consumer UTC instant T immediately
before the protected action. Use a healthy trusted clock; clock uncertainty or
detected backward movement denies. Version 1.0 applies zero skew tolerance.
At later action checkpoints re-evaluate time and current local eligibility even
if reusing the same request's result.

Require approved sourceAuthority/mappingVersion, unchanged trusted binding
identity/version, exact requestId/tuple, and all these conditions:

1. effectiveFrom < effectiveUntil.
2. observedAt <= T; reject future observations.
3. T - observedAt <= the finite local maxAge, inclusive at the deadline.
4. effectiveFrom <= T < effectiveUntil; start is inclusive and end exclusive.
5. accountState is Enabled and current ApplicationUser/Person policy permits.

Draft local maxAge default: 60 seconds; permitted local value: integer 1-300
seconds, chosen explicitly before enablement. maxAge is local policy, never a
producer-controlled wire field. The source interval does not waive maxAge and
HTTP receipt/response time never manufactures source freshness. Scheduled future
or expired state fails acceptance even if its timestamp is schema-valid. A source
that cannot guarantee the asserted state for this interval must return Unknown
or Unavailable. No absolute immediate upstream-change visibility is promised.

Reuse accepted evidence only within the same request for the same unchanged
binding and policy; no persistent allow cache, cross-request reuse or background
projection is defined. Required checks fail closed for every non-Enabled state,
missing binding, unapproved authority, stale/future evidence or any failure.
No outage/result permanently changes IsActive, Person.Status, links, security
stamps or sessions. Remote password failure is not lifecycle evidence and must
never retire an account or mutate a Person.

## Retirement and future integration

Retired/Superseded denies the queried old account. An optional successor is
informational and cannot prove same Person, authorize linking/provisioning,
select another credential authority or trigger another lifecycle lookup. It must
not equal the queried tuple. For synthetic teacher 00015 -> retiree R0001, old
account denial does not imply Person Resigned: eligible Person and other eligible
accounts may remain usable under local policy. Prefixes never prove this mapping.
A separate protected, explicitly authorized transition must establish exact
accounts, same-Person evidence, expected versions, local policy, atomic account
changes and revocation completion; none is implemented by this read API.

Future consumer enforcement must cover password, Passkey and external login
before full cookie/token issuance, each cookie continuation and each new
authorization-code, refresh, device, password or equivalent grant issuance.
Passkey does not call upstream password proof; lifecycle must be an independent
policy boundary. Existing local checks remain authoritative. Already-issued
self-contained access tokens may remain valid until expiry; immediate resource
server invalidation is separate work.

See [capability inventory](PROVIDER_API_CAPABILITIES.md),
[integration handoff](implementation_plans/provider-api-lifecycle-plan.md),
[synthetic examples](contracts/provider-lifecycle/v1.0/examples/README.md),
[authentication integration](AUTHENTICATION_INTEGRATION.md) and
[security](SECURITY.md). Synthetic validation proves draft structure and explicit
acceptance examples only, not runtime enforcement or producer interoperability.
