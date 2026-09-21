# Provider Metadata Contract 1.0

This is the first formal public version of the password-free provider metadata
contract. It is independent of Provider Proof Contract 1.0 and the optional,
independently unversioned Legacy Password Sync contract. Enabling one does not enable
the others. No private SDK, shared producer assembly or repository is required.

## Purpose and ownership

An authenticated service requests email evidence for an already established
provider identity: the exact pair of provider namespace and opaque stable
subject. The subject is immutable within that namespace. It is not a login
name, email address, directory path or cross-provider person identifier.

The provider owns the accuracy of its returned email and trust evidence.
HybridIdP owns durable local links, local eligibility, acceptance of that
evidence, recovery enrollment, MFA, sessions, claims and tokens. Metadata does
not authenticate a user's credentials, authorize password writes, or establish
an identity link. It carries no affiliation, role, group, completeness/status,
directory ownership or observation-time authority. An HTTP success alone is
not verified email evidence or permission to recover an account.

## HTTP exchange

Send `POST /api/authenticate/metadata` to the selected provider's configured
endpoint, for example `https://provider.example.org/api/authenticate/metadata`.
HybridIdP uses the full `ProviderMetadataRefresh:Endpoint` URI verbatim; it does
not discover a provider, append a route or choose an alternative provider.
HybridIdP is the consumer, not the host of this endpoint.

Use `Content-Type: application/json` and the service credential in the
`X-Internal-Secret` header. This is service authentication, not a user's password
or bearer proof. Never put the secret in the URI or JSON body. Provision it out
of band. The existing endpoint also permits a deployment network allowlist;
network admission does not replace the secret. A third-party endpoint must
authenticate the caller before returning metadata.

HTTPS with normal certificate validation is the default. Authenticated HTTP is
allowed only with explicit `AllowPrivateNetworkHttp=true` for a deployment's
controlled private network. This flag permits HTTP; it does not itself validate
whether a hostname resolves to a private address. Configure only trusted fixed
endpoints, with no redirects; the current typed HTTP client has the platform's
default redirect behavior, not a provider-discovery or redirect trust policy.

Request:

```json
{
  "contractVersion": "1.0",
  "providerNamespace": "example.provider",
  "stableSubject": "opaque-subject-7f2a"
}
```

Response (`200`, `application/json`):

```json
{
  "contractVersion": "1.0",
  "providerNamespace": "example.provider",
  "stableSubject": "opaque-subject-7f2a",
  "email": "person@example.org",
  "emailTrustOrigin": "SourceVerified",
  "verifiedAt": "2026-09-01T10:30:00Z"
}
```

## Fields and validation

Emit the camelCase member names shown here. The current JSON reader also
accepts case-insensitive member names. Values of namespace, subject and version
are compared exactly, case-sensitively, without trimming or normalization.

| Field | Request | Response | Type and meaning |
| --- | --- | --- | --- |
| `contractVersion` | Required | Required | Non-null string, exactly `1.0`. Omission is rejected even though in-process DTO construction defaults to `1.0`. |
| `providerNamespace` | Required | Required | Nonblank string, at most 200 UTF-16 code units; identifies the provider's stable key space. |
| `stableSubject` | Required | Required | Nonblank string, at most 256 UTF-16 code units; opaque provider-scoped key. |
| `email` | Not sent | Optional | String or null; omitted means null. Unknown/untrusted email is not accepted as recovery evidence. |
| `emailTrustOrigin` | Not sent | Optional | `Unknown`, `SourceVerified` or `PolicyTrusted`; omission means `Unknown`; explicit null is invalid. |
| `verifiedAt` | Not sent | Optional | ISO 8601 string or null; omitted means null. The time the source verified the email, not the time metadata was fetched. |

`SourceVerified` means the provider asserts source verification.
`PolicyTrusted` means trust comes from an explicitly configured provider policy,
not proof of possession at a known time. Neither substitutes for local policy.
Both require a nonblank email accepted by `MailAddress.TryCreate` whose parsed
address equals the trimmed input, ignoring case. Display-name mailbox forms are
rejected. An `Unknown` email is allowed as an untrusted value and discarded by
the consumer; its syntax does not confer trust.

