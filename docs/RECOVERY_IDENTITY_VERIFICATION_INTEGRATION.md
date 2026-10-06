# Recovery identity verification integration

The consumer implements the fixed `RIV-1.0-PLAN-20260930-A` boundary behind
disabled deployment switches. This guide is an implementation handoff, not
producer acceptance or deployment readiness. Independent security review is
a separate delivery gate; local implementation and synthetic tests cannot
replace it.

## Canonical bundle

Transfer these public files together to the producer implementer:

- [Contract 1.0](RECOVERY_IDENTITY_VERIFICATION_CONTRACT.md).
- [JSON Schema](schemas/recovery-identity-verification.schema.json), UTF-8/LF
  with a terminal LF; SHA-256
  `2b9336d9ebda44098a706ad7fecd8af7239b5e0ae32c603d1a4cef8f843d47f1`.
- [Synthetic C01-C28 fixtures and ownership](examples/recovery-identity-verification/README.md).

The contract baseline retains appendix SHA-256
`27ab73f37bf595d911d89c82b7d32ace53cc86192d120d905d2f001f35d5166f`.
Fixtures are case envelopes; send only their `requestJson` wire payloads.
Never insert a service secret into fixtures. Changes to fields, normalization,
limits, status/outcome pairing or binding require `CONTRACT_CHANGE_REQUIRED`
and agreement on the same baseline before either implementation proceeds.

## Producer responsibilities

Expose the protected route on the existing service. The consumer uses the
complete configured HTTPS endpoint, verifies its certificate, supplies
`X-Internal-Secret`, does not follow redirects and makes no automatic retry.
The existing network admission remains mandatory. Authentication/admission
must precede source lookup. Bound request size to 8 KiB and JSON depth to 8;
reject duplicate/unknown properties and malformed Unicode. Contract responses
are JSON, at most 4 KiB, with `Cache-Control: no-store`.

Resolve exactly the supplied opaque provider namespace and stable subject,
using the producer's approved mapping and source eligibility rules. Do not
search by submitted document value or try alternative sources based on its
format. Compare document values using `Trim().ToUpperInvariant()` only;
retain interior spaces and hyphens. Birthday is neither read nor required by
this scheme. Missing required data, ambiguous mapping and unavailable source
fail closed. No response exposes an email, document value/hash, source detail
or per-field mismatch. Only `Verified` carries the exact binding tuple and
current request ID; non-success has no binding.

The producer must independently prove unique lookup, eligibility, missing-data
handling, zero source calls after admission/shape rejection, bounded source
timeouts, shared rate limiting and fresh verification on repeated request IDs.
A synthetic producer that emits fixture envelopes proves none of those source
behaviors. Mapping every intended consumer authority cohort to an available
producer subject remains a required operator decision.

## Consumer behavior and configuration

`RecoveryIdentityVerification.Enabled`, `RequireForLocalAccounts` and
`RequireForDirectoryAccounts` default to `false`. An enabled configuration
must select an explicit authority cohort, a complete HTTPS `Endpoint`, a
secret supplied through secure configuration and scheme `identity-identifier`.
`TimeoutSeconds` defaults to 5 (range 1-10), covering headers and the entire
body. `PrecheckLifetimeMinutes` defaults to 5 (range 1-5). Label/help resource
keys change encoded presentation only, never verification or source routing.

Native routing, deployment ceiling, account eligibility, trusted source
settings and existing recovery policy still apply. Missing binding in a
required cohort does not downgrade to a local Person-field verifier. An
explicitly exempt local-only account retains only its already-authorized
native route. An incomplete directory migration is not a local-only account.

The browser submits an account identifier and raw identity evidence. The
consumer obtains the provider tuple from the trusted binding. It accepts only
HTTP 200, strictly validated correlated JSON `Verified`, and the exact tuple.
The evidence is cleared from the form and never retained in durable grants.
The resulting masked address is an intentional limited disclosure after
verification. All earlier failures use the common anonymous failure surface.

Prepare does not send mail or authenticate the user. The user explicitly
selects Send code. A durable, context-bound grant reserves at most one OTP
challenge; failed delivery leaves it unredeemable without changing recipient.
OTP and final reset independently recheck current destination, selection
epoch, security state, purpose and context. A producer result or browser-posted
`Verified`, recipient, scheme or binding cannot reset a password, create a
session or replace OTP. Existing local-password and directory uncertain-write
boundaries remain authoritative.

## Offline evidence and remaining interoperability gates

The existing contract tests cover C01-C28 wire semantics; client tests cover
transport bounds. `RecoveryIdentityVerificationHttpTests` additionally runs
the actual strict client against a separate synthetic HTTP process for
C01-C07, C13-C17, C20, C22, C27 and response variants, with unchanged evidence,
fresh repeated exchanges (C18/C21), cancellation/body deadline (C19), no
redirect forwarding and no retry. It uses the client's existing test-only
loopback seam. HTTP loopback is not a deployable configuration.

C08-C12/C26/C28 malformed wire cases remain covered by strict parser/client
tests. C23-C25 are consumer durable ceremony tests, including cross-context
replay, concurrent reservations and state invalidation. Real Razor system
tests use fake precheck/proof/reset boundaries; those prove page state and
CSRF handling, not real credential writes or mail delivery.

Real producer conformance, deployed certificate/admission behavior, approved
source mapping, cohort completeness, live delivery, AD writes and production
schema execution remain separately authorized gates. See
[rollout and rollback](RECOVERY_EMAIL_ROLLOUT.md) and
[offline test commands](TESTING.md#recovery-identity-verification-and-email-selection-offline).
