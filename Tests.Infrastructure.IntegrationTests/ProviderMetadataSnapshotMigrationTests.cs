using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tests.Infrastructure.IntegrationTests;

public sealed class ProviderMetadataSnapshotMigrationTests
{
    [Theory]
    [InlineData(typeof(global::Infrastructure.Migrations.SqlServer.Migrations.AddProviderEmailSnapshot), "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData(typeof(global::Infrastructure.Migrations.Postgres.Migrations.AddProviderEmailSnapshot), "Npgsql.EntityFrameworkCore.PostgreSQL")]
    public void Migration_AddsOneToOneProviderMetadataSnapshot(Type migrationType, string activeProvider)
    {
        var snapshotType = migrationType.Assembly.GetType(
            $"{migrationType.Namespace}.ApplicationDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;
        var entity = snapshot.Model.FindEntityType(typeof(ProviderMetadataSnapshot));

        Assert.NotNull(entity);
        Assert.Equal("ProviderEmailSnapshots", entity!.GetTableName());
        var expectedProperties = new[]
        {
            "Id", "ProviderSubjectDirectoryBindingId", "EvidenceState", "Email", "EmailTrustOrigin", "VerifiedAt", "RefreshedAtUtc"
        };
        Assert.Equal(expectedProperties.Order(), entity.GetProperties().Select(property => property.Name).Order());
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(ProviderMetadataSnapshot.ProviderSubjectDirectoryBindingId)]));

        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        var builder = new MigrationBuilder(activeProvider);
        migrationType.GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        var table = Assert.Single(builder.Operations.OfType<CreateTableOperation>());
        Assert.Equal("ProviderEmailSnapshots", table.Name);
        Assert.Equal(expectedProperties.Order(), table.Columns.Select(column => column.Name).Order());
        Assert.Equal(2, builder.Operations.Count);
        Assert.Single(builder.Operations.OfType<CreateIndexOperation>());
        Assert.Contains(table.ForeignKeys, foreignKey =>
            foreignKey.PrincipalTable == "ProviderSubjectDirectoryBindings" &&
            foreignKey.Columns.Single() == nameof(ProviderMetadataSnapshot.ProviderSubjectDirectoryBindingId));

        // Model comparison and SQL generation do not open a database connection.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (activeProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            options.UseSqlServer("Server=127.0.0.1;Database=unused;Integrated Security=true", configuration =>
                configuration.MigrationsAssembly(migrationType.Assembly.FullName));
        }
        else
        {
            options.UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused", configuration =>
                configuration.MigrationsAssembly(migrationType.Assembly.FullName));
        }

        options.UseOpenIddict<Guid>();
        using var context = new ApplicationDbContext(options.Options);
        Assert.False(context.Database.HasPendingModelChanges());
        var sql = context.GetService<IMigrationsSqlGenerator>().Generate(builder.Operations, context.Model);
        Assert.NotEmpty(sql);
        Assert.All(sql, command => Assert.DoesNotContain("INSERT", command.CommandText, StringComparison.OrdinalIgnoreCase));

        var down = Assert.Single(migration.DownOperations);
        Assert.Equal("ProviderEmailSnapshots", Assert.IsType<DropTableOperation>(down).Name);
    }

    [Theory]
    [InlineData(typeof(global::Infrastructure.Migrations.SqlServer.Migrations.AddProviderMetadataSnapshot))]
    [InlineData(typeof(global::Infrastructure.Migrations.Postgres.Migrations.AddProviderMetadataSnapshot))]
    public void WithdrawnDraftMigration_IsInertAndCannotRecreateLegacyAuthority(Type migrationType)
    {
        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        Assert.Empty(migration.UpOperations);
        Assert.Empty(migration.DownOperations);
        Assert.Null(migration.TargetModel.FindEntityType(typeof(ProviderMetadataSnapshot)));
    }

    [Theory]
    [InlineData(typeof(global::Infrastructure.Migrations.SqlServer.Migrations.AddRecoverySourceBootstrapRevocationAndForgotPasswordMode), "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData(typeof(global::Infrastructure.Migrations.Postgres.Migrations.AddRecoverySourceBootstrapRevocationAndForgotPasswordMode), "Npgsql.EntityFrameworkCore.PostgreSQL")]
    public void Migration_AddsRecoveryRevocationGuardAndBackfillsExternalForgotPasswordMode(
        Type migrationType,
        string activeProvider)
    {
        var snapshotType = migrationType.Assembly.GetType(
            $"{migrationType.Namespace}.ApplicationDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;

        Assert.NotNull(snapshot.Model.FindEntityType(typeof(ApplicationUser))!
            .FindProperty(nameof(ApplicationUser.RecoverySourceBootstrapRevokedAtUtc)));
        Assert.Equal(
            (int)ForgotPasswordMode.External,
            Convert.ToInt32(snapshot.Model.FindEntityType(typeof(SecurityPolicy))!
                .FindProperty(nameof(SecurityPolicy.ForgotPasswordMode))!
                .GetDefaultValue()));

        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        var builder = new MigrationBuilder(activeProvider);
        migrationType.GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        var columns = builder.Operations.OfType<AddColumnOperation>().ToArray();

        var revocationGuard = Assert.Single(columns, column =>
            column.Table == "AspNetUsers" &&
            column.Name == nameof(ApplicationUser.RecoverySourceBootstrapRevokedAtUtc));
        Assert.True(revocationGuard.IsNullable);

        var forgotPasswordMode = Assert.Single(columns, column =>
            column.Table == "SecurityPolicies" &&
            column.Name == nameof(SecurityPolicy.ForgotPasswordMode));
        Assert.False(forgotPasswordMode.IsNullable);
        Assert.Equal((int)ForgotPasswordMode.External, forgotPasswordMode.DefaultValue);
    }
}
