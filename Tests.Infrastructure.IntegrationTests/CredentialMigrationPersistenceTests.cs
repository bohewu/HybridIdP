using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tests.Infrastructure.IntegrationTests;

public sealed class CredentialMigrationPersistenceTests
{
    [Theory]
    [InlineData(typeof(global::Infrastructure.Migrations.SqlServer.Migrations.AddCredentialMigrationEmailOtpRequirement), "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData(typeof(global::Infrastructure.Migrations.Postgres.Migrations.AddCredentialMigrationEmailOtpRequirement), "Npgsql.EntityFrameworkCore.PostgreSQL")]
    public void ProviderMigrations_PersistEquivalentStateAndHashedContinuationModel(
        Type migrationType,
        string activeProvider)
    {
        var snapshotType = migrationType.Assembly.GetType(
            $"{migrationType.Namespace}.ApplicationDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;
        var state = snapshot.Model.FindEntityType(typeof(CredentialMigrationStateRecord));
        var continuation = snapshot.Model.FindEntityType(typeof(CredentialMigrationContinuationRecord));

        Assert.NotNull(state);
        Assert.NotNull(continuation);
        Assert.Equal("CredentialMigrationStates", state!.GetTableName());
        Assert.Equal("CredentialMigrationContinuations", continuation!.GetTableName());
        Assert.True(state.FindProperty(nameof(CredentialMigrationStateRecord.Version))!.IsConcurrencyToken);
        Assert.Equal(40, state.FindProperty(nameof(CredentialMigrationStateRecord.State))!.GetMaxLength());
        var emailOtpRequirement = state.FindProperty(nameof(CredentialMigrationStateRecord.EffectiveEmailOtpRequirement));
        Assert.NotNull(emailOtpRequirement);
        Assert.Equal(32, emailOtpRequirement!.GetMaxLength());
        Assert.Equal("Unspecified", emailOtpRequirement.GetDefaultValue());
        Assert.Contains(state.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(CredentialMigrationStateRecord.LocalAccountId)]));
        Assert.Contains(state.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(CredentialMigrationStateRecord.ProviderSubjectDirectoryBindingId)]));
        Assert.Contains(continuation.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(CredentialMigrationContinuationRecord.TokenHash)]));
        Assert.True(continuation.FindProperty(nameof(CredentialMigrationContinuationRecord.Version))!.IsConcurrencyToken);
        Assert.Equal(64, continuation.FindProperty(nameof(CredentialMigrationContinuationRecord.TokenHash))!.GetMaxLength());
        Assert.Equal(64, continuation.FindProperty(nameof(CredentialMigrationContinuationRecord.ContextHash))!.GetMaxLength());
        Assert.Equal(64, continuation.FindProperty(nameof(CredentialMigrationContinuationRecord.CsrfHash))!.GetMaxLength());
        Assert.Null(continuation.FindProperty("ProtectedValue"));
        Assert.Single(state.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.Name == typeof(ProviderSubjectDirectoryBinding).FullName);
        Assert.Single(continuation.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.Name == typeof(CredentialMigrationStateRecord).FullName);

        var operations = GetUpOperations(migrationType, activeProvider);
        Assert.Contains(operations.OfType<AddColumnOperation>(), operation =>
            operation.Table == "CredentialMigrationStates" &&
            operation.Name == nameof(CredentialMigrationStateRecord.EffectiveEmailOtpRequirement) &&
            operation.MaxLength == 32 &&
            !operation.IsNullable &&
            Equals("Unspecified", operation.DefaultValue));
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