A non-null `verifiedAt` requires `SourceVerified`. `SourceVerified` with a null
timestamp remains representable: it must not be treated as evidence of recent
verification. The consumer never synthesizes that timestamp from fetch time.
Freshness, future-time rejection and recovery eligibility belong to local
policy, not wire validity. Offsets are normalized to UTC on snapshot write.

Emit timestamps with `Z` or an explicit numeric offset. The existing
System.Text.Json `DateTimeOffset` reader additionally accepts date-only and
offsetless ISO forms using the consumer's local offset; implementations should
avoid those nonportable forms. Malformed dates, invalid offsets, and out-of-range
dates fail parsing. Emit canonical trust-origin strings. The existing enum
reader additionally accepts case variants and defined integer values `0`, `1`,
`2` (and their numeric strings) for the three origins respectively. Unknown
enum values are rejected by parsing or semantic validation.

Unknown JSON members are ignored at both boundaries and never retained as
extension data. Adding an unknown member cannot supply authority, override a
known value, or create a local link. Required names must not be repeated;
the existing reader otherwise uses the last duplicate value. Producers must
emit only the documented members; consumers must never infer trust from extras.

## Status and error behavior

The existing endpoint behavior is:

| HTTP status | Meaning |
| --- | --- |
| `200` | A valid response, including a response with null email, `Unknown` trust and null verification time when evidence is disabled, missing, ambiguous or unavailable. This is not proof that an account exists. |
| `400` | Invalid, null or malformed request, including an unsupported contract version. |
| `401` | Missing or invalid service secret. |
| `403` | Deployment network admission rejected the caller. |
| `500` | Endpoint service-secret configuration is unavailable, or another server failure. |
| `503` | The endpoint could not produce a structurally valid result. |

There is no versioned error-envelope DTO. Validation may produce framework
problem details or an empty body; authentication may produce a JSON message;
network admission may produce text or an empty body. Consumers must not parse
those bodies as metadata or depend on their wording. There is no OAuth error
contract, `404` subject-existence contract, retry-after guarantee or automatic
version negotiation. Other non-success statuses are also failures.

The HybridIdP adapter accepts a successfully parsed, validated, tuple-matching
body on any HTTP `2xx`; empty bodies are malformed and JSON `null` is missing
evidence. It does not
enforce the response media-type header separately from JSON parsing. Providers
must emit JSON with the media type above.

## Versioning and failures

Both messages explicitly carry `contractVersion: "1.0"`. There is no version
header, URL version, downgrade, supported-version discovery or retry negotiation.
The endpoint rejects an unsupported request version with `400`; the consumer
rejects an unsupported response version as `Unsupported`. An echoed namespace or
subject mismatch is also malformed. Unknown fields do not negotiate a new
version. Incompatible field or authority changes require a separate version
decision and documented migration/deprecation before adoption; no deprecation
date or legacy compatibility alias is defined for this first public version.

Metadata refresh is disabled by default. With it enabled, an exact durable
binding must exist before any HTTP request. Invalid local arguments, missing
binding and disabled operation do not call the provider. A missing/invalid
endpoint, invalid JSON, unsupported version or mismatched tuple invalidates
current snapshot evidence. Transport errors, non-`2xx` statuses and timeout also
invalidate evidence. The adapter persists and reports `AuthenticationFailed`
for `401`/`403`, `TimedOut` for timeout, `Unavailable` for other HTTP/transport
failures, `Unsupported` for an explicit unsupported response version,
`Malformed` for invalid data, and `Missing` for JSON `null`. Missing required
fields are malformed. A valid response with unknown email trust, or a future
verification timestamp, is `Untrusted`; only usable trusted email is
`Refreshed`. A valid unknown response does not reveal whether source evidence
was absent, disabled, ambiguous or unavailable, so the consumer does not infer
one of those causes. These are local outcomes, not wire fields.
Caller cancellation propagates. No retry is installed by this adapter.

