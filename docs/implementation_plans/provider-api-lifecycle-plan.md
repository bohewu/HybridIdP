# Provider API and lifecycle integration handoff

Date: 2026-10-03. Status: consumer implemented, default disabled; independent final verification passed and independent review passed after one side-effect ordering repair. Producer ownership is
undecided. [Lifecycle Status 1.0](../PROVIDER_LIFECYCLE_CONTRACT.md) and its
[schema/examples](../contracts/provider-lifecycle/v1.0/examples/README.md) are the
neutral contract definition for review. [Current capability inventory](../PROVIDER_API_CAPABILITIES.md)
preserves Proof, Metadata, Recovery and unversioned Password Sync independently.

This replaces no frozen contract. The read-only AuthProxy background handoff at
`C:\repos\auth-proxy\handoff\provider-api-lifecycle-plan.md` informed scope;
its proposed flags/fields/transition steps are background, not current settings
or implementation authorization. No neighboring repository must be present to
use this bundle, and AuthProxy is not assigned lifecycle/affiliation ownership.

## Integration sequence and gates

1. Review the neutral draft's account-state vocabulary, authoritative mapping,
   explicit times/finite maxAge, HTTP pairs, strict wire limits and discriminator.
   Resolve sourceAuthority/mappingVersion acceptance and actual snapshot meaning
   with the selected producer. A service credential is not source authority.
2. Separately authorize producer implementation in its owner's project. Prove
   exact immutable account lookup and authority for each approved namespace;
   missing mapping/source freshness is fail closed. API transport remains required
   for internal/co-located nodes; no shared DB, DLL or private SDK integration.
3. HybridIdP configuration, HTTP client, composed eligibility and covered
   checkpoints are implemented, default disabled. No producer, authority,
   cohort or clock assurance is selected; runtime enablement remains separate.
4. Each checkpoint performs a fresh lifecycle lookup and current local AND remote
   acceptance immediately before the protected action; no allow cache, fallback
   or durable lifecycle rewrite is implemented.
5. Use existing product tests and ordinary synthetic HTTP fixtures for focused
   implementation verification. Later real-producer read-only validation, small
   explicit binding cohorts, operator-approved enablement and rollback require
   source/mapping/admission evidence. Draft schema examples do not prove any gate.

Existing local cookie/new-token checks remain in force. Already-issued
self-contained JWTs can remain valid until expiry; immediate resource-server
invalidation is separate scope. Local eligibility, MFA, provisioning, claims,
consent and authorization remain HybridIdP decisions.

## Separate retirement transition

This read contract denies the queried Retired/Superseded account without mutating
local state. The synthetic 00015 -> R0001 example neither infers a Person link
from prefixes nor marks the whole Person Resigned. A successor reference grants
no linking, provisioning, dispatch or authentication authority.

A later protected transition must independently prove the old/new account mapping
and same Person, apply approved local retirement policy and expected versions,
atomically preserve correct per-account state/stamps, and explicitly finish
revocation using existing services. Repeated/recovered operations must not create
duplicates, change established links or re-enable the old account. Remote password
failure or lifecycle outages cannot trigger this transition. Rollback of a remote
check must not re-enable an account disabled by an explicit local transition.

## Review scenarios and open decisions

| Scenario | Required later result |
| --- | --- |
| Independent full URLs/hosts and co-located services | Call each configured capability URL; no shared-host/path assumption |
| Same literal URL, valid lifecycle selector or mixed signatures | Exact intended dispatch; collision rejects before any source operation |
| Remote Enabled, local User/Person denied | Reject at every covered checkpoint |
| Non-Found, Unknown, stale/future/expired, mismatched tuple/correlation/authority, outage | Reject covered operation; no persistent local mutation/fallback |
| Retired old account with successor; eligible Person and other account | Deny old account only; successor does not create a link or account |
| Existing unknown password write | Preserve single-send and durable CommitUnknown barrier |

Open decisions before enablement: selected producer/authoritative source;
documented managed mappings and actual snapshot semantics; approved local account
cohort and maxAge; operational host UTC clock trust; live interoperability/source
verification and rollout/rollback authorization. No producer implementation,
runtime activation, retirement transition, deployment or push has occurred.

Backlog: per-namespace/multiple lifecycle routing; in-process Local/Inner adapters;
persistent caching or event projection; automatic retirement import; immediate
JWT invalidation/introspection. These are not version-one dependencies.

## Delivered consumer configuration

