# Neutral-query 2.0 schema and example bundle

The [common contract](../../../NEUTRAL_QUERY_CONTRACT.md) defines the semantics.
`common.schema.json` contains shared definitions, not a standalone endpoint
acceptance schema. Each capability's `v2.0/contract.schema.json` validates a
response at its root and a request at `#/$defs/request`. The six capability
contract documents describe result fields and retained authority boundaries.

Each capability's examples directory contains endpoint payloads. `*.valid.json`
means structurally valid; `*.invalid.json` must fail its request schema. Missing
inputs fail locally without dispatch; mixed legacy/v2 shapes fail admission.
HTTP status is separate: Found/NotFound/Ambiguous use 200, Unavailable 503,
Unsupported 400. RIV Ambiguous uses 503. A Found RIV Denied or a Found Password
Sync CommitUnknown is not success for the protected operation. Wrong status,
source, type, version, requestId or operationId must fail consumer correlation
even when the body alone validates. Unknown members and result-on-failure are
forbidden in v2.

`query.examples.json` is a named collection of query objects, not an endpoint
request. Validate each value against common `#/$defs/query`. It covers all seven
possible nonempty sets and an optional queryRule. A deployment enables only the
agreed supported subset; these examples do not approve a data source/rule.
The digest is SHA256 of the synthetic text TEST-ID-0001 under PidHasher's
normalization. It demonstrates a digest, never a raw identifier or verification
credential. The source must independently approve the storage algorithm.

`legacy-requests.examples.json` is a named collection of unchanged compatibility
request shapes, not a shared wrapper accepted at an endpoint. Select the payload
for one capability only. Existing 1.0/unversioned documents and schemas remain
normative for those modes. Lifecycle additionally needs its original 1.0 header;
none of these legacy payloads uses the v2 header. Do not add query to a legacy
payload or infer fallback from source failures.

The lifecycle Found example describes synthetic independent authority and a
historical observation interval. It is not current authorization or evidence
that any actual producer has lifecycle authority. A producer without independently
approved authority returns the Unavailable example. No sample enables runtime,
creates a login/account link, approves recovery or authorizes password writes.

Schema validation cannot check duplicate JSON names, valid trusted inputs,
normalization/comparison semantics, unique source matching, source authority,
freshness, configuration mutation, replay or transactional write concurrency.
Use the existing capability product tests for those obligations. This bundle
adds no validator, test harness, discovery service or production dependency.
