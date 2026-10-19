/*
  One-time, operator-specific physical cleanup; SQL Server, current HybridIdP schema.
  Read cleanup-provider-users-sqlserver.md first. Run on a dedicated connection,
  with application/background writers stopped and a verified restorable backup.
  PREVIEW is the default. No provider/type/active-state filter changes selection.
  Literal, case-sensitive prefix: ncut.authproxy_ (underscore is NOT a wildcard).
  Edit ONLY the four confirmation values below after reviewing PREVIEW output.
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;

DECLARE @Apply bit = 0;
DECLARE @ExpectedDatabase sysname = NULL;
DECLARE @ExpectedUsers int = NULL;
DECLARE @ExpectedPersons int = NULL;

DECLARE @Prefix nvarchar(256) = N'ncut.authproxy_';
DECLARE @Now datetime2 = SYSUTCDATETIME();
IF @@TRANCOUNT <> 0 THROW 51000, 'Use a dedicated connection without an existing transaction.', 1;
IF @Apply = 1 AND (@ExpectedDatabase IS NULL OR @ExpectedDatabase <> DB_NAME()
    OR @ExpectedUsers IS NULL OR @ExpectedUsers < 0
    OR @ExpectedPersons IS NULL OR @ExpectedPersons < 0)
    THROW 51001, 'Apply requires this database name and both reviewed nonnegative candidate counts.', 1;

DECLARE @Users TABLE (Id uniqueidentifier PRIMARY KEY, UserName nvarchar(256), PersonId uniqueidentifier);
DECLARE @Persons TABLE (Id uniqueidentifier PRIMARY KEY, SoleUserId uniqueidentifier);
DECLARE @Blockers TABLE (Kind nvarchar(80), EntityId uniqueidentifier, Reason nvarchar(400));
DECLARE @Authorizations TABLE (Id uniqueidentifier PRIMARY KEY);

-- SERIALIZABLE keeps selection, pre-cleanup link counts, dependency checks and
-- deletion in one consistent transaction. The maintenance window also prevents
-- application caches or in-flight external operations from racing this SQL.
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRY
    BEGIN TRANSACTION;

    INSERT @Users (Id, UserName, PersonId)
    SELECT Id, UserName, PersonId
    FROM dbo.AspNetUsers WITH (UPDLOCK, HOLDLOCK)
    WHERE LEFT(UserName, LEN(@Prefix)) COLLATE Latin1_General_100_BIN2
        = @Prefix COLLATE Latin1_General_100_BIN2;

    -- Count ALL links before deleting any User, including inactive/deleted users.
    INSERT @Persons (Id, SoleUserId)
    SELECT p.Id, u.Id
    FROM dbo.Persons p WITH (UPDLOCK, HOLDLOCK)
    JOIN @Users u ON u.PersonId = p.Id
    WHERE (SELECT COUNT_BIG(*) FROM dbo.AspNetUsers a WITH (UPDLOCK, HOLDLOCK)
           WHERE a.PersonId = p.Id) = 1;

    INSERT @Blockers
    SELECT N'AccountDenial', a.Id, N'Local inactive/deleted/locked or Person lifecycle denial must not be removed by physical cleanup.'
    FROM dbo.AspNetUsers a JOIN @Users u ON u.Id = a.Id
    LEFT JOIN dbo.Persons p ON p.Id = a.PersonId
    WHERE a.IsDeleted = 1 OR a.IsActive = 0 OR a.LockoutEnd > @Now
       OR p.IsDeleted = 1 OR p.Status <> 1 OR CONVERT(date, p.StartDate) > CONVERT(date, @Now) OR CONVERT(date, p.EndDate) < CONVERT(date, @Now);

    INSERT @Blockers
    SELECT N'DirectoryAuthority', b.LocalAccountId, N'Retained directory binding; requires a separate authority/retention decision.'
    FROM dbo.ProviderSubjectDirectoryBindings b JOIN @Users u ON u.Id = b.LocalAccountId
    UNION
    SELECT N'MigrationAuthority', m.LocalAccountId, N'Retained credential migration state; do not reset durable authority.'
    FROM dbo.CredentialMigrationStates m JOIN @Users u ON u.Id = m.LocalAccountId
    UNION
    SELECT N'DirectoryAttempt', a.LocalAccountId, N'Retained directory recovery/uncertain-write evidence.'
    FROM dbo.NativeDirectoryRecoveryAttempts a JOIN @Users u ON u.Id = a.LocalAccountId
    UNION
    SELECT N'PasswordSyncAttempt', a.LocalAccountId, N'Retained password synchronization/uncertain-write evidence.'
    FROM dbo.LegacyPasswordSyncAttempts a JOIN @Users u ON u.Id = a.LocalAccountId;

    INSERT @Blockers
    SELECT N'PersonApiResource', p.Id, N'Person owns API resources. No resource deletion or ownership transfer is authorized here.'
    FROM @Persons p WHERE EXISTS (SELECT 1 FROM dbo.ApiResources r WHERE r.OwnerPersonId = p.Id)
    UNION ALL
    SELECT N'PersonClientOwnership', p.Id, N'Person owns clients. Preserve ownership; resolve separately.'
    FROM @Persons p WHERE EXISTS (SELECT 1 FROM dbo.ClientOwnerships c WHERE c.CreatedByPersonId = p.Id)
    UNION ALL
    SELECT N'PersonScopeOwnership', p.Id, N'Person owns scopes. Preserve ownership; resolve separately.'
    FROM @Persons p WHERE EXISTS (SELECT 1 FROM dbo.ScopeOwnerships s WHERE s.CreatedByPersonId = p.Id);

    INSERT @Authorizations
    SELECT a.Id FROM dbo.OpenIddictAuthorizations a
    JOIN @Users u ON a.Subject = CONVERT(nvarchar(36), u.Id);

    -- Reject inconsistent ownership instead of revoking/deleting another user.
    INSERT @Blockers
    SELECT N'CrossAccountToken', t.Id, N'Token references target authorization but has another subject.'
    FROM dbo.OpenIddictTokens t JOIN @Authorizations a ON a.Id = t.AuthorizationId
    WHERE t.Subject IS NOT NULL AND NOT EXISTS
        (SELECT 1 FROM @Users u WHERE t.Subject = CONVERT(nvarchar(36), u.Id))
    UNION ALL
    SELECT N'CrossAccountSession', s.Id, N'Session references target authorization but belongs to another user.'
    FROM dbo.UserSessions s JOIN @Authorizations a ON s.AuthorizationId = CONVERT(nvarchar(36), a.Id)
    WHERE NOT EXISTS (SELECT 1 FROM @Users u WHERE u.Id = s.UserId)
    UNION ALL
    SELECT N'AmbiguousSessionAuthorization', s.Id, N'Target session references an authorization without the same user subject.'
    FROM dbo.UserSessions s JOIN @Users u ON u.Id = s.UserId
    JOIN dbo.OpenIddictAuthorizations a ON s.AuthorizationId = CONVERT(nvarchar(36), a.Id)
    WHERE a.Subject IS NULL OR a.Subject <> CONVERT(nvarchar(36), u.Id)
    UNION ALL
    SELECT N'CrossAccountChallenge', c.Id, N'Recovery challenge references target email but belongs to another user.'
    FROM dbo.RecoveryProofChallenges c JOIN dbo.RecoveryEmails e ON e.Id = c.RecoveryEmailId
    JOIN @Users u ON u.Id = e.LocalAccountId
    WHERE NOT EXISTS (SELECT 1 FROM @Users x WHERE x.Id = c.LocalAccountId)
    UNION ALL
    SELECT N'CrossAccountApproval', a.Id, N'Native reset approval references target challenge but belongs to another user.'
    FROM dbo.NativeRecoveryResetApprovals a JOIN dbo.RecoveryProofChallenges c ON c.Id = a.RecoveryProofChallengeId
    JOIN @Users u ON u.Id = c.LocalAccountId
    WHERE NOT EXISTS (SELECT 1 FROM @Users x WHERE x.Id = a.LocalAccountId);

    SELECT DB_NAME() AS DatabaseName, CASE WHEN @Apply = 1 THEN 'APPLY' ELSE 'PREVIEW' END AS Mode,
        (SELECT COUNT(*) FROM @Users) AS UserCandidates,
        (SELECT COUNT(*) FROM @Persons) AS PersonCandidates,
        (SELECT COUNT(*) FROM @Blockers) AS BlockingDependencies;
    SELECT Id AS UserId, UserName, PersonId FROM @Users ORDER BY UserName, Id;
    SELECT Id AS PersonId, SoleUserId, 1 AS AccountLinksBeforeCleanup FROM @Persons ORDER BY Id;
    SELECT Kind, EntityId, Reason FROM @Blockers ORDER BY Kind, EntityId;
    SELECT N'OwnedLoginHistoriesDeleted' AS Effect, COUNT_BIG(*) AS [RowCount]
        FROM dbo.LoginHistories h JOIN @Users u ON u.Id = h.UserId
    UNION ALL SELECT N'OwnedPasskeysDeleted', COUNT_BIG(*) FROM dbo.UserCredentials c JOIN @Users u ON u.Id = c.UserId
    UNION ALL SELECT N'SessionsRetainedAndRevoked', COUNT_BIG(*) FROM dbo.UserSessions s JOIN @Users u ON u.Id = s.UserId
    UNION ALL SELECT N'AuthorizationsRetainedAndRevoked', COUNT_BIG(*) FROM @Authorizations;

    IF @Apply = 0
    BEGIN
        ROLLBACK TRANSACTION;
        SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
        PRINT 'PREVIEW ONLY: no persistent data changed.';
        RETURN;
    END;
    IF (SELECT COUNT(*) FROM @Users) <> @ExpectedUsers
       OR (SELECT COUNT(*) FROM @Persons) <> @ExpectedPersons
        THROW 51002, 'Candidate counts changed. Nothing applied; review a fresh preview.', 1;
    IF EXISTS (SELECT 1 FROM @Blockers)
        THROW 51003, 'Blocking dependencies or retained denial authority found. Nothing applied; resolve separately.', 1;

    -- Keep historical session/OAuth evidence, explicitly revoke target authority.
    UPDATE s SET RevokedUtc = @Now, RevocationReason = N'Operator physical account cleanup'
    FROM dbo.UserSessions s JOIN @Users u ON u.Id = s.UserId WHERE s.RevokedUtc IS NULL;
    UPDATE t SET Status = N'revoked', ConcurrencyToken = CONVERT(nvarchar(36), NEWID())
    FROM dbo.OpenIddictTokens t
    WHERE (EXISTS (SELECT 1 FROM @Users u WHERE t.Subject = CONVERT(nvarchar(36), u.Id))
        OR EXISTS (SELECT 1 FROM @Authorizations a WHERE a.Id = t.AuthorizationId))
      AND (t.Status IS NULL OR t.Status <> N'revoked');
    UPDATE a SET Status = N'revoked', ConcurrencyToken = CONVERT(nvarchar(36), NEWID())
    FROM dbo.OpenIddictAuthorizations a JOIN @Authorizations x ON x.Id = a.Id
    WHERE a.Status IS NULL OR a.Status <> N'revoked';

    -- Explicit order for recovery restrictive FKs and default-address challenges
    -- that have no RecoveryEmailId FK. Unexpected restrictive dependencies fail
    -- the entire transaction; never traverse arbitrary dependencies dynamically.
    DELETE g FROM dbo.RecoveryPrecheckGrants g JOIN @Users u ON u.Id = g.LocalAccountId;
    DELETE r FROM dbo.RecoveryEmailChangeRequests r JOIN @Users u ON u.Id = r.LocalAccountId;
    DELETE a FROM dbo.NativeRecoveryResetApprovals a JOIN @Users u ON u.Id = a.LocalAccountId;
    DELETE c FROM dbo.RecoveryProofChallenges c JOIN @Users u ON u.Id = c.LocalAccountId;

    -- Existing FKs cascade ONLY owned Identity roles/claims/logins/tokens,
    -- LoginHistories, UserCredentials, UserAppRoles and remaining recovery rows.
    -- Global roles, clients, scopes, independent AuditEvents are never deleted.
    DELETE a FROM dbo.AspNetUsers a JOIN @Users u ON u.Id = a.Id;
    DECLARE @DeletedUsers int = @@ROWCOUNT;
    DELETE p FROM dbo.Persons p JOIN @Persons x ON x.Id = p.Id;
    DECLARE @DeletedPersons int = @@ROWCOUNT;
    IF @DeletedUsers <> @ExpectedUsers OR @DeletedPersons <> @ExpectedPersons
        THROW 51004, 'Unexpected affected row counts; all changes rolled back.', 1;

    COMMIT TRANSACTION;
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    SELECT N'APPLIED' AS Result, @DeletedUsers AS DeletedUsers, @DeletedPersons AS DeletedPersons;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    THROW;
END CATCH;