The [disabled example](../examples/provider_lifecycle_disabled_config.json.example)
contains no endpoint, credential or account cohort. Settings are implementation
choices in `Infrastructure.Options.ProviderLifecycleOptions`, bound from
`ProviderLifecycle`; they do not change the neutral wire contract.

| Setting | Implemented meaning |
| --- | --- |
| Enabled | Defaults false; disabled checks make no lifecycle HTTP calls |
| Endpoint | Independent complete fixed HTTPS URL, without path appending, userinfo, query or fragment |
| SharedSecret | Nonempty header-safe secret from protected configuration; never commit it or expose it to browser state |
| TimeoutSeconds | Integer 1-10, default 5; monotonic deadline includes send, headers and complete bounded body consumption |
| MaxAgeSeconds | Integer 1-300, default 60; local freshness policy |
| RequiredAccounts | Nonempty explicit cohort when enabled; unique nonzero local ApplicationUser Guid per entry |

Each entry has `LocalAccountId`, exact `BindingKind` (`DirectoryBinding` or
`ExternalLogin`), `ProviderNamespace`, approved `SourceAuthority` and producer
`MappingVersion`. `ExternalLoginProvider` is required for ExternalLogin and
must be null/omitted for DirectoryBinding. Invalid enabled configuration fails
startup validation. Use the deployment's existing protected configuration and
restart after changes; this delivery executes no secret-setting commands.

Requiredness follows the local account ID independently of binding existence.
DirectoryBinding selects the account's existing stored binding by exact namespace.
ExternalLogin selects its existing Identity login by exact LoginProvider with the
explicitly approved namespace. The subject is read from the server-owned record,
never configured or browser supplied. Missing or ambiguous bindings deny required
accounts. Compare namespace, subject, provider, authority and mapping ordinally,
without normalization or guessed joins. Local binding record IDs and
security/concurrency stamps detect local changes; they are separate from producer
mappingVersion. Source/mapping approval is separate from endpoint admission.

Every checkpoint performs a fresh lookup and correlation/tuple validation, then
checks unchanged policy/binding/user stamps and current local eligibility.
At the same final UTC instant T, require Found Enabled AND local User/Person
eligibility, approved authority/mapping, effectiveFrom < effectiveUntil,
observedAt <= T, T - observedAt <= maxAge and effectiveFrom <= T < effectiveUntil,
with zero skew. User lockout and Person date checks use that same instant,
retaining existing inclusive UTC-day Person bounds. Disabled and uncovered
accounts preserve local behavior, including currently permitted no-Person
accounts; stricter Passkey Person rules remain.

`ProviderLifecycleClock` uses host TimeProvider and latches unhealthy on
unreadable, non-UTC, extreme or backward time until process replacement.
This denies required accounts only; disabled/uncovered behavior is unchanged.
The guard establishes no actual production clock trust.

## Delivered checkpoint coverage

`Core.Application.Interfaces.IAccountLifecycleEligibility` and
`Web.IdP.Services.ICurrentUserLifecycleEligibility` resolve to the same scoped
full `CurrentUserLifecycleEligibility` instance in production registration.
Each protected action invokes it afresh, including later actions after awaits
within the same request; no persistent or request allow cache is used.

| Checkpoint | Delivered integration |
| --- | --- |
| Password login | Normal and mandatory-MFA grace full-cookie branches |
| MFA | TOTP, chooser/recovery codes, Email OTP and setup skip/completion before full cookie |
| Passkey | Full sign-in, retaining existing stricter Person/security/MFA checks |
| External login | Full sign-in after existing coordinator and migration checks |
| Application cookie | Every continuation, composed with migration/current-state/security-stamp checks |
| Authorization/consent | New code/user authorization and session issuance |
| User token grants | Authorization-code, refresh, device, password and equivalent user issuance |
| Device approval | User approval and subsequent device-grant issuance |
| Account switch | Receiving account before existing MFA-state work and full cookie |
| Impersonation | Target and restored original account before their cookie issuance |
| Profile renewal | Cookie refresh after explicitly authorized external-login removal |
| Registration | Existing account/Person creation retained; full cookie gated |

Non-user client credentials behavior is unchanged. Denials retain existing
response conventions, including OpenIddict OAuth errors such as invalid_grant
where applicable, without lifecycle payload disclosure. Explicit registration
and link-management operations retain their own authority. Remote denial does
not durably change users, Persons, links, stamps or sessions, invoke a successor,
provision, retire or select another credential authority. Already-issued
self-contained JWTs may remain valid until expiry; immediate invalidation is not
implemented.

