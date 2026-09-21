using Microsoft.EntityFrameworkCore;
using Core.Domain; // Assuming ApplicationUser is in Core.Domain
using Core.Domain.Entities;

namespace Core.Application;

public interface IApplicationDbContext
{
    DbSet<ApplicationUser> Users { get; }
    DbSet<ClaimDefinition> ClaimDefinitions { get; }
    DbSet<ScopeClaim> ScopeClaims { get; }
    DbSet<Setting> Settings { get; }
    DbSet<SecurityPolicy> SecurityPolicies { get; }
    DbSet<ScopeExtension> ScopeExtensions { get; }
    DbSet<Resource> Resources { get; }
    DbSet<ApiResource> ApiResources { get; }
    DbSet<ApiResourceScope> ApiResourceScopes { get; }
    DbSet<LoginHistory> LoginHistories { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<UserSession> UserSessions { get; }
    DbSet<Person> Persons { get; }
    DbSet<ClientOwnership> ClientOwnerships { get; }
    DbSet<ScopeOwnership> ScopeOwnerships { get; }
    DbSet<UserCredential> UserCredentials { get; }
    DbSet<UserAppRole> UserAppRoles { get; }
    DbSet<ProviderSubjectDirectoryBinding> ProviderSubjectDirectoryBindings { get; }
    DbSet<ProviderMetadataSnapshot> ProviderMetadataSnapshots { get; }
    DbSet<CredentialMigrationStateRecord> CredentialMigrationStateRecords { get; }
    DbSet<CredentialMigrationContinuationRecord> CredentialMigrationContinuations { get; }
    DbSet<RecoveryEmailRecord> RecoveryEmails { get; }
    DbSet<RecoveryProofChallenge> RecoveryProofChallenges { get; }
    DbSet<RecoveryResetApproval> RecoveryResetApprovals { get; }
    DbSet<NativeRecoveryResetApproval> NativeRecoveryResetApprovals { get; }
    DbSet<NativeDirectoryRecoveryAttempt> NativeDirectoryRecoveryAttempts { get; }
    DbSet<DirectorySettlementPreparation> DirectorySettlementPreparations { get; }
    DbSet<LegacyPasswordSyncAttempt> LegacyPasswordSyncAttempts { get; }

    void Detach<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
