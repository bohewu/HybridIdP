# Synthetic Lifecycle Status 1.0 examples

These are fake wire payloads for draft review, not real identities, captured
producer responses or evidence of implementation. `example.provider`, source
authority and revisions are synthetic. Account display labels 00015/R0001
are illustrative only; tuples are opaque and no prefix mapping is inferred.

[Contract](../../../../PROVIDER_LIFECYCLE_CONTRACT.md) defines transport and
semantics; [schema](../lifecycle-status.schema.json) validates response root or
request `#/$defs/request` using Draft 2020-12 and a date-time format checker.
The .invalid.json suffix means expected structural rejection. All .valid.json
files are structurally valid, including stale/future/mismatched evidence.
`semantic-cases.json` is a case-description document, not a wire message.

| File | Purpose |
| --- | --- |
| request.valid.json | Exact existing trusted tuple and request correlation |
| request-missing-version.invalid.json | Required explicit version absent |
| request-mixed-contract.invalid.json | Password/proof field forbidden; reject before source dispatch |
| response-enabled.valid.json | Found Enabled with approved evidence |
| response-retired.valid.json | Old account Retired; successor supplies no linking authority |
| response-not-found.valid.json | No account-state or identity evidence |
| response-stale.valid.json | Schema valid, semantic age failure |
| response-future.valid.json | Schema valid, future observation failure |
| response-tuple-mismatch.valid.json | Schema valid, exact requested tuple failure |
| response-found-missing-evidence.invalid.json | Found cannot omit freshness/source evidence |
| response-failure-with-state.invalid.json | Non-Found cannot carry a state, even Unknown |
| response-unknown-field.invalid.json | Undeclared affiliation authority rejected |

Semantic cases use T=2026-10-03T00:00:30.000Z, local maxAge=60s and approved
sourceAuthority/mappingVersion. They distinguish remote evidence validity from
the final local operation decision, cover inclusive age/start and exclusive end,
and show local denial despite fresh Enabled. Retired denies only the old account;
no case authorizes Person mutation, successor provisioning or actual login.

JSON Schema cannot detect duplicates after JSON parsing, streamed byte/depth
limits, HTTP/header pairing, correlation, mapping approval, Unicode scalar
validity in every runtime, time ordering, source truth, or current local policy.
Future implementations must apply the strict wire parser and semantic rules.
