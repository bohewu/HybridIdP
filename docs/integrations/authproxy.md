# Optional AuthProxy integration recipe

AuthProxy is one optional, deployment-local producer of the public HybridIdP
contracts. HybridIdP has no AuthProxy package, assembly, database, deployment
namespace or topology dependency. Configure independent producers using the
[core deployment ceilings and example](../DEPLOYMENT_GUIDE.md#neutral-non-login-query-configuration)
and [neutral query contract](../NEUTRAL_QUERY_CONTRACT.md). Authentication remains
separate from non-login queries; neither a query match nor this recipe creates
account-linking, recovery or password-write authority.

## Adapter configuration

For the AuthProxy adapter, use its six `/api/neutral/...`
URLs and global `Auth:InternalApiKey` as the consumer's shared secret. Its
neutral feature, per-rule enablement and source-read approvals default off.
It supports employeeId, accountName or both, optionally combined with
nationalIdHash; hash-only is unsupported. Full Profile retains its existing
projection approvals and readers. Lifecycle returns Unavailable because this
producer has no authoritative source. Password writes require its separate
destination, cohort and completed-source-operation approvals and existing
writer/receipt controls. Consult that producer's `docs/neutral-query-v2.md` for
its own configuration; HybridIdP has no producer assembly or storage dependency.

The [core configuration example](../DEPLOYMENT_GUIDE.md#deployment-ceilings-and-a-copyable-example)
retains the consumer's exact setting keys. Configure each complete URL explicitly;
the example paths do not imply automatic route construction. Producer approvals
remain separate from HybridIdP deployment ceilings and administrative selections.
Provision the consumer's `SharedSecret` privately from the approved producer
service key; never copy secret values into documentation.

Producer file references below are paths in the separately maintained AuthProxy
checkout, not HybridIdP files. Review that producer's current public documentation
and approved configuration before enabling an adapter; this recipe is not proof
of live source authority or interoperability.

## AuthProxy v2 Password Sync receipt prerequisite

Before enabling AuthProxy **v2 Password Sync writes**, the operator must review
the producer's public `docs/neutral-query-v2.md` (Password write boundary), take
a verified restorable backup of its approved receipt database, and apply that
producer checkout's existing idempotent
`deployment/sqlserver/password-sync-receipts.sql`. On an existing receipt store,
this adds nullable `binary(32)` `NeutralRequestScopeDigest` to the existing
`PasswordSyncOperations` table. Legacy rows retain null and legacy receipt
queries/inserts remain unchanged. No additional database, table or UUID mapping
store is introduced. The application does not execute this DDL or automatically
upgrade the receipt database; a missing column makes v2 Password Sync unavailable
before source lookup or writing. Read-only v2 capabilities do not require a
producer receipt database or this DDL. This producer prerequisite is separate
from HybridIdP EF migrations, which are unchanged for this round.

Unit/fixture and in-process HTTP checks are bounded implementation evidence.
Connected SQL, deployed TLS, real source authority and joint E2E require their
own observed evidence in a separately authorized environment.
