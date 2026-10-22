# Legacy Password Sync contracts (unversioned and 2.0)

The original sections below describe the unversioned compatibility mode.
For independently selected neutral queries, see [version 2.0](#neutral-query-version-20)
and the [common contract](NEUTRAL_QUERY_CONTRACT.md). Neither format is inferred
from a failed request; all original authority limits continue to apply.

## Neutral query version 2.0

The [common neutral-query contract](NEUTRAL_QUERY_CONTRACT.md) is normative for
selection, typed candidates, queryRule, approved mappings, comparison, unique
matching, transport, response correlation and mutation invalidation. Legacy
formats remain unchanged under explicit compatibility selection.

Neutral-v2 uses `password-sync/2.0`. Its availableCandidates are typed
`employeeId` (String), `nationalIdHash` (Sha256Hex), and `accountName` (String).
The seven nonempty sets in the common contract may be explicitly agreed. They
must AND-match one exact authorized destination account/write scope, using the
common approved value sources and comparison rules. No public accountIdentity,
SSO UUID, previous Proof login or UUID mapping database is required. A person
identifier resolving several credential targets is Ambiguous, not fan-out.

The request adds the existing nonzero `operationId` and nonempty `password` to
the v2 query envelope. requestId correlates this HTTP call; operationId owns durable
single-send/idempotency. Found returns `result: { outcome }` with the original
NoOp/Success/PartialSuccess/Failed/CommitUnknown vocabulary; every admitted response
also echoes operationId. Internal source/target IDs and writer topology remain
private. A valid 200 NotFound/Ambiguous proves no write was attempted and becomes
local Failed. These outcomes and Unsupported never authorize a weaker query or
legacy fallback. Unavailable/non-200/malformed/mismatched/timeout responses after
possible dispatch retain the existing Unknown barrier.

The independently approved source-operation/cohort gates, destination authority,
current local account/security stamps and operation reservation remain required.
Instead of a legacy tuple-to-accountIdentity prerequisite, v2 reserves the exact
approved query/rule/configuration revision and destination scope; admin selection
alone does not authorize a write. Consumer checks current mapping/approval/value
concurrency before send, and never retargets a reserved attempt. Producer resolves
the unique authorized target and durably binds it and the query to operationId
before its writer executes; competing mapping changes cannot change that reserved
target. Exact replay returns its stored aggregate receipt without another write;
conflicting operationId reuse rejects without write. Never store plaintext
passwords or include them in logs/receipts. Possibly sent/claimed/Unknown attempts
are never automatically resent, cleared or replaced after configuration changes.
Existing settlement, both-destination ordering and aggregate outcome rules remain.
`MappingVersion` still means local mapping concurrency, not wire version.

Wire definitions: [request](contracts/password-sync/v2.0/contract.schema.json#/$defs/request)
and [response](contracts/password-sync/v2.0/contract.schema.json#/$defs/response).
[Synthetic examples](contracts/password-sync/v2.0/examples/) cover Found, NotFound,
Ambiguous, Unavailable, Unsupported, missing inputs and forbidden mixed shapes.
Their validity proves structure only; semantic, authority and live product checks
remain separate. queryRule is omitted in these unambiguous agreed-rule examples.

## Unversioned compatibility contract


Legacy Password Sync is one optional, unversioned password-write endpoint. It
is disabled by default and is independent of [Provider Proof Contract 1.0](PROVIDER_PROOF_CONTRACT.md)
and [Provider Metadata Contract 1.0](PROVIDER_METADATA_CONTRACT.md).
`MappingVersion` is local deployment state, not an API version, and neither
proof nor metadata enables this write boundary.

## Destination and ownership boundary

HybridIdP has exactly two password destinations:

1. Directory/AD, controlled by the existing directory settings.
2. One Legacy Password Sync API endpoint, controlled by
   `LegacyPasswordSync:Enabled` and its cohort gates.

The two destinations are configured independently:

| Directory | Legacy | Result for an eligible source |
|---|---|---|
| Off | On | 0 Directory writes, 1 Legacy dispatch |
| On | Off | 1 Directory write, 0 Legacy dispatches |
| On | On | 1 Directory write, then 1 Legacy dispatch |
| Off | Off | 0 Directory writes, 0 Legacy dispatches |

A legacy-only dispatch must derive from an authorized required-change attempt,
a consumed native proof challenge, or a consumed migration continuation. This
preserves the authorization and replay boundary without making the endpoint a
standalone reset authority. There is no endpoint list, public fan-out, target
discovery or topology negotiation.

HybridIdP dispatches only after an eligible completed source operation, an
exact current provider binding and mapping, unchanged account and security
stamps, and a durable single-send claim. The configured endpoint owns its
internal password write and aggregate commit result. The public boundary does
not own affiliation, personnel classification, identity linking, roles, groups,
OU placement or directory synchronization policy.

## HTTP exchange

Configure one complete endpoint URI. The established route is
`/api/password-sync`. HybridIdP sends `POST`,
`Content-Type: application/json`, and `X-Internal-Secret`. Redirects and
cookies are disabled. HTTPS is required unless
`AllowPrivateNetworkHttp=true` explicitly permits HTTP on a controlled private
network.

Request:

```json
{
  "operationId": "5be9fbab-c86f-4bc5-bc20-1cf7193554ae",
  "accountIdentity": "d76391a7-bef7-42b5-8105-5e67ca53ff7a",
  "password": "<new-password>"
}
```

Response:

```json
{
  "operationId": "5be9fbab-c86f-4bc5-bc20-1cf7193554ae",
  "outcome": "Success"
}
```

`operationId` must be a nonempty UUID. The receiving HTTP boundary rejects a
missing or all-zero identifier before its coordinator or password writers are
called. `accountIdentity` is an opaque,
case-sensitive account identity: HybridIdP does not parse it for affiliation or
route selection. The currently supported representation is the exact canonical
lowercase UUID `D` form, with no surrounding whitespace or control characters,
and a maximum length of 256 characters. The password must be nonempty.

The response must be HTTP `200` with media type `application/json`, no more
than 64 KiB, and contain exactly `operationId` and `outcome`. The operation must
match the request. `outcome` must be exactly one of `NoOp`, `Success`,
`PartialSuccess`, `Failed` or `CommitUnknown`. Target names, target counts,
target-result collections, internal identifiers, routing decisions, error
details and connection topology are not part of the public contract.

## Durable dispatch and settlement

The attempt is durably reserved and claimed before any external dispatch.
Persistence failure prevents dispatch while preserving known cleanup or
recovery eligibility. Completed source operations are deduplicated, and stale,
contradictory, ineligible or already claimed sources cannot dispatch.

Endpoint outcomes map to local attempt state as follows:

| Endpoint outcome | Local state |
|---|---|
| `Success` | `Succeeded` |
| `NoOp` or `Failed` | `Failed` |
| `PartialSuccess` or `CommitUnknown` | `Unknown` |

After dispatch may have occurred, timeout, cancellation observed by transport,
network failure, non-`200` status, wrong media type, oversized or malformed
body, operation mismatch, extra response members or an unsupported outcome are
also `Unknown`. An unknown or claimed state is a durable barrier: it is never
automatically resent, replaced or cleared because time elapsed, a later login
succeeded or a generic acknowledgement was received. Settlement requires a
separately authorized recovery procedure.

## Configuration and activation checklist

```text
LegacyPasswordSync__Enabled=false
LegacyPasswordSync__CompletedDirectoryRecoveryEnabled=false
LegacyPasswordSync__CompletedDirectoryRequiredChangeEnabled=false
LegacyPasswordSync__Stage2MigrationEnabled=false
LegacyPasswordSync__Endpoint=https://example.provider/api/password-sync
LegacyPasswordSync__SharedSecret=<PROVISION_THROUGH_SECRET_CONFIGURATION>
LegacyPasswordSync__AllowPrivateNetworkHttp=false
LegacyPasswordSync__Timeout=00:00:05
LegacyPasswordSync__TrustedProviderNamespace=example.provider
LegacyPasswordSync__ActiveMappingVersion=<DEPLOYMENT_MAPPING_VERSION>
```

Mappings are configured under `LegacyPasswordSync:Mappings` with `Enabled`,
`ProviderNamespace`, `StableSubject`, `AccountIdentity` and `MappingVersion`.
Enabling the endpoint requires at least one cohort, one protected endpoint and
caller secret, one exact trusted provider namespace, one active mapping version
and one unique enabled mapping. Source identities and account identities must
both be unique. Timeout must be greater than zero and no more than thirty
seconds.

The endpoint must authenticate the caller before reading the password,
durably deduplicate `operationId`, return only the aggregate response, and
report `CommitUnknown` whenever it cannot prove whether a write committed. It
must never log or persist the plaintext password or caller secret.

The SQL Server and PostgreSQL `Hidp13LegacyPasswordSyncAttempts` migrations
were rewritten before publication and verified offline for the
`LegacyPasswordSyncAttempts` model. No live or production migration was run.
Apply the appropriate migration only through the operator-controlled migration
workflow. Checked-in settings and offline tests do not establish deployment,
external interoperability or connected password-write evidence.
