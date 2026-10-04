# Synthetic RIV conformance cases

Baseline RIV-1.0-PLAN-20260930-A. All values are synthetic. These case envelopes
are test data, not API request shapes. Parse requestJson/responseJson as raw UTF-8
wire data; requestVariants and responseVariants intentionally include invalid JSON.
No secret belongs in a fixture. The canonical schema lives at
../../schemas/recovery-identity-verification.schema.json.

| Cases | Fixture coverage | Execution ownership |
| --- | --- | --- |
| C01-C07 | Verified, denial and unavailable source scenarios; absent birthday | Shared wire checks here; producer proves actual unique lookup, eligibility and missing-data handling. |
| C08-C09 | Missing/null/blank/control fields, excessive length/depth | Pure request semantic tests here; producer proves zero source calls. |
| C10 | 8193-byte request and wrong media type | Byte limit tested here; HTTP admission deferred to producer and consumer transport stage. |
| C11-C12 | Unsupported version/scheme, duplicate/unknown/case-sensitive fields | Pure rejection here; producer owns Unsupported versus Malformed classification and zero lookup. |
| C13-C14 | Auth/network rejection, source failure/disabled | Rejection/Unavailable envelope here; actual auth/admission/source behavior belongs to producer and HTTP stage. |
| C15-C16 | Mismatched binding/correlation, empty/HTML/null/unknown response | Strict consumer semantic rejection here. |
| C17 | Redirect | Pure status rejection here; no-follow/body-forwarding proof belongs to HTTP transport tests. |
| C18 | Trim and uppercase only | Consumer unchanged round trip here; producer comparisons supplied, actual normalization proof belongs to producer. |
| C19 | Cancellation | Test fake cancellation here; HTTP deadline/body-read cancellation remains transport-stage work. |
| C20 | 429 | Pure rejection here; producer proves rate limiting with no source call; transport stage proves no retry. |
| C21 | Repeated requestId | Fake invokes fresh response each time; producer proves fresh lookup and shared rate limit. |
| C22 | Non-success identity leak | Strict rejection here. |
| C23-C25 | Cross-context replay, concurrent sends, state changes | Consumer durable H3/H5 gate only; fixture assertions supplied, no DB coverage claimed here. |
| C26 | Scalar lengths and invalid surrogates | Pure serialization/parsing tests here, including invalid UTF-8 and binding scalar limits. |
| C27 | Incorrect status with Verified | Strict consumer envelope rejection here. |
| C28 | Browser supplied Verified/binding/recipient/scheme | Extra wire fields rejected here; actual browser/service authorization belongs to consumer H3/H5. |

consumerAcceptsEnvelope means only a structurally valid correlated response, not
permission to send mail or reset. consumerMayPrepareGrant applies only after all
local policy/account/destination checks; it never means this fixture issues a grant.
Producer-specific behavior and deferred transport/durable gates must be recorded
separately from pure test results.
