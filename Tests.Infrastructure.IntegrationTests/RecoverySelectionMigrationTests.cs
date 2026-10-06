using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit.Abstractions;

namespace Tests.Infrastructure.IntegrationTests;

// Model/SQL checks are offline. Explicit owned-database cases execute real provider migrations.
public sealed class RecoverySelectionMigrationTests(ITestOutputHelper output)
{
    [OwnedLocalDbFact]
    public async Task SqlServer_ShouldPreserveLegacyStateAndGuardDowngrade_WhenMigratingOwnedDatabaseAsync()
    {
        // The caller creates and later removes ONE disposable instance. Never accept an arbitrary
        // connection string or use the default LocalDB instance. This test owns only its new database.
        var instance = Environment.GetEnvironmentVariable("RIV_H2_LOCALDB_INSTANCE")!;
        Assert.Matches("^riv_h2_[a-f0-9]{32}$", instance);
        Assert.True(OperatingSystem.IsWindows());
        var instanceDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Microsoft SQL Server Local DB", "Instances", instance);
        Assert.True(Directory.Exists(instanceDirectory));
        Assert.False(new DirectoryInfo(instanceDirectory).Attributes.HasFlag(FileAttributes.ReparsePoint));
        var database = instance + "_migration";
        var dataFile = Path.Combine(instanceDirectory, database + ".mdf");
        var logFile = Path.Combine(instanceDirectory, database + "_log.ldf");
        Assert.False(File.Exists(dataFile));
        Assert.False(File.Exists(logFile));
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\" + instance,
            InitialCatalog = "master",
            IntegratedSecurity = true,
            Encrypt = false,
            Pooling = false,
            ConnectTimeout = 15
        };
        await using var master = new SqlConnection(connectionString.ConnectionString);
        await master.OpenAsync();
        Assert.Equal(1, await ScalarAsync(master, "SELECT CONVERT(int, SERVERPROPERTY('IsLocalDB'))"));
        Assert.Equal(Path.Combine(instanceDirectory, "master.mdf"),
            (string)(await ScalarAsync(master, "SELECT physical_name FROM sys.master_files WHERE database_id = 1 AND file_id = 1"))!,
            ignoreCase: true);
        Assert.Null(await ScalarAsync(master, $"SELECT DB_ID(N'{database}')"));
        output.WriteLine("Provider: Microsoft.EntityFrameworkCore.SqlServer; server version: {0}; instance: {1}; database: {2}",
            master.ServerVersion, instance, database);

