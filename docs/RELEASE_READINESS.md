# Release readiness and upgrade notes

Prepared on 2026-10-08 for the changes after 1.5.0. This is a release-preparation
record, not a version announcement or a production acceptance report.

## Source baseline

| Item | Verified value |
| --- | --- |
| Latest published release | 1.5.0, published 2026-09-21 |
| Comparison start | `v1.5.0`, commit `63d75192560a75a5bf136a4b81eeb1cf680247e5` |
| Main after PR #9 | `3c3823aedb8d45dc5b330e2e084893b30f123d93` |
| Dev preparation baseline | `3174e6060815c9317d63993d10008d0ae7957166` |
| Source-tree comparison | Main and dev trees were identical at the preparation baseline; the local dev worktree now contains unpublished provider/profile changes |

The release scope is the entire change since 1.5.0, including earlier merged
work, rather than only PR #9. The [Unreleased changelog](./CHANGELOG.md) summarizes
that scope. No new version, tag, release image, production migration or deployment
was performed during this preparation. Dependency-bot PRs remain separate work.

## Client and account impact

The OIDC protocol remains available, but this upgrade includes policy changes.
Do not treat it as a guarantee that every existing client requires no preparation.

| Area | Upgrade behavior | Operator action |
| --- | --- | --- |
| IdP cookies | Cookies without the current assurance marker are rejected at their next validation; users sign in again | Plan for reauthentication and deploy consistent validation across all IdP instances sharing cookies |
| Already-issued tokens | The cookie marker does not itself revoke existing self-contained access tokens | Account for their normal expiry and the existing revocation model |
| Delegated client management | Existing ownership rows have no immutable application binding and grant no delegated access | Verify ownership independently and bind the actual application key; leave ambiguous rows unbound |
| API scopes | Existing client permissions and end-user consent do not replace resource-owner/Admin approval | Record exact client/scope/resource approvals or deliberately configure the resource as open for usage |
| Consent | Explicit/Systematic clients require current interactive approval; External clients require a permanent grant | Test each affected client's consent policy, including `prompt=none` and `consent_required` |
| Local password age | With expiration enabled, a successful local password proof with unknown change age requires a password change; password grant is denied until then | Prepare the change-password journey; do not invent historical password-change dates |
| Account security | Sensitive factor removal, external linking and resets enforce current proof/authority; MFA enrollment alone does not establish performed MFA | Exercise the affected account-management journeys |
| Passkeys | The new nullable disable timestamp does not automatically retire existing credentials | Any bulk retirement is a separate approved operation after verifying fallback login |

API catalog visibility and usage permission are independent. Showing a resource
in the catalog does not grant its scopes. A restricted resource needs a
server-recorded approval by its owner or a full IdP administrator; changing
ownership or scope mapping can require new approval. Missing approval can produce
machine-readable `invalid_scope` during authorization-code, refresh, device,
client-credentials or password issuance. Include custom/API-scope clients in
canary checks; an identity-only sample login does not prove their compatibility.

See [OAuth flows](./OAUTH_FLOWS.md), [security expectations](./SECURITY.md),
[permissions](./PERMISSION_SYSTEM.md) and
[ownership migration guidance](./DATABASE_CONFIGURATION.md).

## Schema changes after 1.5.0

Apply only the migration assembly for the deployment's selected database provider.
These changes include indexes and nullability changes as well as new fields/tables.

| Migration | SQL Server ID | PostgreSQL ID | Deployment consideration |
| --- | --- | --- | --- |
| `AddRecoveryEmailSelectionState` | `20260930095054` | `20260930095106` | Adds selection, proof/epoch and throttle state; rollback is guarded once the feature has persisted state |
| `BindClientOwnershipToApplication` | `20261004144533` | `20261004144554` | Existing rows remain unbound; changes ownership indexes |
| `AddPasskeyDisabledAtUtc` | `20261005001707` | `20261005001720` | Existing null timestamps remain active |
| `AddApiResourceUsagePolicy` | `20261006040034` | `20261006040047` | Existing resources default to catalog-hidden and restricted usage |
| `AddProviderProfilesAndClaimConditions` | `20261008032457` | `20261008032522` | Adds nullable source snapshots and claim-rule fields; drain Profile writers before an older binary or schema rollback |

Do not backfill ownership by joining only the current `ClientId`: renames and
identifier reuse can assign another person's application. An administrator must
verify the immutable application key and Person using independent creation/audit
evidence. See [database configuration](./DATABASE_CONFIGURATION.md) for migration
and rollback constraints.

No account-specific retirement SQL, credentials or database extracts are included
in this release record. The passkey migration does not execute a retirement script.

## Configuration review

Preserve the deployment's existing configuration unless its operator explicitly
selects a change. New defaults do not rewrite an existing environment file.

