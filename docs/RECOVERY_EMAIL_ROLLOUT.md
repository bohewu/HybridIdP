# Recovery email selection rollout and rollback

Recovery identity precheck and recovery email selection are implemented with
OSS defaults disabled. Implementation, synthetic tests and generated migration
SQL do not establish deployment readiness. Complete independent security
review, producer/source mapping and the authorized environment gates before
enabling a cohort. This guide authorizes no migration, mail, provider request,
directory operation or deployment.

## Persisted selection and feature controls

`RecoveryEmailSelection.Enabled`, `SelfServiceEnabled` and
`TrustedDefaultFallbackEnabled` default to `false`.
`RecentStepUpMinutes` defaults to 5. Identity precheck has independent controls
described in the [integration guide](RECOVERY_IDENTITY_VERIFICATION_INTEGRATION.md).
Do not enable credential migration, directory writes, legacy password sync or
source acceptance as an incidental effect of enabling precheck.

An eligible verified custom address has precedence over a default. Pending
first enrollment leaves the qualified default effective; pending replacement
leaves the old custom effective. Verification atomically promotes the new
address, advances the account selection epoch and invalidates old recovery
authorization. Failed delivery, cancellation, expiry and concurrent replacement
do not silently erase the active destination. Custom delivery failure never
falls back to a default.

A default is usable only with approved provenance, freshness, lifecycle and
revocation evidence. A contact email or a failed custom lookup is not proof of
a default. Default OTP possession does not create a custom preference. Historical
verified records with unknown origin retain `LegacyUnknown` precedence; do not
invent user intent or erase source-bootstrap opt-out or administrator blocking.
Selection is account scoped, even when several accounts share a Person.

Authenticated status reports current mode and masked effective/default/pending
addresses. A valid `Legacy`, `UseDefault`, `UseCustom` or `Disabled` mode selects
the new status shape even when `enabled=false`; this is read-only persisted
intent. Only absence of that shape uses the legacy status API. A null effective
mask means unavailable under current policy, not permission to show a retained
historical address as active. Read-only GET does not create state or send mail.

Changing or using default requires current-account authorization, existing
MFA/hardware-key prerequisites, fresh authority-correct reauthentication,
CSRF/context binding and a short-lived server step-up grant. Pending mailbox
OTP never replaces the account step-up. Partial login, required password change
and credential-migration continuations cannot mutate selection. External
accounts currently use an existing passkey when the server returns hardware-only
reauthentication; do not substitute another authority's password. An account
without an eligible step-up method needs the existing enrollment or administrator
assistance path. Use default prepares a masked confirmation and rechecks that
exact eligible destination before switching. Cancel affects pending state only.

The existing `/api/account/recovery-email` routes retain `GET`, `POST change`,
`POST verify` and `DELETE`. Selection adds `POST reauthenticate`, `resend`,
`cancel`, `use-default/prepare` and `use-default`. Older change/revoke paths
cannot bypass pending/step-up/epoch rules once selection state exists. Revoke
and use-default have different intent; unavailable defaults cannot be reported
as a successful switch. Notifications retain durable bounded retry state and
contain no password, OTP or document evidence.

## Ordered gates

| Gate | Required decision/evidence |
| --- | --- |
| Schema | Back up both relevant provider deployments; verify additive create/upgrade and guarded downgrade on disposable fixtures with old addresses, challenges and tombstones. Generated SQL/model checks alone are insufficient. |
| Producer | Transfer the immutable contract/schema/fixtures and verify the real producer's admission, source semantics and correlated wire behavior. Approve source-to-subject mapping for each intended authority cohort. |
| Disabled consumer | Deploy compatible consumer binaries with all new flags off; validate startup/readiness and masked read-only persisted intent. Do not run old and new recovery writers together. |
| Custom selection | Enable selection/self-service only for an explicitly approved population with working step-up; preserve historical records and pending/active separation. |
| Trusted default | Independently approve source trust, freshness, revocation and eligibility before enabling `TrustedDefaultFallbackEnabled`; confirm the mailbox can be accessed independently of the forgotten credential. |
| Identity cohort | Enable identity verification only for the approved Local/Directory authority scope with complete durable provider bindings. Required missing bindings fail closed. |
| Synthetic journey | Run isolated contract, state, HTTP and desktop/mobile UI cases using fake services and disposable state. Record remaining provider and browser gaps honestly. |
| Authorized live pilot | Obtain separate authorization for controlled real provider/mail/database/directory checks and review the results before broader rollout. |

All gates are independent. Do not infer a producer implementation or cohort
decision from consumer synthetic success. Accepted disposable SQL Server 2019
LocalDB 15.0.4382.1 and PostgreSQL 17.11 execution verifies create, historical
upgrade, safe Down and the unsafe Down guard using synthetic data. SQL Server
passed 5/5 cases and PostgreSQL passed 1/1 lifecycle case. Current Vue component
tests passed 40/40 and the frontend build passed with existing SignalR annotation
and chunk-size warnings. Rendered desktop/mobile Vue keyboard, focus-return,
read-only, empty and hardware-dialog states passed with seven opened screenshots.
The full synthetic Razor journey passed 1/1, including password confirmation
validation, server mismatch rejection, reset success and desktop/mobile localized
states, with eight opened screenshots. This local evidence does not establish
production database readiness. Independent security review is a separate required
delivery gate; record its outcome in the delivery handoff. Real producer/source
mapping, mail, directory and live production gates remain unverified. These
results authorize no infrastructure startup or rollout.

## Rollback and schema downgrade

Stop new precheck and profile mutations first and drain recovery writers.
Disable rollout switches through deployment configuration and restart compatible
instances. Preserve the effective custom/default/disabled preference, epoch,
revocation tombstones, historical addresses, pending audit and notification
records. Disabling flags must not reinterpret a retained custom row as the
effective destination or permit old mutation routes to bypass persisted intent.
Keep the additive schema and use a compatible binary for operational rollback.

An old binary does not understand preference/epoch state. Never mix it with
new recovery writers; do not assume that image rollback or restoring an old
database backup can preserve changes made after that backup. Schema downgrade
requires an explicit archival and compatibility decision, including treatment
of default-backed challenges that have no local recovery-email FK.

Both `AddRecoveryEmailSelectionState` Down migrations fail before destructive
operations if recovery selection state has been used: preference, pending,
grant, throttle, step-up or notification rows; selection-bound challenges or
approvals; or non-`LegacyUnknown` provenance. Do not delete rows, reset provenance
or manually clear epochs merely to defeat that guard. A downgrade must preserve
or deliberately reconcile those records under a separately approved data plan.
The guard and generated SQL have offline coverage. Executed safe Down and the
unsafe Down guard are verified on disposable SQL Server 2019 LocalDB
15.0.4382.1 and PostgreSQL 17.11 using synthetic data. These results do not
authorize production schema downgrade.
Follow the existing
[operator-controlled migration procedure](DATABASE_CONFIGURATION.md#production-operator-controlled-schema-migration),
which treats schema and application rollback as separate operations.