Recovery policy accepts source email only when explicitly enabled for its trust
origin and its snapshot is `Available`. Evidence fetched before the configured
current period's `EffectiveAtUtc` is `Stale`; future fetch or verification times
are `Untrusted`. This uses the existing local period boundary, not an inferred
source observation time or a new wire freshness guarantee. Source timestamps
never satisfy current-period local recovery-email verification. Failed or stale
source evidence cannot bootstrap recovery, while independent locally verified
recovery email, local eligibility, credential proof and guarded writeback keep
their existing rules. There is no affiliation owner or affiliation-owner
decision surface in this implementation, and policy cannot exempt a user from
current-period verification using metadata, extras or legacy configuration.

The `AddProviderEmailSnapshot` migrations for both database providers create a
fresh `ProviderEmailSnapshots` cache without importing legacy rows. The
withdrawn, unreleased draft cache migration ID is retained as a no-op for local
history compatibility, and its draft model is removed from migration metadata.
An existing draft cache table is left untouched and unmapped; only a new
validated refresh can populate the current cache. Independent recovery-email
records and account bindings are not changed. Rollback removes only the new
cache and does not restore legacy authority; restarting withdrawn binaries is
not a supported rollback. Operators must review and apply the appropriate
migration before enabling this implementation. No migration is applied by these
offline checks.

The deadline covers sending and reading the body: default five seconds,
configuration range greater than zero through thirty seconds. Authentication
hooks preserve their prior successful credential result if refresh fails;
metadata failure must never authenticate a credential or create a verified
email. Logging must exclude secrets, response bodies, subjects and addresses.

## Consumer configuration

The options, service, typed HTTP client registration and configuration section
use the `ProviderMetadataRefresh` vocabulary. Use these deployment keys:

```text
ProviderMetadataRefresh__Enabled=false
ProviderMetadataRefresh__Endpoint=https://provider.example.org/api/authenticate/metadata
ProviderMetadataRefresh__SharedSecret=<PROVISION_THROUGH_SECRET_CONFIGURATION>
ProviderMetadataRefresh__AllowPrivateNetworkHttp=false
ProviderMetadataRefresh__Timeout=00:00:05
```

The repository's deployment compose files pass the selected environment file
through to the container. Restart after changing these startup-bound options.
Neither a secret nor an endpoint is supplied by default. Enabled operation
requires both, and invalid options fail startup validation. This documentation
does not enable the feature or authorize a deployment or database migration.

## Schema, fixtures and verification

[The single JSON Schema](contracts/provider-metadata.schema.json) describes
responses at its root and requests at `#/$defs/request`.
[Synthetic fixtures](contracts/fixtures/provider-metadata/) cover required and
nullable fields, ignored unknown members, timestamps and unsupported versions.
Validate request fixtures against the request definition. Schema validation is
structural; the application additionally validates mailbox syntax, actual
calendar/offset validity, UTF-16 length and the exact requested tuple. JSON
Schema lengths count Unicode code points, so supplementary characters also
need the application's UTF-16 length check. Case-insensitive member-name
tolerance is a reader feature; the schema uses canonical camelCase names.

Focused consumer checks:

```text
dotnet test Tests.Infrastructure.UnitTests/Tests.Infrastructure.UnitTests.csproj --filter "FullyQualifiedName~ProviderMetadata"
dotnet test Tests.Infrastructure.IntegrationTests/Tests.Infrastructure.IntegrationTests.csproj --filter "FullyQualifiedName~ProviderMetadataRefreshIntegrationTests"
```

These checks use synthetic HTTP responses and local test storage. They do not
establish deployed producer interoperability, directory access, recovery
authorization or password-sync success. Provider Proof and optional Password
Sync retain their separate authentication, binding and failure contracts; see
[Authentication Integration](AUTHENTICATION_INTEGRATION.md) and
[Security](SECURITY.md) for the surrounding local policy.
