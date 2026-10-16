/*
SQL Server only. Run this complete batch in the selected HybridIdP database.
Requires IMPLICIT_TRANSACTIONS OFF and no existing transaction.
Edit ONLY the operator inputs below in a private copy. No SQLCMD mode required.
Default mode previews counts without changing application data.

This copies ALL exact Legacy keys to one verified Proof authority. Before apply,
the operator must establish that EVERY selected old key is the exact opaque
stableSubject returned by that authority for the same account. The former reader
used ssoUuid ?? userId; a UUID-looking key or a row count is NOT that evidence.
Do not infer identity from account name/email or translate Academic/HR subjects.
If that equivalence cannot be established for the whole set, STOP and use a
separately verified account-linking process. No automatic subset is inferred.

Stop account-link writers during the cutover. Apply locks AspNetUserLogins until
commit, preserves all existing links, and inserts missing links only. It never
changes users, Persons, passwords, factors, recovery codes, roles, or .env.
Retain the database backup. An application rollback can retain these additive
links; do not bulk-delete links as an automatic rollback step.

Use sqlcmd -b -i <private-copy.sql> (or execute the full batch in SSMS).
Only count summaries are returned; no keys, usernames, or credentials are output.
*/

DECLARE @Apply int = 0;
DECLARE @TargetProviderNamespace nvarchar(max) = N'';
DECLARE @ExpectedDatabaseName nvarchar(max) = N'';
DECLARE @ExpectedLegacyCount bigint = NULL;
DECLARE @BackupConfirmed int = 0;
DECLARE @SameSubjectAuthorityConfirmed int = 0;

SET NOCOUNT ON;

IF @@TRANCOUNT <> 0
    THROW 51000, 'Run this batch outside any existing transaction.', 1;

IF (@@OPTIONS & 2) = 2
    THROW 51015, 'Run this batch with IMPLICIT_TRANSACTIONS OFF.', 1;

IF @Apply IS NULL OR @Apply NOT IN (0, 1)
    OR @BackupConfirmed IS NULL OR @BackupConfirmed NOT IN (0, 1)
    OR @SameSubjectAuthorityConfirmed IS NULL OR @SameSubjectAuthorityConfirmed NOT IN (0, 1)
    THROW 51001, 'Apply and confirmation inputs must be 0 or 1.', 1;

IF @ExpectedDatabaseName IS NULL
    OR DATALENGTH(@ExpectedDatabaseName) <> DATALENGTH(DB_NAME())
    OR @ExpectedDatabaseName COLLATE Latin1_General_100_BIN2 <> DB_NAME() COLLATE Latin1_General_100_BIN2
    THROW 51002, 'ExpectedDatabaseName must exactly match the selected database.', 1;

-- The whitespace set matches .NET whitespace checks, including non-ASCII space.
DECLARE @Whitespace nvarchar(32) = NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12)
    + NCHAR(13) + NCHAR(32) + NCHAR(133) + NCHAR(160) + NCHAR(5760)
    + NCHAR(8192) + NCHAR(8193) + NCHAR(8194) + NCHAR(8195) + NCHAR(8196)
    + NCHAR(8197) + NCHAR(8198) + NCHAR(8199) + NCHAR(8200) + NCHAR(8201)
    + NCHAR(8202) + NCHAR(8232) + NCHAR(8233) + NCHAR(8239) + NCHAR(8287) + NCHAR(12288);
DECLARE @Positions TABLE ([Position] int NOT NULL PRIMARY KEY);
INSERT INTO @Positions ([Position])
SELECT ones.n + tens.n * 10 + hundreds.n * 100 + 1
FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) ones(n)
CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) tens(n)
CROSS JOIN (VALUES (0),(1),(2)) hundreds(n)
WHERE ones.n + tens.n * 10 + hundreds.n * 100 < 256;