| Setting | Default / requirement |
| --- | --- |
| `RecoveryIdentityVerification__Enabled` | `false` |
| `RecoveryEmailSelection__Enabled`, `SelfServiceEnabled`, `TrustedDefaultFallbackEnabled` | `false` within the recovery-email selection section |
| `ProviderLifecycle__Enabled` | `false` |
| `DirectoryIntegration__Enabled`, `DirectoryIntegration__AuthenticationEnabled`, `CredentialMigration__Enabled` | `false`; enabling a directory stage is a separate deployment decision |
| `RecoveryThrottle__HashKey` | At least 32 characters when identity verification or email selection is enabled; use a random protected key shared by all instances |
| `ForgotPasswordRecovery__PrecheckHintsEnabled` | `true`; `false` uses a uniform public result without eligibility or masked-address hints |
| `ProviderProof__Enabled` | `false`; independent API login requires an explicit trusted namespace, endpoint and service secret |
| `ProviderProof__AllowPrivateNetworkHttp` | `false`; HTTPS required unless explicitly allowed for a private deployment |
| `ProviderProfile__Enabled` | `false`; each source has its own full endpoint, namespace, secret and approved properties |
| `ProviderProfile__MaximumAge` | Initial default `00:05:00`; select deployment freshness policy explicitly; zero refreshes each issuance |
| `EmailSettings__SmtpRequireTls` | `false`; mandatory STARTTLS when enabled without implicit SSL |
| `EmailSettings__SmtpValidateServerCertificate` | `true`; any relaxation is an explicit deployment choice |
| `DatabaseMigration__ApplyOnStartup` | Keep `false` in production; run schema migration explicitly |

Disabling recovery hints changes the public precheck/continuation projection, not
the server-side proof and throttle requirements. It is not a claim of exact timing
equality. Precheck does not send recovery mail.

The optional bottom notice accepts text or a localized resource key, for example:

```dotenv
ForgotPasswordRecovery__BottomNotice=@Recovery.Guidance.Bottom
ForgotPasswordRecovery__BottomNoticeType=info
```

Outside Development/Test, provide the configured signing and encryption PFX
material required at startup. Operator-managed self-signed material is supported;
an internal CA is not required by this release. Verify the actual upstream and
SMTP certificate trust if enabling transport requirements. Keep optional metadata,
password-sync, directory and lifecycle activation within their separate contracts.

Review [deployment guidance](./DEPLOYMENT_GUIDE.md) for secrets, trusted proxy
IPs/CIDRs and internal-only readiness checks. `/health` is shallow;
`/health/ready` checks deployment readiness.

## Upgrade procedure

1. Inventory the actual running image/digest, database provider and applied
   migration IDs. Record configuration, signing/encryption and data-protection
   material, client owners and required API-scope approvals. A Git tag alone does
   not identify the image currently deployed.
2. Take and verify a restorable database backup for that provider. Protect the
   configuration and certificate backup separately. `deployment/backup.sh` backs
   configuration, certificates, Nginx files and logs; it does **not** back up the
   SQL Server or PostgreSQL database.
3. Select an approved published image containing the intended source revision,
   pin its digest, and retain the previous approved image and configuration.
   Keep `DatabaseMigration__ApplyOnStartup=false`. Use the deployment's existing
   Compose mode and required override options for both commands below; do not
   switch database engines or reset volumes as part of this update.
4. Drain incompatible writers where required, run schema-only migration, and
   deploy that same unchanged image. Replace every placeholder before running:

   ```bash
   cd deployment
   release_image='ghcr.io/<owner>/hybrididp-idp-service@sha256:<approved-64-hex-digest>'
   compose_file='<existing-mode-compose-file>'
   ./migrate-db.sh --confirm-backup --source ghcr --image "$release_image" --env-file .env --compose "$compose_file"
   ./deploy-idp.sh --source ghcr --image "$release_image" --env-file .env --compose "$compose_file"
   ```

   Migration performs schema changes only: it does not seed, expose HTTP or run
   provider/directory workflows. A failed migration must be resolved before
   deployment. Normal startup with pending migrations fails before readiness.
5. Confirm readiness and discovery, and complete the verified ownership bindings
   and API-scope approvals. Roll out consistent cookie-validation behavior to
   all IdP instances. Keep recovery/directory/lifecycle features at their existing
   settings until their independent activation is approved.
6. Run deployment canaries for old-cookie reauthentication, ordinary login,
   UserInfo, refresh, logout and each affected client's consent/API scopes.
   Check both expected rejection and approved issuance. For a deployment using
   the current AuthProxy producer, complete the connected verification below
   before claiming connected login acceptance.

The [deployment guide](./DEPLOYMENT_GUIDE.md) remains the command reference.
These steps are operator instructions; they were not executed against production.

