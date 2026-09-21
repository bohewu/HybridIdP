using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tests.Infrastructure.IntegrationTests;

public sealed class Stage1BindingMigrationTests
{
    [Fact]
    public void SqlServerSnapshot_ContainsImmutableBindingTableAndUniqueIndexes()
    {
        AssertBindingModel(
            typeof(global::Infrastructure.Migrations.SqlServer.Migrations.AddProviderSubjectDirectoryBinding));
    }

    [Fact]
    public void PostgresSnapshot_ContainsImmutableBindingTableAndUniqueIndexes()
    {
        AssertBindingModel(
            typeof(global::Infrastructure.Migrations.Postgres.Migrations.AddProviderSubjectDirectoryBinding));
    }

    [Theory]
    [InlineData(
        typeof(global::Infrastructure.Migrations.SqlServer.Migrations.AddCanonicalAccountAliasToProviderBinding),
        "Microsoft.EntityFrameworkCore.SqlServer",
        "[NormalizedCanonicalAccountAlias] IS NOT NULL")]
    [InlineData(
        typeof(global::Infrastructure.Migrations.Postgres.Migrations.AddCanonicalAccountAliasToProviderBinding),
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "\"NormalizedCanonicalAccountAlias\" IS NOT NULL")]
    public void CanonicalAccountAliasMigrations_AddNullableUniqueNormalizedAlias(
        Type migrationType,
        string activeProvider,
        string expectedFilter)
    {
        var snapshotType = migrationType.Assembly.GetType(
            $"{migrationType.Namespace}.ApplicationDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;
        var entity = snapshot.Model.FindEntityType(typeof(ProviderSubjectDirectoryBinding));

        Assert.NotNull(entity);
        var alias = entity!.FindProperty(nameof(ProviderSubjectDirectoryBinding.NormalizedCanonicalAccountAlias));
        Assert.NotNull(alias);
        Assert.True(alias!.IsNullable);
        Assert.Equal(256, alias.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(ProviderSubjectDirectoryBinding.NormalizedCanonicalAccountAlias)]) &&
            index.GetFilter() == expectedFilter);

        var operations = GetUpOperations(migrationType, activeProvider);
        Assert.Contains(operations.OfType<AddColumnOperation>(), operation =>
            operation.Table == "ProviderSubjectDirectoryBindings" &&
            operation.Name == nameof(ProviderSubjectDirectoryBinding.NormalizedCanonicalAccountAlias) &&
            operation.IsNullable &&
            operation.MaxLength == 256);
        Assert.Contains(operations.OfType<CreateIndexOperation>(), operation =>
            operation.Table == "ProviderSubjectDirectoryBindings" &&
            operation.Columns.Single() == nameof(ProviderSubjectDirectoryBinding.NormalizedCanonicalAccountAlias) &&
            operation.IsUnique &&
            operation.Filter == expectedFilter);
    }

    private static void AssertBindingModel(Type migrationType)
    {
        var snapshotType = migrationType.Assembly.GetType(
            $"{migrationType.Namespace}.ApplicationDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;
        var entity = snapshot.Model.FindEntityType(typeof(ProviderSubjectDirectoryBinding));

        Assert.NotNull(entity);
        Assert.Equal("ProviderSubjectDirectoryBindings", entity!.GetTableName());
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["ProviderNamespace", "StableSubject"]));
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(["DirectoryObjectId"]));
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(["LocalAccountId"]));
    }

    private static IReadOnlyList<MigrationOperation> GetUpOperations(Type migrationType, string activeProvider)
    {
        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        var builder = new MigrationBuilder(activeProvider);
        migrationType.GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }
}