IF @TargetProviderNamespace IS NULL OR DATALENGTH(@TargetProviderNamespace) NOT BETWEEN 2 AND 400
    OR DATALENGTH(TRIM(@Whitespace FROM @TargetProviderNamespace)) <> DATALENGTH(@TargetProviderNamespace)
    OR @TargetProviderNamespace COLLATE Latin1_General_100_CI_AS = N'Legacy'
    OR EXISTS (
        SELECT 1 FROM @Positions
        WHERE [Position] <= DATALENGTH(@TargetProviderNamespace) / 2
          AND (UNICODE(SUBSTRING(@TargetProviderNamespace, [Position], 1)) < 32
            OR UNICODE(SUBSTRING(@TargetProviderNamespace, [Position], 1)) = 127))
    THROW 51003, 'Use a non-Legacy namespace of 1-200 characters without edge whitespace or control characters.', 1;

IF @ExpectedLegacyCount < 0
    THROW 51004, 'ExpectedLegacyCount cannot be negative.', 1;

IF @Apply = 1 AND (@BackupConfirmed <> 1 OR @SameSubjectAuthorityConfirmed <> 1
    OR @ExpectedLegacyCount IS NULL)
    THROW 51005, 'Apply requires backup confirmation, whole-source identity confirmation, and ExpectedLegacyCount.', 1;

IF OBJECT_ID(N'dbo.AspNetUserLogins', N'U') IS NULL
    THROW 51006, 'The selected database does not contain dbo.AspNetUserLogins.', 1;

SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @AllLinksBefore bigint;
    IF @Apply = 1
        SELECT @AllLinksBefore = COUNT_BIG(*) FROM dbo.AspNetUserLogins WITH (TABLOCKX, HOLDLOCK);
    ELSE
        SELECT @AllLinksBefore = COUNT_BIG(*) FROM dbo.AspNetUserLogins WITH (TABLOCK, HOLDLOCK);

    -- SQL collations can equate strings that the Proof contract compares ordinally.
    -- Check BOTH the column's actual collation and case aliases, even on a CS DB.
    IF EXISTS (
        SELECT 1 FROM dbo.AspNetUserLogins
        WHERE ([LoginProvider] = N'Legacy'
            OR [LoginProvider] COLLATE Latin1_General_100_CI_AS = N'Legacy')
          AND (DATALENGTH([LoginProvider]) <> DATALENGTH(N'Legacy')
            OR [LoginProvider] COLLATE Latin1_General_100_BIN2 <> N'Legacy'))
        THROW 51007, 'Legacy provider aliases or padding exist; resolve them explicitly before backfill.', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.AspNetUserLogins
        WHERE ([LoginProvider] = @TargetProviderNamespace
            OR [LoginProvider] COLLATE Latin1_General_100_CI_AS = @TargetProviderNamespace COLLATE Latin1_General_100_CI_AS)
          AND (DATALENGTH([LoginProvider]) <> DATALENGTH(@TargetProviderNamespace)
            OR [LoginProvider] COLLATE Latin1_General_100_BIN2 <> @TargetProviderNamespace COLLATE Latin1_General_100_BIN2))
        THROW 51008, 'Target provider aliases or collation collisions exist; no links were added.', 1;

    DECLARE @LegacyCount bigint = (SELECT COUNT_BIG(*) FROM dbo.AspNetUserLogins WHERE [LoginProvider] = N'Legacy');
    IF @ExpectedLegacyCount IS NOT NULL AND @ExpectedLegacyCount <> @LegacyCount
        THROW 51009, 'Legacy count differs from ExpectedLegacyCount; repeat source verification.', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.AspNetUserLogins
        WHERE [LoginProvider] = N'Legacy'
        GROUP BY [UserId]
        HAVING COUNT_BIG(*) <> 1)
        THROW 51010, 'A user has multiple Legacy subjects; the Proof authority requires one unambiguous link.', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.AspNetUserLogins s
        WHERE s.[LoginProvider] = N'Legacy'
          AND (s.[ProviderKey] IS NULL OR DATALENGTH(s.[ProviderKey]) NOT BETWEEN 2 AND 512
            OR DATALENGTH(TRIM(@Whitespace FROM s.[ProviderKey])) <> DATALENGTH(s.[ProviderKey])
            OR EXISTS (
                SELECT 1 FROM @Positions p
                WHERE p.[Position] <= DATALENGTH(s.[ProviderKey]) / 2
                  AND (UNICODE(SUBSTRING(s.[ProviderKey], p.[Position], 1)) < 32
                    OR UNICODE(SUBSTRING(s.[ProviderKey], p.[Position], 1)) = 127))))
        THROW 51011, 'A Legacy subject is empty, too long, padded, or contains control characters; do not normalize it.', 1;

    -- Includes a SQL case/accent/padding collision with a different exact key.
    IF EXISTS (
        SELECT 1
        FROM dbo.AspNetUserLogins s
        JOIN dbo.AspNetUserLogins t ON t.[ProviderKey] = s.[ProviderKey]
        WHERE s.[LoginProvider] = N'Legacy' AND t.[LoginProvider] = @TargetProviderNamespace
          AND (t.[UserId] <> s.[UserId]
            OR DATALENGTH(t.[ProviderKey]) <> DATALENGTH(s.[ProviderKey])
            OR t.[ProviderKey] COLLATE Latin1_General_100_BIN2 <> s.[ProviderKey] COLLATE Latin1_General_100_BIN2))
        THROW 51012, 'A target subject is owned by another user or differs ordinally under the SQL collation.', 1;

    -- Existing target links on a source user must be precisely the same subject.
    -- Otherwise adding another link would make LoginService reject that user.
    IF EXISTS (
        SELECT 1
        FROM dbo.AspNetUserLogins s
        JOIN dbo.AspNetUserLogins t ON t.[UserId] = s.[UserId]
        WHERE s.[LoginProvider] = N'Legacy' AND t.[LoginProvider] = @TargetProviderNamespace
          AND (DATALENGTH(t.[ProviderKey]) <> DATALENGTH(s.[ProviderKey])
            OR t.[ProviderKey] COLLATE Latin1_General_100_BIN2 <> s.[ProviderKey] COLLATE Latin1_General_100_BIN2))
        THROW 51013, 'A source user already has a different target-provider subject; no links were added.', 1;

    DECLARE @ExistingExactCount bigint = (
        SELECT COUNT_BIG(*)
        FROM dbo.AspNetUserLogins s
        JOIN dbo.AspNetUserLogins t ON t.[ProviderKey] = s.[ProviderKey] AND t.[UserId] = s.[UserId]
        WHERE s.[LoginProvider] = N'Legacy' AND t.[LoginProvider] = @TargetProviderNamespace);
    DECLARE @PendingCount bigint = @LegacyCount - @ExistingExactCount;
    DECLARE @InsertedCount bigint = 0;

    IF @Apply = 1
    BEGIN
        INSERT INTO dbo.AspNetUserLogins ([LoginProvider], [ProviderKey], [ProviderDisplayName], [UserId])
        SELECT @TargetProviderNamespace, s.[ProviderKey], @TargetProviderNamespace, s.[UserId]
        FROM dbo.AspNetUserLogins s
        WHERE s.[LoginProvider] = N'Legacy'
          AND NOT EXISTS (
            SELECT 1 FROM dbo.AspNetUserLogins t
            WHERE t.[LoginProvider] = @TargetProviderNamespace AND t.[ProviderKey] = s.[ProviderKey]);
        SET @InsertedCount = @@ROWCOUNT;

        IF @InsertedCount <> @PendingCount
            THROW 51014, 'Inserted count differs from the validated source set; transaction rolled back.', 1;
    END;

    COMMIT TRANSACTION;

    SELECT CASE WHEN @Apply = 1 THEN N'Applied' ELSE N'Preview' END AS [Mode],
        @LegacyCount AS [LegacyLinks], @AllLinksBefore - @LegacyCount AS [OtherLinksBefore],
        @ExistingExactCount AS [AlreadyLinked], @PendingCount AS [LinksToAdd],
        @InsertedCount AS [LinksInserted], @AllLinksBefore + @InsertedCount AS [TotalLinksAfter];
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