## Rollback limits

- Application image rollback and database schema rollback are separate actions.
  Do not automatically run EF Core `Down` when reverting an image.
- Disable/drain new feature writers before considering an older binary. Preserve
  recovery proofs, selection preferences, epochs, tombstones and grants. An old
  binary does not understand all new recovery state; mixing writers can violate
  those invariants.
- Recovery-selection `Down` refuses rollback once relevant state has been used.
  Do not delete proof/preference records or reset provenance to bypass that guard.
- Ownership `Down` cannot safely reconstruct the former unique `ClientId` index
  after identifier reuse. An older binary also restores older security behavior;
  evaluate compatibility before choosing it as a rollback target.
- Use a verified database restore only under an explicit recovery plan that
  accounts for data written after the backup. Retain the matching configuration,
  certificates and data-protection material.

## Verification evidence and limits

Historical local evidence below is reused with its original scope. No full test
suite or browser journey was rerun for these documentation-only changes.

| Evidence | Result | Limit |
| --- | --- | --- |
| Main CodeQL on `3c3823ae` | [Successful run](https://github.com/bohewu/HybridIdP/actions/runs/37714285952) | Scanner success is not complete security or deployment acceptance |
| Previous solution build and scoped test evidence for PR #9 | Build: 0 warnings/errors; 579 unique backend cases across 603 targeted executions; frontend: 13 tests and Vite build passed | Recorded pre-publication evidence, not a full-suite rerun or a new execution on the merged main binary |
| 2026-10-07 local headed browser | 13 passed, 0 failed/skipped; registration, password age/history, email MFA, OIDC callback, cookie assurance, UserInfo, refresh and logout | Isolated LocalDB/Mailpit; report base `ca08b443` plus then-uncommitted remediation; no connected upstream or production acceptance |
| 2026-10-06 policy browser follow-ups | Hint on/off for eligible/absent/ineligible accounts, Mailpit receipt, mandatory-TLS rejection, TOTP and virtual WebAuthn journeys | Local fixtures and virtual authenticator; no physical passkey or production SMTP trust validation |
| Latest local security diff review | No reportable findings in 43 changed files from `ca08b443` to `3174e606` | Does not cover the entire release diff since 1.5.0 |
| 2026-10-08 real AuthProxy authentication | Existing Legacy SSO source returned HTTP 200, Proof 1.0 `Authenticated` | Producer-only success; full AuthProxy-to-IdP-to-TestClient flow has not passed |

The connected check used the producer at revision
`57df6c82285356f0e2cc0f49a16aa2f2ed2cc362` with existing protected test credentials.
Its loopback test process was stopped and its port cleared. No database or browser
was created, no directory operation was performed, and neither repository's
persistent connection settings were changed. No credentials, identities or
tokens are recorded here.

## Upstream contract transition and connected verification

Observed on 2026-10-08: the real AuthProxy endpoint accepts the legacy
`username`/`password` request shape, but returns typed Provider Proof Contract 1.0
JSON with `Outcome=Authenticated` and no legacy `authenticated` field.
The previous `LegacyAuthService` required that legacy boolean and could not
accept this response. This incompatibility was established by the observed wire
shape and consumer source; it was not a completed browser failure demonstration.

The local dev implementation now supports independent Proof 1.0 login without
enabling Stage 1 directory integration. It retires the flat reader/configuration,
adds optional Profile 1.0 retrieval and supports bounded conditional claims.
Historical links require operator-verified transition; never infer equivalence
from a mutable login name or email.

The initial preparation kept settings unchanged and listed the gap as a pending
decision. The subsequent direction agreed on 2026-10-08 is to standardize upstream
API login on Provider Proof 1.0 independently of directory integration, add an
optional Profile contract for extra properties, and support simple conditional
claim mapping. See the [unified provider/profile/claims design](./design_specs/provider-contract-profile-claims.md).

The latest local backend unit run passed 3,074 cases. Headed Sample TestClient
verification passed login, consent, callback, UserInfo and rotated refresh against
a generic Proof/Profile fixture with directory integration disabled. A rule-only
change recomputed the Boolean at refresh. SQL Server migrations were applied only
to an isolated test database; PostgreSQL migrations were generated, not deployed.

These changes are unpublished. AuthProxy's Profile producer is not implemented
by this work, and the real AuthProxy-to-IdP-to-Web-TestClient journey still needs
verification before connected deployment acceptance. Select the source allowlist,
independent endpoints/secrets and Profile freshness policy before rollout.
This upstream integration change is separate from downstream OIDC compatibility.

See [authentication integration](./AUTHENTICATION_INTEGRATION.md),
[Provider Proof Contract 1.0](./PROVIDER_PROOF_CONTRACT.md) and
[sample verification flows](./TESTING.md).
