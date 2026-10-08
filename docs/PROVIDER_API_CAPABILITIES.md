# Provider API capability inventory

Source review: local checkout, updated for Proof/Profile on 2026-10-08. Contracts retain their
own formats, configuration and admission rules. No deployment is inferred.
The [Lifecycle Status 1.0 draft](PROVIDER_LIFECYCLE_CONTRACT.md) adds a separate
read-only, default-disabled consumer; it does not change these delivered boundaries.

| Capability | Responsibility / no delegated authority | Complete endpoint and selection |
| --- | --- | --- |
| [Provider Proof 1.0](PROVIDER_PROOF_CONTRACT.md) | Account/password proof, assured provider identity/profile; no lifecycle Person mutation, affiliation or password-write authority | ProviderProof:Endpoint; Enabled defaults false; independent login validates trusted namespace/endpoint/secret; Stage 1 retains its explicit directory gate |
| [Provider Profile 1.0](PROVIDER_PROFILE_CONTRACT.md) | Approved display/custom-claim properties for an established exact identity; no credential, role, recovery or password-write authority | ProviderProfile:Sources:<source>:Endpoint; Enabled defaults false; each source configures namespace, secret and approved property types |
| [Provider Metadata 1.0](PROVIDER_METADATA_CONTRACT.md) | Email/trust evidence for an existing exact tuple; no credentials, affiliation, lifecycle, roles/groups or links | ProviderMetadataRefresh:Endpoint; existing route /api/authenticate/metadata; Enabled defaults false |
| [Recovery Identity Verification 1.0](RECOVERY_IDENTITY_VERIFICATION_CONTRACT.md) | Existing bound identity-identifier supplementary recovery evidence; no birthday, account discovery, password proof, OTP/reset or login authority | RecoveryIdentityVerification:Endpoint; suggested /api/recovery/verify-identity; Enabled defaults false; explicit RequireForLocalAccounts/RequireForDirectoryAccounts cohort |
| [Legacy Password Sync](PASSWORD_SYNC_CONTRACT.md), unversioned | Authorized durable single-send aggregate write command; no public targets, discovery, lifecycle or affiliation authority | LegacyPasswordSync:Endpoint; existing /api/password-sync; Enabled and three source cohort flags default false |

Every endpoint is a complete independently configured URL used without path
appending. Each capability independently configures SharedSecret and its deadline;
all use X-Internal-Secret. Different hosts/services or a shared service/literal URL
are permitted when producer dispatch is unambiguous. Configuring one endpoint
grants no other capability or source authority. Existing envelopes gain no shared
wrapper/discriminator. The new draft describes collision-safe lifecycle dispatch.

| Capability | Actual reader / HTTP acceptance | Transport and limits |
| --- | --- | --- |
| Proof | System.Text.Json Web reader/string enum converter and semantic TryValidate; required response version/outcome; any 2xx valid result; no independent response media-type check | Redirects disabled; platform cookie defaults; Timeout 5s, >0 through 30s; AllowPrivateNetworkHttp optional; response <=64 KiB; namespace <=200, subject/canonical account <=256 UTF-16 code units |
| Profile | Required version/tuple/property object, duplicate object members rejected, string/Boolean values only; exact tuple validation; only HTTP 200 application/json | Redirects disabled; timeout 5s, >0 through 30s; explicit private HTTP option; response <=64 KiB, depth 8, <=32 keys; approved-source snapshots and configurable confirmation age |
| Metadata | Permissive Web reader; case-insensitive names, ignored unknown fields, reader enum/date tolerance; required version and exact tuple validated; any 2xx parsed valid result, no independent media-type check | Redirects disabled; platform cookie defaults; default Timeout 5s, >0 through 30s covering send/body; AllowPrivateNetworkHttp optional; namespace <=200 / subject <=256 UTF-16 code units; response <=16 KiB |
| Recovery | Strict dedicated parser, case-sensitive names/enums, no extras/duplicates; 200 Verified/Denied, 400 Unsupported/Malformed, 503 Unavailable; application/json plus matching requestId and complete binding for Verified | HTTPS/certificate verification; no redirects/cookies/decompression; isolated loopback HTTP only in test seam; TimeoutSeconds 5, enabled range 1-10; request 8 KiB, response 4 KiB, depth 8; namespace 200 / subject 256 / evidence 128 Unicode code points |
| Password Sync | Strict aggregate response: exactly operationId/outcome, matching operation, canonical outcomes; only HTTP 200 application/json; accountIdentity exact canonical lowercase UUID D string <=256 characters | No redirects/cookies; HTTPS with optional explicit AllowPrivateNetworkHttp; default Timeout 5s, >0 through 30s when enabled; response <=64 KiB; durable reservation/dedup and CommitUnknown barrier |

