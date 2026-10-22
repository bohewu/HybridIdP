# Recovery Identity Verification contracts (1.0 and 2.0)

The original sections below describe the 1.0 compatibility mode.
For independently selected neutral queries, see [version 2.0](#neutral-query-version-20)
and the [common contract](NEUTRAL_QUERY_CONTRACT.md). Neither format is inferred
from a failed request; all original authority limits continue to apply.

## Neutral query version 2.0

The [common neutral-query contract](NEUTRAL_QUERY_CONTRACT.md) is normative for
selection, typed candidates, queryRule, approved mappings, comparison, unique
matching, transport, response correlation and mutation invalidation. Legacy
formats remain unchanged under explicit compatibility selection.

Neutral-v2 uses `recovery-identity-verification/2.0`. Its availableCandidates are
typed `employeeId` (String), `nationalIdHash` (Sha256Hex), and `accountName`
(String). The seven nonempty sets in the common contract may be explicitly
agreed. Use only trusted server-resolved/approved lookup inputs for the already
selected local account. Common normalization and unique source-account matching
apply; no prior SSO/Proof login, subject UUID mapping or public account discovery
is introduced. The anonymous browser cannot select the source/query/rule.

The request independently includes `scheme: "identity-identifier"` and
`evidence: { identityIdentifier }` with the same unchanged submitted evidence,
128-code-point limit and producer Trim().ToUpperInvariant() comparison as 1.0.
Lookup attributes select the target only. They NEVER substitute for that evidence,
even when a nationalIdHash query was used. The producer must verify the separately
submitted raw evidence against its trusted source document/storage algorithm;
a matching lookup, received digest or consumer's approval is not verification.

Found returns `result: { outcome: "Verified" | "Denied", scheme:
"identity-identifier" }`. It carries no login binding. NotFound maps to generic
Denied locally; Ambiguous uses HTTP 503 and grants nothing, as does Unavailable.
Verified requires exact requestId/source/type/version plus the immutable pending
query/local-account context and independent evidence verification. The restricted
durable grant binds that account, query/configuration/approval revisions, stamps,
policy, destination and browser/CSRF context. Do not persist raw query identifiers,
document hashes or submitted evidence in the grant or logs. Changes invalidate
it at VerifyIdentity, SendOtp, VerifyOtp and Reset; preserve atomic single-use
consumption, recipient stability, OTP, eligibility and uncertain-write barriers.
Verified alone never signs in, resets a password or bypasses OTP. Keep uniform
anonymous failure presentation and the existing bounded disclosure policy.

Wire definitions: [request](contracts/recovery-identity-verification/v2.0/contract.schema.json#/$defs/request)
and [response](contracts/recovery-identity-verification/v2.0/contract.schema.json#/$defs/response).
[Synthetic examples](contracts/recovery-identity-verification/v2.0/examples/) cover Found, NotFound,
Ambiguous, Unavailable, Unsupported, missing inputs and forbidden mixed shapes.
Their validity proves structure only; semantic, authority and live product checks
remain separate. queryRule is omitted in these unambiguous agreed-rule examples.

## Version 1.0 compatibility contract


Baseline: `RIV-1.0-PLAN-20260930-A`. This is the fixed implementation baseline,
not a declaration of a published or deployed API. Appendix A UTF-8/LF SHA-256:
`27ab73f37bf595d911d89c82b7d32ace53cc86192d120d905d2f001f35d5166f`.

## Responsibility

This boundary answers whether submitted identity evidence matches the provider
subject already bound by the consumer. It provides supplementary recovery
verification, not password authentication, MFA, formal identity assurance,
account discovery or reset authorization. Re-entering an identifier that is also
the login name does not create an independent factor.

The producer does not send mail, return a recovery address, create an account,
Person or binding, change passwords/MFA/lockout, or issue login tokens. Verified
permits only a restricted consumer-owned precheck grant. Recovery email OTP or
an independently authorized administrator ceremony remains required. This
contract is independent of Provider Proof, Provider Metadata and Password Sync.

Only the `identity-identifier` scheme exists in 1.0. The identifier may represent
any deployment-approved identity document; no national-format assumption applies.
It is neither a row key nor a stable subject. Birthday is absent from requests,
required evidence and hidden verification, including when birthday happens to
be available. A future scheme requires a separately agreed contract revision.

## HTTP and admission

Suggested producer route: `POST /api/recovery/verify-identity`. The consumer uses
one configured complete endpoint URI without appending paths. This is a route
on an existing service and requires no new process, port or deployment.

Requests and ordinary contract responses use `application/json` and UTF-8.
JSON property names and enum values are case sensitive. Use HTTPS with server
certificate verification, the `X-Internal-Secret` service authentication header,
and existing network/IP admission. Authentication failure precedes source lookup.
Provision secrets through secure configuration, never fixtures, URLs or browsers.
Only explicitly isolated loopback synthetic tests may use HTTP. Redirects must
not be followed or forward the body. Do not add anonymous admission exceptions.

Limit request bodies to 8 KiB, response bodies to 4 KiB and JSON depth to 8.
Reject duplicate properties (including equivalent escaped names), unknown
properties, null required fields, invalid encodings and unsupported shapes.
No comments or trailing commas are JSON. The consumer deadline covers sending
and reading the entire body: default five seconds, configurable from one to ten
seconds. Producer source work uses a shorter budget. Propagate cancellation;
do not automatically retry. Respond with `Cache-Control: no-store`. Do not put
evidence, subjects, secrets or verification results in query strings.

## Request

```json
{
  "contractVersion": "1.0",
  "requestId": "73aab4b5-2dfa-471a-8c2b-1d70d7540092",
  "providerNamespace": "example.provider",
  "stableSubject": "synthetic-subject-001",
  "scheme": "identity-identifier",
  "evidence": { "identityIdentifier": "TEST-ID-0001" }
}
```

Every call gets a nonzero lowercase D-format UUID requestId. It is correlation,
not a browser bearer token or a receipt allowing verification to be skipped.
Repeated request IDs require fresh verification and remain rate limited.

The server obtains providerNamespace (1-200 Unicode code points) and stableSubject
(1-256 code points) from an existing trusted binding. Both are opaque and compared
ordinally without trimming or case conversion. Browser input cannot choose them,
the scheme, a verifier, or a recovery recipient. Never substitute names, email or
submitted evidence for the target subject.

The consumer sends identityIdentifier unchanged: 1-128 Unicode code points, no
control characters, malformed Unicode or all-whitespace value. Count supplementary
characters as one scalar, reject unpaired UTF-16 surrogates and invalid UTF-8.
The schema explicitly excludes C0 and DEL; application semantics also reject
Unicode control characters and trim-empty evidence. Opaque binding values retain
their exact contents. No identifier hash, normalization or Person-field write is
performed by the consumer.

Only the producer comparison applies `Trim().ToUpperInvariant()` to the submitted
and source synthetic/document values. Interior spaces and hyphens remain; there
is no fullwidth conversion, source probing based on format or approximate match.
An alternate source canonicalization requires an agreed baseline revision.
Hashed source data requires its explicit trusted storage algorithm, never guessing
from a hash-shaped input or accepting a received hash as a replayable credential.
Evidence and sensitive DTOs must not be persisted, logged, destructured or placed
in diagnostics. The DTO ToString implementations redact values; this does not make
arbitrary structured logging safe.

## Response and consumer acceptance

```json
{
  "contractVersion": "1.0",
  "requestId": "73aab4b5-2dfa-471a-8c2b-1d70d7540092",
  "outcome": "Verified",
  "binding": {
    "providerNamespace": "example.provider",
    "stableSubject": "synthetic-subject-001",
    "scheme": "identity-identifier"
  }
}
```

A non-success response contains exactly contractVersion, requestId and outcome:

```json
{
  "contractVersion": "1.0",
  "requestId": "73aab4b5-2dfa-471a-8c2b-1d70d7540092",
  "outcome": "Denied"
}
```

Verified requires binding; all other outcomes forbid that property, even null.
Non-success must not include subject, canonical account, name, identifier or hash,
birthday, field comparison details, email, table names or routing. All responses
require version, requestId and outcome. A syntactically valid request receives
its exact requestId. Only Malformed may have null requestId, and only if the
malformed input supplied no parseable valid requestId. The consumer always sends
a valid ID, so null correlation is never accepted for its own request.

| HTTP | Outcome | Required meaning |
| --- | --- | --- |
| 200 | Verified | Exactly one eligible subject, available required source data, all checks pass. |
| 200 | Denied | Missing subject, mismatched evidence or explicit ineligibility; no detailed reason. |
| 503 | Unavailable | Disabled feature, missing data, uncertain mapping, ambiguity, timeout or unreadable source. |
| 400 | Unsupported | Unsupported version or scheme; zero source lookups, no downgrade. |
| 400 | Malformed | Invalid structure, value or encoding; zero source lookups. |
| 401/403 | Existing auth/admission body | Operational failure; never parse it as proof. |
| 413/415 | Oversize/wrong media type | Zero source lookups; no grant. |
| 429 | Rate limited | Optional Retry-After, no guaranteed contract body, no automatic retry or relaxed verification. |

Only HTTP 200, valid JSON media type and body, version 1.0, exact requestId and
an ordinally identical complete binding tuple with outcome Verified can advance.
Reject empty/HTML/null bodies, invalid types, unknown enums, bad media types and
wrong status/outcome pairs. Denied and all other failures never create grants.
Anonymous UI uses a uniform failure presentation for missing account, mismatch,
unavailable source/destination and related failures. A post-verification masked
address is an intentional bounded disclosure, not complete enumeration prevention.

## Consumer grant and replay boundary

The producer supplies no browser-redeemable JWT or reset proof. A durable local
grant binds localAccountId, provider binding identity/version, securityStamp,
browserContextHash, csrfContextHash, effectivePolicyVersion, destination
fingerprint/version, expiry and consumed/revoked state. Keep only necessary state
and sanitized correlation; never retain identifier raw values or hashes.

Suggested TTL is five minutes and no longer than the recovery ceremony. SendOtp
atomically consumes the grant and reserves one challenge/send across instances.
SMTP runs outside the transaction; failure makes the reservation unredeemable.
Resend retains the same authorized context and existing cooldown/attempt policy.
RequestId or Verified alone cannot authorize resend, reset, sign-in or bypass OTP.
At VerifyIdentity, SendOtp, VerifyOtp and Reset, changes to account/binding,
securityStamp, policy, eligibility or destination invalidate the old context;
never silently switch recipient. Preserve existing credential authority and
uncertain-write barriers. Browser-posted Verified/binding/recipient/scheme has
no authority.

## Canonical schema, fixtures and delivery gates

[Schema](schemas/recovery-identity-verification.schema.json) is the exact A6
UTF-8, LF, terminal-LF extraction. SHA-256:
`2b9336d9ebda44098a706ad7fecd8af7239b5e0ae32c603d1a4cef8f843d47f1`.
Validate requests against `#/$defs/request`, responses against `#/$defs/response`.
Schema cannot prove duplicates, HTTP pairing, current tuple, trim-empty evidence,
cancellation, source eligibility or atomic consumption; apply semantic checks.

[C01-C28 fixtures](examples/recovery-identity-verification/) are synthetic case
envelopes, not endpoint payloads. Their requestJson/responseJson fields preserve
wire text, including intentionally malformed variants. TEST-ID-0001 is fake data.
The fixture README maps execution ownership and deferred gates. C01-C22 and
C26-C28 have shared wire obligations; C23-C25 are consumer-only durable tests.
A producer must document that it offers no browser grant and must not claim
consumer database coverage. A fake response is not proof of producer source
lookup, admission, normalization or mapping correctness.

The current pure implementation provides redacted DTOs, strict request serialization
and parsing, response parsing/envelope matching, a client port and a test-only fake.
Use RecoveryIdentityVerificationJson at the wire boundary, not permissive default
JsonSerializer deserialization. HTTP transport, live producer interoperability and
durable/browser acceptance are separate gates. Nothing here enables rollout.

Focused offline tests:

```text
dotnet test Tests.Infrastructure.UnitTests/Tests.Infrastructure.UnitTests.csproj --no-restore --filter FullyQualifiedName~RecoveryIdentityVerificationContractTests -nodeReuse:false
```

Before changing fields, HTTP semantics, normalization, scheme, limits or binding,
record CONTRACT_CHANGE_REQUIRED and the concrete proposed diff. Do not silently
relax validation. Align both implementations' schema checksum, fixtures, serializers
and semantic tests before accepting a contract freeze. Initial unpublished 1.0
may be jointly revised; published revisions follow the approved versioning policy.
Transfer the neutral bundle for producer comparison without editing another repo.
