using Core.Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tests.Infrastructure.IntegrationTests;

public sealed class LegacyPasswordSyncAttemptMigrationTests
{
    [Theory]
    [InlineData(
        typeof(global::Infrastructure.Migrations.SqlServer.Migrations.Hidp13LegacyPasswordSyncAttempts),
        "Microsoft.EntityFrameworkCore.SqlServer",
        "uniqueidentifier",
        "[Status] IN (N'Claimed', N'Unknown')")]
    [InlineData(
        typeof(global::Infrastructure.Migrations.Postgres.Migrations.Hidp13LegacyPasswordSyncAttempts),
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "uuid",
        "\"Status\" IN ('Claimed', 'Unknown')")]
    public void Migration_PreservesProviderNeutralAttemptLedger(
        Type migrationType,
        string activeProvider,
        string accountIdentityStoreType,
        string expectedBarrierFilter)
    {
        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        var builder = new MigrationBuilder(activeProvider);
        migrationType.GetMethod(
                "Up",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var table = Assert.Single(builder.Operations.OfType<CreateTableOperation>());
        Assert.Equal("LegacyPasswordSyncAttempts", table.Name);
        Assert.Equal(["OperationId"], table.PrimaryKey!.Columns);
        Assert.Equal(
            new[]
            {
                "AccountIdentity",
                "ClaimedAtUtc",
                "CompletedAtUtc",
                "CreatedAtUtc",
                "Generation",
                "LocalAccountId",
                "MappingVersion",
                "OperationId",
                "ProviderSubjectDirectoryBindingId",
                "SanitizedOutcome",
                "SourceAttemptId",
                "SourceAttemptVersion",
                "SourceCompletedAtUtc",
                "SourceCompletion",
                "SourceCreatedAtUtc",
                "SourceKind",
                "Status",
                "UpdatedAtUtc",
                "Version"
            },
            table.Columns.Select(column => column.Name).Order());

        var accountIdentity = Assert.Single(table.Columns, column => column.Name == "AccountIdentity");
        Assert.Equal(typeof(Guid), accountIdentity.ClrType);
        Assert.Equal(accountIdentityStoreType, accountIdentity.ColumnType);
        Assert.False(accountIdentity.IsNullable);
        Assert.DoesNotContain(table.Columns, column =>
            column.Name.Contains("Sso", StringComparison.OrdinalIgnoreCase));

        var foreignKey = Assert.Single(table.ForeignKeys);
        Assert.Equal(["ProviderSubjectDirectoryBindingId"], foreignKey.Columns);
        Assert.Equal("ProviderSubjectDirectoryBindings", foreignKey.PrincipalTable);
        Assert.Equal(ReferentialAction.Restrict, foreignKey.OnDelete);

        var indexes = builder.Operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Equal(4, indexes.Length);
        AssertIndex(indexes, true, null, "SourceKind", "SourceAttemptId");
        AssertIndex(indexes, true, null, "LocalAccountId", "AccountIdentity", "Generation");
        AssertIndex(indexes, true, expectedBarrierFilter, "LocalAccountId", "AccountIdentity");
        AssertIndex(indexes, false, null, "ProviderSubjectDirectoryBindingId");
        Assert.All(indexes, index => Assert.Equal("LegacyPasswordSyncAttempts", index.Table));

        var snapshotType = migrationType.Assembly.GetType(
            $"{migrationType.Namespace}.ApplicationDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;
        var snapshotEntity = snapshot.Model.FindEntityType(typeof(LegacyPasswordSyncAttempt));
        Assert.NotNull(snapshotEntity);
        Assert.Equal("LegacyPasswordSyncAttempts", snapshotEntity!.GetTableName());
        Assert.Equal(typeof(Guid), snapshotEntity.FindProperty(nameof(LegacyPasswordSyncAttempt.AccountIdentity))!.ClrType);
        Assert.Null(snapshotEntity.FindProperty("SsoUserUuid"));

        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (activeProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            options.UseSqlServer(
                "Server=127.0.0.1;Database=unused;Integrated Security=true",
                configuration => configuration.MigrationsAssembly(migrationType.Assembly.FullName));
        }
        else
        {
            options.UseNpgsql(
                "Host=127.0.0.1;Database=unused;Username=unused",
                configuration => configuration.MigrationsAssembly(migrationType.Assembly.FullName));
        }

        options.UseOpenIddict<Guid>();
        using var context = new ApplicationDbContext(options.Options);
        Assert.False(context.Database.HasPendingModelChanges());

        var down = Assert.Single(migration.DownOperations);
        Assert.Equal("LegacyPasswordSyncAttempts", Assert.IsType<DropTableOperation>(down).Name);
    }

    private static void AssertIndex(
        IEnumerable<CreateIndexOperation> indexes,
        bool unique,
        string? filter,
        params string[] columns)
    {
        var index = Assert.Single(indexes, candidate => candidate.Columns.SequenceEqual(columns));
        Assert.Equal(unique, index.IsUnique);
        Assert.Equal(filter, index.Filter);
    }
}