        var created = false;
        try
        {
            await ScalarAsync(master, $"CREATE DATABASE [{database}] ON PRIMARY (NAME = N'{database}', FILENAME = N'{dataFile.Replace("'", "''")}') LOG ON (NAME = N'{database}_log', FILENAME = N'{logFile.Replace("'", "''")}')");
            created = true;
            connectionString.InitialCatalog = database;
            await using var connection = new SqlConnection(connectionString.ConnectionString);
            await connection.OpenAsync();
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
            builder.UseSqlServer(connection, sql => sql.MigrationsAssembly("Infrastructure.Migrations.SqlServer"));
            builder.UseOpenIddict<Guid>();
            await using var context = new ApplicationDbContext(builder.Options);
            var migrations = context.GetService<IMigrationsAssembly>();
            var selection = migrations.Migrations.Keys.Single(key => key.EndsWith("_AddRecoveryEmailSelectionState"));
            var prior = migrations.Migrations.Keys.TakeWhile(key => key != selection).Last();
            var migrator = context.GetService<IMigrator>();

            await migrator.MigrateAsync(selection);
            Assert.Contains(selection, await context.Database.GetAppliedMigrationsAsync());
            Assert.Equal(6, await ScalarAsync(connection, """
                SELECT COUNT(*) FROM sys.tables WHERE name IN
                ('RecoveryEmailPreferences','RecoveryEmailChangeRequests','RecoveryPrecheckGrants',
                 'RecoveryStepUpGrants','RecoveryNotifications','RecoveryThrottleBuckets')
                """));
            output.WriteLine("CREATE from empty database through {0}: PASS", selection);

            // Seed only pre-feature state, then downgrade to the actual historical schema.
            // The next upgrade therefore operates on existing addresses, challenge FKs and tombstones.
            var now = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
            var account = new ApplicationUser { Id = Guid.NewGuid(), RecoverySourceBootstrapRevokedAtUtc = now };
            context.Users.Add(account);
            var email = new RecoveryEmailRecord(account.Id, "legacy@example.test", "LEGACY@EXAMPLE.TEST", now);
            email.MarkVerified(now.AddMinutes(1));
            context.RecoveryEmails.Add(email);
            var active = new RecoveryProofChallenge(email.Id, account.Id, RecoveryProofPurpose.NativePasswordRecovery,
                "synthetic-active-hash", now, now.AddMinutes(10));
            var revoked = new RecoveryProofChallenge(email.Id, account.Id, RecoveryProofPurpose.NativePasswordRecovery,
                "synthetic-revoked-hash", now, now.AddMinutes(10));
            revoked.Revoke(now.AddMinutes(2));
            context.RecoveryProofChallenges.AddRange(active, revoked);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var historical = await ReadLegacyStateAsync(connection);

            await migrator.MigrateAsync(prior);
            Assert.DoesNotContain(selection, await context.Database.GetAppliedMigrationsAsync());
            Assert.Null(await ScalarAsync(connection, "SELECT OBJECT_ID(N'RecoveryEmailPreferences')"));
            Assert.Equal(historical, await ReadLegacyStateAsync(connection));
            Assert.Equal(0, await ScalarAsync(connection, "SELECT CONVERT(int, is_nullable) FROM sys.columns WHERE object_id = OBJECT_ID(N'RecoveryProofChallenges') AND name = 'RecoveryEmailId'"));
            output.WriteLine("SAFE DOWN to {0}: legacy values and relationships preserved", prior);

            await migrator.MigrateAsync(selection);
            Assert.Equal(historical, await ReadLegacyStateAsync(connection));
            Assert.Equal(RecoveryEmailProvenance.LegacyUnknown, (await context.RecoveryEmails.SingleAsync()).Provenance);
            Assert.Empty(await context.RecoveryEmailPreferences.ToListAsync());
            Assert.All(await context.RecoveryProofChallenges.ToListAsync(), challenge =>
            {
                Assert.Equal(email.Id, challenge.RecoveryEmailId);
                Assert.Null(challenge.SelectionEpoch);
                Assert.Null(challenge.DestinationKind);
                Assert.Null(challenge.DestinationFingerprint);
                Assert.Null(challenge.DestinationVersion);
                Assert.Null(challenge.DeliveryState);
            });
            Assert.Equal(now, (await context.Users.SingleAsync()).RecoverySourceBootstrapRevokedAtUtc);
            Assert.Equal(now.AddMinutes(2), (await context.RecoveryProofChallenges.SingleAsync(c => c.Id == revoked.Id)).RevokedAtUtc);
            Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'RecoveryProofChallenges') AND referenced_object_id = OBJECT_ID(N'RecoveryEmails') AND is_disabled = 0 AND is_not_trusted = 0"));
            output.WriteLine("HISTORICAL UPGRADE: 1 verified address, 2 challenges, challenge revocation and source opt-out tombstone retained; no preference or selection intent invented");

