# Targeted SQL Server physical cleanup

`cleanup-provider-users-sqlserver.sql` is an operator-specific, destructive reset
for the literal, case-sensitive `ncut.authproxy_` username prefix. It never uses
SQL `LIKE`; `_` is an ordinary character. Candidate selection includes all account
types and states, without inferring identity from a provider name or email.

1. Deploy the corrected account naming/deletion flow first. Confirm the database
   uses the current repository SQL Server migrations and take a verified,
   restorable backup. Stop all application/background writers and use a dedicated
   SQL connection; restart application instances after completion to clear caches.
2. Select the intended database explicitly. Run the unchanged SQL with
   `@Apply = 0`. Review the database name, separate User and Person candidate
   lists/counts, owned-record effects and blocking dependency rows. Protect output
   as account data. A Person qualifies only if exactly one account link existed
   before cleanup and that account is targeted. Every multi-account Person stays,
   including one whose accounts all match.
3. In a private copy of the SQL set `@Apply = 1`, `@ExpectedDatabase` to the exact
   selected database name, and both expected counts to the reviewed values.
   Execute the entire batch. A count mismatch, blocker or SQL error rolls back
   the entire operation. Rerun preview after any mismatch; counts are a guard,
   not a substitute for reviewing the selected identities in a maintenance window.
4. Retain the result securely. Restart application instances, verify the intended
   accounts are absent and unrelated accounts remain, then reopen traffic.

SQL Server Management Studio can execute this as one batch. With `sqlcmd`, use
`-I -b` (quoted identifiers and nonzero exit on SQL errors), an explicit `-d`, and
normal authenticated TLS. Supply credentials through the approved secure prompt
or secret mechanism, never inline in a shared command. Local disposable SQL Server
fixtures may need `-C`; do not copy that certificate-trust exception to production.

Physical deletion removes target users' Identity login bindings, role memberships,
claims, tokens, passkeys/MFA and recovery records, application-role assignments and
`LoginHistories` through the existing owned-record FKs. These histories cannot
remain attached to a physically deleted user. Independent `AuditEvents` remain
untouched. Target `UserSessions` and OpenIddict authorization/token rows remain
as revoked records, including tokens attached to target authorizations. Global
roles, clients and scopes and all non-target account data remain unchanged.
Already-issued self-contained JWTs can remain valid at resource servers until
expiry; SQL cleanup cannot recall those copies.

Some candidates cannot be safely reset by this script. It reports and refuses
the entire apply when local User/Person denial would be removed, retained directory
bindings/migration or directory/password-write records exist, inconsistent
cross-account references exist, or a candidate Person owns API resources, clients
or scopes. This preserves the terminal denial contract in `docs/SECURITY.md`
and avoids discarding durable uncertain-write evidence or changing resource
ownership. Candidates are never silently skipped or reclassified as non-targets.
Unexpected restrictive FKs also abort; their SQL error identifies the constraint.
Resolve such cases through a separately reviewed retention/authority/ownership
decision. Do not disable constraints, erase a blocker, or widen this script.

The script creates no persistent tables, indexes or schema changes. It requires
the current schema, not an older deployment with missing tables. It performs no
production operation until an operator explicitly applies their reviewed copy.