Proof/Metadata registrations disable redirects and bound response bodies. Their
private-network HTTP switch does not verify DNS/network placement and does not
apply to the lifecycle draft.
Recovery and Password Sync strict parsing do not justify rewriting the older
readers. See the full contracts for detailed values and failures.

Metadata additionally maintains provider email evidence snapshots: failure
invalidates evidence; it cannot turn a failed credential into success or grant
recovery independently. Recovery uses the fixed identity-identifier scheme,
1-5 minute PrecheckLifetimeMinutes and local grant/OTP policy; Verified alone
cannot reset or sign in. Password Sync requires exact trusted namespace,
ActiveMappingVersion and unique enabled tuple-to-accountIdentity mappings plus
an authorized completed source operation. PartialSuccess, CommitUnknown and
possibly dispatched failures become a durable unknown barrier; never resend
automatically. MappingVersion is not a wire version.

Profile uses full snapshots and IdP-computed canonical hashes; producers need no
revision or update flag. Missing, failed, stale, ambiguous or unlinked source
data is omitted from new claims. Existing issued JWTs retain their normal expiry.
The initial five-minute confirmation age is configurable deployment policy,
not a source-freshness guarantee or an approved SLA.

Source anchors (read-only review):

- [Registrations](../Web.IdP/Extensions/ServiceCollectionExtensions.cs),
  [Proof options](../Infrastructure/Options/ProviderProofOptions.cs),
  [Profile options](../Infrastructure/Options/ProviderProfileOptions.cs),
  [Metadata options](../Infrastructure/Options/ProviderMetadataRefreshOptions.cs),
  [Recovery options](../Infrastructure/Options/RecoveryIdentityVerificationOptions.cs),
  [Password Sync options](../Infrastructure/Options/LegacyPasswordSyncOptions.cs).
- [Proof reader](../Infrastructure/Services/ProviderProofProvider.cs),
  [Profile transport](../Infrastructure/Services/ProviderProfileClient.cs),
  [Profile snapshots](../Infrastructure/Services/ProviderProfileService.cs),
  [Metadata reader](../Infrastructure/Services/ProviderMetadataRefreshService.cs),
  [Recovery transport](../Infrastructure/Services/RecoveryIdentityVerificationClient.cs),
  [Password Sync transport](../Infrastructure/Services/LegacyPasswordSyncContractServices.cs).

Implemented lifecycle selection has one independently configured full URL/auth/deadline,
HTTPS only and no redirects/cookies/automatic retry. It supplies source account
state for a trusted tuple, subject to strict freshness and local eligibility;
it supplies neither credential proof nor affiliation or mutation authority.
No SDK, shared business EF model, consumer reads of producer databases or producer
assembly loading is introduced. Multiple lifecycle endpoint routing, in-process
adapters, caching/projection and immediate JWT invalidation remain backlog.

Lifecycle settings, exact account selectors, strict consumer transport, checkpoint
coverage and synthetic verification limits are documented in the
[consumer integration handoff](implementation_plans/provider-api-lifecycle-plan.md#delivered-consumer-configuration).
No producer is selected or runtime enabled; independent final review passed after one side-effect ordering repair.