## Recorded synthetic verification and remaining limits

Task results record ordinary product tests with synthetic HTTP fixtures and
injected policy outcomes. Counts overlap across rounds and must not be summed
as unique overall tests.

| Round | Focused results |
| --- | --- |
| Task 1 consumer | 80 passed, 0 failed, 0 skipped |
| Task 2 policy/regression | 95 passed, 0 failed, 0 skipped |
| Task 3 checkpoints | 242 passed: Web 86, Application 139, Infrastructure 17; 0 failed/skipped |
| Pre-repair independent Infrastructure | 97 passed, 0 failed, 0 skipped |
| Pre-repair independent Web | 160 passed, 0 failed, 0 skipped |
| Pre-repair independent Application | 139 passed, 0 failed, 0 skipped |
| Post-repair AuthorizationServiceTests | 25 passed, 0 failed, 0 skipped |
| Post-repair AccountManagementServiceTests | 18 passed, 0 failed, 0 skipped |

Each implementation task recorded a successful Web.IdP build; task 3 also
compiled Tests.Infrastructure.IntegrationTests without connected test execution.
Independent final solution build completed with zero reported warnings/errors.
The first independent review found final-denial side effects: new session and
consent persistence, and switch sign-out. Focused regressions reproduced these
behaviors; one ordering repair passed the post-repair focused tests, and final
independent review accepted the delivery. An earlier CS0162 warning in
AuthorizationServiceTests and two pre-existing Infrastructure recovery analyzer
warnings remain recorded; the later build result does not establish that all
source is warning-free.

```powershell
dotnet test Tests.Infrastructure.UnitTests/Tests.Infrastructure.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ProviderLifecycle" --verbosity minimal
```

```powershell
dotnet test Tests.Web.IdP.UnitTests/Tests.Web.IdP.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~CurrentUserLifecycleEligibilityTests|FullyQualifiedName~ApplicationCookieCurrentStateValidatorTests|FullyQualifiedName~MigrationPageIssuanceGateTests' --verbosity minimal -nodeReuse:false
```

```powershell
dotnet test Tests.Web.IdP.UnitTests/Tests.Web.IdP.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~MigrationPageIssuanceGateTests|FullyQualifiedName~ApplicationCookieCurrentStateValidatorTests|FullyQualifiedName~ExternalSignInCoordinatorTests|FullyQualifiedName~PasskeyControllerTests|FullyQualifiedName~MfaSetupApiControllerEmailMfaTests|FullyQualifiedName~LoginMfaModelPasskeyTests|FullyQualifiedName~LoginModelTurnstileTests|FullyQualifiedName~LoginModelClientIdParsingTests|FullyQualifiedName~ProfileManagementControllerTests' --verbosity minimal -nodeReuse:false
```

```powershell
dotnet test Tests.Application.UnitTests/Tests.Application.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~TokenServiceTests|FullyQualifiedName~AuthorizationServiceTests|FullyQualifiedName~DeviceFlowServiceTests|FullyQualifiedName~ImpersonationServiceTests|FullyQualifiedName~UsersController' --verbosity minimal -nodeReuse:false
```

```powershell
dotnet test Tests.Infrastructure.UnitTests/Tests.Infrastructure.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~AccountManagementServiceTests' --verbosity minimal -nodeReuse:false
```

No live producer exchange, authoritative source read, production clock assurance,
live database/browser journey, full host activation or deployment verification
was performed. Checkpoint tests inject policy outcomes; policy tests establish
synthetic acceptance boundaries. OpenIddict error properties were asserted,
without HTTP TestServer serialization checks. The DI alias was reviewed in
source without connected host activation. Contract schema/examples alone prove
neither runtime enforcement nor producer/source truth. The bounded guard-fixture correction changes only the Password value in `request-mixed-contract.invalid.json` to an explicit placeholder; its member set and schema-invalid classification remain unchanged. This is the sole exception to the earlier example-byte hash baseline; wire/schema definitions are unchanged.

Exact task commands, checkpoint-to-test evidence and the task-3 operational-read
correction deviation are retained in the ignored local
`.pipeline-output/hybrid-lifecycle-consumer-20261003-a/pipeline/task-1-result.json`,
`task-2-result.json` and `task-3-result.json`. Independent final test/build
counts above are parent-reported pre-repair results; final independent review
passed after one side-effect ordering repair. The post-repair focused counts are
reported separately and are not summed with earlier rounds.