            var preference = new RecoveryEmailPreference(account.Id, now);
            Assert.True(preference.TrySelect(RecoveryEmailSelectionMode.Disabled, 1, now.AddMinutes(3)));
            preference.SetSourceDefaultOptOut(true, now.AddMinutes(3));
            context.RecoveryEmailPreferences.Add(preference);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var usedSelection = await ScalarAsync(connection, "SELECT * FROM RecoveryEmailPreferences ORDER BY LocalAccountId FOR JSON PATH, INCLUDE_NULL_VALUES");
            var rejected = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(prior));
            Assert.Equal(51000, rejected.Number);
            Assert.Contains("before downgrade", rejected.Message);
            Assert.Contains(selection, await context.Database.GetAppliedMigrationsAsync());
            Assert.Equal(historical, await ReadLegacyStateAsync(connection));
            Assert.Equal(usedSelection, await ScalarAsync(connection, "SELECT * FROM RecoveryEmailPreferences ORDER BY LocalAccountId FOR JSON PATH, INCLUDE_NULL_VALUES"));
            output.WriteLine("GUARDED DOWN: SQL error 51000; used Disabled preference/opt-out, migration history and all historical rows retained");
        }
        finally
        {
            if (created)
            {
                // Exact identity and file checks prevent cleanup from drifting to another database.
                await using var files = new SqlCommand($"SELECT physical_name FROM sys.master_files WHERE database_id = DB_ID(N'{database}') ORDER BY file_id", master);
                await using (var reader = await files.ExecuteReaderAsync())
                {
                    Assert.True(await reader.ReadAsync());
                    Assert.Equal(dataFile, reader.GetString(0), ignoreCase: true);
                    Assert.True(await reader.ReadAsync());
                    Assert.Equal(logFile, reader.GetString(0), ignoreCase: true);
                    Assert.False(await reader.ReadAsync());
                }
                await ScalarAsync(master, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
                Assert.Null(await ScalarAsync(master, $"SELECT DB_ID(N'{database}')"));
                Assert.False(File.Exists(dataFile));
                Assert.False(File.Exists(logFile));
                output.WriteLine("CLEANUP: owned database dropped; both database files absent. Caller still owns instance teardown.");
            }
        }
    }

    [OwnedPostgresFact]
    public async Task Postgres_ShouldPreserveLegacyStateAndGuardDowngrade_WhenMigratingOwnedDatabaseAsync()
    {
        // The caller owns a new portable cluster and its teardown. Do not accept arbitrary connections.
        var cluster = Environment.GetEnvironmentVariable("RIV_H2_POSTGRES_CLUSTER")!;
        Assert.Matches("^riv_h2_pg_[a-f0-9]{32}$", cluster);
        var directory = Path.GetFullPath(Environment.GetEnvironmentVariable("RIV_H2_POSTGRES_DIRECTORY")!);
        Assert.Equal(cluster, Path.GetFileName(directory));
        Assert.Equal("pg-17.11-continue3", Directory.GetParent(directory)!.Name);
        Assert.Equal("recovery-20260930-riv-a", Directory.GetParent(directory)!.Parent!.Name);
        Assert.Equal(".pipeline-output", Directory.GetParent(directory)!.Parent!.Parent!.Name);
        for (var ancestor = new DirectoryInfo(directory); ancestor is not null; ancestor = ancestor.Parent)
            Assert.False(ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint));
        var port = int.Parse(Environment.GetEnvironmentVariable("RIV_H2_POSTGRES_PORT")!);
        Assert.InRange(port, 1024, 65535);
        var pidFile = await File.ReadAllLinesAsync(Path.Combine(directory, "postmaster.pid"));
        Assert.Equal(directory, Path.GetFullPath(pidFile[1]), ignoreCase: true);
        Assert.Equal(port.ToString(), pidFile[3]);
        Assert.Equal("127.0.0.1", pidFile[5]);
        var database = cluster + "_migration";
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1", Port = port, Database = "postgres", Username = cluster,
            Pooling = false, Timeout = 15, CommandTimeout = 60, SslMode = SslMode.Disable
        };
        await using var master = new NpgsqlConnection(connectionString.ConnectionString);
        await master.OpenAsync();
        Assert.Equal(directory, Path.GetFullPath((string)(await ScalarAsync(master, "SHOW data_directory"))!), ignoreCase: true);
        Assert.Equal(cluster, await ScalarAsync(master, "SHOW cluster_name"));
        Assert.Equal("127.0.0.1", await ScalarAsync(master, "SHOW listen_addresses"));
        Assert.Equal(0L, await ScalarAsync(master, $"SELECT count(*) FROM pg_database WHERE datname = '{database}'"));
        output.WriteLine("Provider: Npgsql.EntityFrameworkCore.PostgreSQL; server version: {0}; cluster: {1}; database: {2}",
            master.ServerVersion, cluster, database);

        var created = false;
        try
        {
            await ScalarAsync(master, $"CREATE DATABASE \"{database}\"");
            created = true;
            connectionString.Database = database;
            await using var connection = new NpgsqlConnection(connectionString.ConnectionString);
            await connection.OpenAsync();
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
            builder.UseNpgsql(connection, options => options.MigrationsAssembly("Infrastructure.Migrations.Postgres"));
            builder.UseOpenIddict<Guid>();
            await using var context = new ApplicationDbContext(builder.Options);
            var migrations = context.GetService<IMigrationsAssembly>();
            var selection = migrations.Migrations.Keys.Single(key => key.EndsWith("_AddRecoveryEmailSelectionState"));
            var prior = migrations.Migrations.Keys.TakeWhile(key => key != selection).Last();
            var migrator = context.GetService<IMigrator>();

            await migrator.MigrateAsync(selection);
            Assert.Contains(selection, await context.Database.GetAppliedMigrationsAsync());
            Assert.Equal(6L, await ScalarAsync(connection, """
                SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN
                ('RecoveryEmailPreferences','RecoveryEmailChangeRequests','RecoveryPrecheckGrants',
                 'RecoveryStepUpGrants','RecoveryNotifications','RecoveryThrottleBuckets')
                """));
            output.WriteLine("CREATE from empty database through {0}: PASS", selection);

            // Same synthetic historical values and assertions as the SQL Server lifecycle above.
            var now = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
            var account = new ApplicationUser { Id = Guid.NewGuid(), RecoverySourceBootstrapRevokedAtUtc = now };
            context.Users.Add(account);
            var email = new RecoveryEmailRecord(account.Id, "legacy@example.test", "LEGACY@EXAMPLE.TEST", now);
            email.MarkVerified(now.AddMinutes(1));
            context.RecoveryEmails.Add(email);
            var active = new RecoveryProofChallenge(email.Id, account.Id, RecoveryProofPurpose.NativePasswordRecovery,
                "synthetic-active-hash", now, now.AddMinutes(10));
            var revoked = new RecoveryProofChallenge(email.Id, account.Id, RecoveryProofPurpose.NativePasswordRecovery,
                "synthetic-revoked-hash", now, now.AddMinutes(10));
            revoked.Revoke(now.AddMinutes(2));
            context.RecoveryProofChallenges.AddRange(active, revoked);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var historical = await ReadLegacyStateAsync(connection);
            const string foreignKeyCount = """
                SELECT count(*) FROM pg_constraint WHERE contype = 'f' AND convalidated
                  AND conrelid = '"RecoveryProofChallenges"'::regclass
                  AND confrelid = '"RecoveryEmails"'::regclass
                """;

            await migrator.MigrateAsync(prior);
            Assert.DoesNotContain(selection, await context.Database.GetAppliedMigrationsAsync());
            Assert.Null(await ScalarAsync(connection, "SELECT to_regclass('\"RecoveryEmailPreferences\"')::text"));
            Assert.Equal(historical, await ReadLegacyStateAsync(connection));
            Assert.Equal("NO", await ScalarAsync(connection, """
                SELECT is_nullable FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'RecoveryProofChallenges' AND column_name = 'RecoveryEmailId'
                """));
            Assert.Equal(1L, await ScalarAsync(connection, foreignKeyCount));
            output.WriteLine("SAFE DOWN to {0}: legacy columns, values, versions and FK preserved", prior);

            await migrator.MigrateAsync(selection);
            Assert.Equal(historical, await ReadLegacyStateAsync(connection));
            Assert.Equal(RecoveryEmailProvenance.LegacyUnknown, (await context.RecoveryEmails.SingleAsync()).Provenance);
            Assert.Empty(await context.RecoveryEmailPreferences.ToListAsync());
            var challenges = await context.RecoveryProofChallenges.ToListAsync();
            Assert.Equal(2, challenges.Count);
            Assert.All(challenges, challenge =>
            {
                Assert.Equal(email.Id, challenge.RecoveryEmailId);
                Assert.Null(challenge.SelectionEpoch);
                Assert.Null(challenge.DestinationKind);
                Assert.Null(challenge.DestinationFingerprint);
                Assert.Null(challenge.DestinationVersion);
                Assert.Null(challenge.DeliveryState);
            });
            Assert.Equal(now, (await context.Users.SingleAsync()).RecoverySourceBootstrapRevokedAtUtc);
            Assert.Equal(now.AddMinutes(2), challenges.Single(c => c.Id == revoked.Id).RevokedAtUtc);
            Assert.Equal(1L, await ScalarAsync(connection, foreignKeyCount));
            output.WriteLine("HISTORICAL UPGRADE: 1 verified address, 2 native challenges, IDs/FKs/versions, revocation and source opt-out tombstone retained; LegacyUnknown and no invented selection");

            var preference = new RecoveryEmailPreference(account.Id, now);
            Assert.True(preference.TrySelect(RecoveryEmailSelectionMode.Disabled, 1, now.AddMinutes(3)));
            preference.SetSourceDefaultOptOut(true, now.AddMinutes(3));
            context.RecoveryEmailPreferences.Add(preference);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            const string preferenceSnapshot = """
                SELECT jsonb_agg(to_jsonb(p) ORDER BY "LocalAccountId")::text FROM "RecoveryEmailPreferences" p
                """;
            var usedSelection = await ScalarAsync(connection, preferenceSnapshot);
            var history = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(prior));
            Assert.Equal("P0001", rejected.SqlState);
            Assert.Contains("before downgrade", rejected.MessageText);
            Assert.Equal(history, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Contains(selection, history);
            Assert.Equal(historical, await ReadLegacyStateAsync(connection));
            Assert.Equal(usedSelection, await ScalarAsync(connection, preferenceSnapshot));
            Assert.Equal(1L, await ScalarAsync(connection, foreignKeyCount));
            output.WriteLine("GUARDED DOWN: PostgreSQL SQLSTATE {0}; used Disabled preference/opt-out, complete migration history and historical rows retained", rejected.SqlState);
        }
        finally
        {
            if (created)
            {
                await ScalarAsync(master, $"DROP DATABASE \"{database}\"");
                Assert.Equal(0L, await ScalarAsync(master, $"SELECT count(*) FROM pg_database WHERE datname = '{database}'"));
                output.WriteLine("CLEANUP: exact owned database dropped. Caller still owns cluster teardown.");
            }
        }
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task<object?[]> ReadLegacyStateAsync(NpgsqlConnection connection) =>
    [
        await ScalarAsync(connection, """SELECT jsonb_agg(to_jsonb(u) ORDER BY "Id")::text FROM "AspNetUsers" u"""),
        await ScalarAsync(connection, """
            SELECT jsonb_agg(to_jsonb(e) ORDER BY "Id")::text FROM (
                SELECT "Id", "LocalAccountId", "Address", "NormalizedAddress", "CreatedAtUtc", "UpdatedAtUtc",
                    "VerifiedAtUtc", "NextSendAllowedAtUtc", "LastAdministrativeActorId",
                    "LastAdministrativeReason", "LastIdentityCheckEvidence", "Version" FROM "RecoveryEmails") e
            """),
        await ScalarAsync(connection, """
            SELECT jsonb_agg(to_jsonb(c) ORDER BY "Id")::text FROM (
                SELECT "Id", "RecoveryEmailId", "LocalAccountId", "CredentialMigrationContinuationId", "Purpose",
                    "CodeHash", "ProofTokenHash", "CreatedAtUtc", "ExpiresAtUtc", "SentAtUtc", "VerificationAttempts",
                    "VerifiedAtUtc", "ConsumedAtUtc", "RevokedAtUtc", "Version", "NativeRecoveryChallengeId",
                    "NativeContextHash", "NativeCsrfHash", "NativeDirectoryAuthority", "NativeDirectoryObjectId",
                    "NativeRecoveryEmailVersion", "NativeSecurityStamp" FROM "RecoveryProofChallenges") c
            """)
    ];

    public sealed class OwnedPostgresFactAttribute : FactAttribute
    {
        public OwnedPostgresFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RUN_RIV_H2_POSTGRES") != "1" ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RIV_H2_POSTGRES_CLUSTER")) ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RIV_H2_POSTGRES_DIRECTORY")) ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RIV_H2_POSTGRES_PORT")))
                Skip = "Requires RUN_RIV_H2_POSTGRES=1 and a new owned RIV_H2_POSTGRES_CLUSTER/DIRECTORY/PORT.";
        }
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task<object?[]> ReadLegacyStateAsync(SqlConnection connection) =>
    [
        await ScalarAsync(connection, "SELECT (SELECT * FROM AspNetUsers ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES)"),
        await ScalarAsync(connection, """
            SELECT (SELECT Id, LocalAccountId, Address, NormalizedAddress, CreatedAtUtc, UpdatedAtUtc,
                VerifiedAtUtc, NextSendAllowedAtUtc, LastAdministrativeActorId,
                LastAdministrativeReason, LastIdentityCheckEvidence, Version
            FROM RecoveryEmails ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES)
            """),
        await ScalarAsync(connection, """
            SELECT (SELECT Id, RecoveryEmailId, LocalAccountId, CredentialMigrationContinuationId, Purpose,
                CodeHash, ProofTokenHash, CreatedAtUtc, ExpiresAtUtc, SentAtUtc, VerificationAttempts,
                VerifiedAtUtc, ConsumedAtUtc, RevokedAtUtc, Version, NativeRecoveryChallengeId,
                NativeContextHash, NativeCsrfHash, NativeDirectoryAuthority, NativeDirectoryObjectId,
                NativeRecoveryEmailVersion, NativeSecurityStamp
            FROM RecoveryProofChallenges ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES)
            """)
    ];

    public sealed class OwnedLocalDbFactAttribute : FactAttribute
    {
        public OwnedLocalDbFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RUN_RIV_H2_LOCALDB") != "1" ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RIV_H2_LOCALDB_INSTANCE")))
                Skip = "Requires RUN_RIV_H2_LOCALDB=1 and a newly created disposable RIV_H2_LOCALDB_INSTANCE (riv_h2_<32 lowercase hex>).";
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Migration_PreservesHistoricalDataAndGeneratesUpgradeAndGuardedDowngrade(bool postgres)
    {
        using var context = CreateContext(postgres);
        var migrations = context.GetService<IMigrationsAssembly>();
        var id = migrations.Migrations.Keys.Single(key => key.EndsWith("_AddRecoveryEmailSelectionState"));
        var migration = migrations.CreateMigration(migrations.Migrations[id], context.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, operation => operation is DropTableOperation or DropColumnOperation or DeleteDataOperation or UpdateDataOperation or InsertDataOperation or SqlOperation);
        var provenance = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>(), operation => operation.Name == "Provenance");
        Assert.Equal("LegacyUnknown", provenance.DefaultValue);
        Assert.Equal("RecoveryEmails", provenance.Table);
        Assert.Equal(6, migration.UpOperations.OfType<CreateTableOperation>().Count());
        var altered = migration.UpOperations.OfType<AlterColumnOperation>().ToArray();
        Assert.Equal(2, altered.Length);
        Assert.All(altered, column => { Assert.Equal("RecoveryEmailId", column.Name); Assert.True(column.IsNullable); });
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("before downgrade", guard.Sql);
        Assert.Contains("RecoveryEmailPreferences", guard.Sql);
        Assert.Contains("LegacyUnknown", guard.Sql);
        Assert.All(migration.DownOperations.OfType<AlterColumnOperation>(), operation => Assert.Null(operation.DefaultValue));

        var prior = migrations.Migrations.Keys.TakeWhile(key => key != id).Last();
        var migrator = context.GetService<IMigrator>();
        var upgrade = migrator.GenerateScript(prior, id);
        var create = migrator.GenerateScript("0", id);
        var down = migrator.GenerateScript(id, prior);
        Assert.Contains("RecoveryPrecheckGrants", upgrade);
        Assert.Contains("RecoveryEmailChangeRequests", create);
        Assert.Contains("before downgrade", down);
        Assert.True(down.IndexOf("before downgrade", StringComparison.Ordinal) < down.IndexOf("DROP TABLE", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Model_EnforcesAccountKeysConcurrencyReservationsAndPreservesExistingForeignKeys(bool postgres)
    {
        using var context = CreateContext(postgres);
        var model = context.GetService<IDesignTimeModel>().Model;
        var preference = model.FindEntityType(typeof(RecoveryEmailPreference))!;
        Assert.Equal(nameof(RecoveryEmailPreference.LocalAccountId), Assert.Single(preference.FindPrimaryKey()!.Properties).Name);
        Assert.True(preference.FindProperty(nameof(RecoveryEmailPreference.SelectionEpoch))!.IsConcurrencyToken);
        foreach (var type in new[] { typeof(RecoveryEmailPreference), typeof(RecoveryEmailPendingChange), typeof(RecoveryPrecheckGrant),
                     typeof(RecoveryStepUpGrant), typeof(RecoveryNotification), typeof(RecoveryThrottleBucket) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.True(entity.FindProperty("Version")!.IsConcurrencyToken);
            Assert.Null(entity.FindProperty("PersonId"));
            Assert.DoesNotContain(entity.GetProperties(), property => property.Name.Contains("IdentityIdentifier", StringComparison.OrdinalIgnoreCase));
        }
        var pending = model.FindEntityType(typeof(RecoveryEmailPendingChange))!;
        var pendingIndex = Assert.Single(pending.GetIndexes(), index => index.IsUnique);
        Assert.Contains("ConsumedAtUtc", pendingIndex.GetFilter());
        Assert.Contains("RevokedAtUtc", pendingIndex.GetFilter());
        var grant = model.FindEntityType(typeof(RecoveryPrecheckGrant))!;
        Assert.Contains(grant.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == "ReservedChallengeId");
        var challenge = model.FindEntityType(typeof(RecoveryProofChallenge))!;
        Assert.True(challenge.FindProperty("RecoveryEmailId")!.IsNullable);
        Assert.Contains(challenge.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(RecoveryEmailRecord));
        Assert.Contains(challenge.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(CredentialMigrationContinuationRecord));
        Assert.Equal(64, grant.FindProperty("ProviderBindingVersion")!.GetMaxLength());
    }

    private static ApplicationDbContext CreateContext(bool postgres)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (postgres)
            builder.UseNpgsql("Host=127.0.0.1;Port=1;Database=offline", options => options.MigrationsAssembly("Infrastructure.Migrations.Postgres"));
        else
            builder.UseSqlServer("Server=127.0.0.1,1;Database=offline;Integrated Security=True", options => options.MigrationsAssembly("Infrastructure.Migrations.SqlServer"));
        builder.UseOpenIddict<Guid>();
        return new ApplicationDbContext(builder.Options);
    }
}
