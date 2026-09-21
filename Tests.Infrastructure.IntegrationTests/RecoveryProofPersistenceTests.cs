using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tests.Infrastructure.IntegrationTests;

public sealed class RecoveryProofPersistenceTests
{
    [Theory]
    [InlineData(typeof(global::Infrastructure.Migrations.SqlServer.Migrations.AddCredentialRecoveryProofFoundation), "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData(typeof(global::Infrastructure.Migrations.Postgres.Migrations.AddCredentialRecoveryProofFoundation), "Npgsql.EntityFrameworkCore.PostgreSQL")]
    public void ProviderMigrations_HaveEquivalentRecoveryProofModel(Type migrationType, string activeProvider)
    {
        var snapshotType = migrationType.Assembly.GetType(
            $"{migrationType.Namespace}.ApplicationDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;
        var recoveryEmail = snapshot.Model.FindEntityType(typeof(RecoveryEmailRecord));
        var challenge = snapshot.Model.FindEntityType(typeof(RecoveryProofChallenge));
        var approval = snapshot.Model.FindEntityType(typeof(RecoveryResetApproval));

        Assert.Equal("RecoveryEmails", recoveryEmail!.GetTableName());
        Assert.Equal("RecoveryProofChallenges", challenge!.GetTableName());
        Assert.Equal("RecoveryResetApprovals", approval!.GetTableName());
        Assert.True(recoveryEmail.FindProperty(nameof(RecoveryEmailRecord.Version))!.IsConcurrencyToken);
        Assert.True(challenge.FindProperty(nameof(RecoveryProofChallenge.Version))!.IsConcurrencyToken);
        Assert.True(approval.FindProperty(nameof(RecoveryResetApproval.Version))!.IsConcurrencyToken);
        Assert.Equal(512, challenge.FindProperty(nameof(RecoveryProofChallenge.CodeHash))!.GetMaxLength());
        Assert.Equal(64, challenge.FindProperty(nameof(RecoveryProofChallenge.ProofTokenHash))!.GetMaxLength());
        Assert.Equal(64, approval.FindProperty(nameof(RecoveryResetApproval.TokenHash))!.GetMaxLength());
        Assert.Contains(recoveryEmail.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(RecoveryEmailRecord.LocalAccountId));
        Assert.Contains(challenge.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(RecoveryProofChallenge.ProofTokenHash));
        Assert.Contains(approval.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(RecoveryResetApproval.TokenHash));

        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        var builder = new MigrationBuilder(activeProvider);
        migrationType.GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        var createdTables = builder.Operations.OfType<CreateTableOperation>().Select(operation => operation.Name).ToArray();
        Assert.Contains("RecoveryEmails", createdTables);
        Assert.Contains("RecoveryProofChallenges", createdTables);
        Assert.Contains("RecoveryResetApprovals", createdTables);
    }

    [Theory]
    [InlineData(typeof(global::Infrastructure.Migrations.SqlServer.Migrations.AddNativeRecoveryAssistance), "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData(typeof(global::Infrastructure.Migrations.Postgres.Migrations.AddNativeRecoveryAssistance), "Npgsql.EntityFrameworkCore.PostgreSQL")]
    public void ProviderMigrations_HaveEquivalentNativeAssistanceModel(Type migrationType, string activeProvider)
    {
        var snapshotType = migrationType.Assembly.GetType(
            $"{migrationType.Namespace}.ApplicationDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;
        var challenge = snapshot.Model.FindEntityType(typeof(RecoveryProofChallenge));
        var approval = snapshot.Model.FindEntityType(typeof(NativeRecoveryResetApproval));

        Assert.Equal(256, challenge!.FindProperty(nameof(RecoveryProofChallenge.NativeContextHash))!.GetMaxLength());
        Assert.Equal(256, challenge.FindProperty(nameof(RecoveryProofChallenge.NativeCsrfHash))!.GetMaxLength());
        Assert.Equal(256, challenge.FindProperty(nameof(RecoveryProofChallenge.NativeSecurityStamp))!.GetMaxLength());
        Assert.Equal("NativeRecoveryResetApprovals", approval!.GetTableName());
        Assert.True(approval.FindProperty(nameof(NativeRecoveryResetApproval.Version))!.IsConcurrencyToken);
        Assert.Equal(500, approval.FindProperty(nameof(NativeRecoveryResetApproval.Reason))!.GetMaxLength());
        Assert.Contains(approval.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(NativeRecoveryResetApproval.RecoveryProofChallengeId));

        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        var builder = new MigrationBuilder(activeProvider);
        migrationType.GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        Assert.Contains(builder.Operations.OfType<CreateTableOperation>(), operation =>
            operation.Name == "NativeRecoveryResetApprovals");
        Assert.Contains(builder.Operations.OfType<AddColumnOperation>(), operation =>
            operation.Table == "RecoveryProofChallenges" && operation.Name == "NativeContextHash");
    }

    [Theory]
    [InlineData(typeof(global::Infrastructure.Migrations.SqlServer.Migrations.Hidp11PendingSettlement), "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData(typeof(global::Infrastructure.Migrations.Postgres.Migrations.Hidp11PendingSettlement), "Npgsql.EntityFrameworkCore.PostgreSQL")]
    public void ProviderMigrations_HaveEquivalentPendingSettlementModel(Type migrationType, string activeProvider)
    {
        var snapshotType = migrationType.Assembly.GetType(
            $"{migrationType.Namespace}.ApplicationDbContextModelSnapshot", throwOnError: true)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;
        var preparation = snapshot.Model.FindEntityType(typeof(DirectorySettlementPreparation));

        Assert.Equal("DirectorySettlementPreparations", preparation!.GetTableName());
        Assert.True(preparation.FindProperty(nameof(DirectorySettlementPreparation.Version))!.IsConcurrencyToken);
        Assert.Equal(64, preparation.FindProperty(nameof(DirectorySettlementPreparation.ContinuationHash))!.GetMaxLength());
        Assert.Equal(200, preparation.FindProperty(nameof(DirectorySettlementPreparation.EvidenceReference))!.GetMaxLength());
        Assert.Contains(preparation.GetIndexes(), index => index.IsUnique &&
            index.Properties.Single().Name == nameof(DirectorySettlementPreparation.AttemptId));
        Assert.Contains(preparation.GetIndexes(), index => index.IsUnique &&
            index.Properties.Single().Name == nameof(DirectorySettlementPreparation.ContinuationHash));

        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        var builder = new MigrationBuilder(activeProvider);
        migrationType.GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        Assert.Contains(builder.Operations.OfType<CreateTableOperation>(), operation =>
            operation.Name == "DirectorySettlementPreparations");
    }
}
