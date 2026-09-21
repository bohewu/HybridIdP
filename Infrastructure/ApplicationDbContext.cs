using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Core.Application.Interfaces;
using Core.Domain.Enums;

namespace Infrastructure;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Implement the DbSet from IApplicationDbContext
    public new DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    
    // Custom claim definitions (different from IdentityUserClaim)
    DbSet<ClaimDefinition> IApplicationDbContext.ClaimDefinitions => Set<ClaimDefinition>();
    public DbSet<ScopeClaim> ScopeClaims => Set<ScopeClaim>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<SecurityPolicy> SecurityPolicies => Set<SecurityPolicy>();
    public DbSet<LoginHistory> LoginHistories => Set<LoginHistory>();
    public DbSet<ScopeExtension> ScopeExtensions => Set<ScopeExtension>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ApiResource> ApiResources => Set<ApiResource>();
    public DbSet<ApiResourceScope> ApiResourceScopes => Set<ApiResourceScope>();
    public DbSet<ClientRequiredScope> ClientRequiredScopes => Set<ClientRequiredScope>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<ClientOwnership> ClientOwnerships => Set<ClientOwnership>();
    public DbSet<ScopeOwnership> ScopeOwnerships => Set<ScopeOwnership>();
    // Phase 20.4
    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();
    // Phase 22.1
    public DbSet<UserAppRole> UserAppRoles => Set<UserAppRole>();
    public DbSet<ProviderSubjectDirectoryBinding> ProviderSubjectDirectoryBindings => Set<ProviderSubjectDirectoryBinding>();
    public DbSet<ProviderMetadataSnapshot> ProviderMetadataSnapshots => Set<ProviderMetadataSnapshot>();
    public DbSet<CredentialMigrationStateRecord> CredentialMigrationStateRecords => Set<CredentialMigrationStateRecord>();
    public DbSet<CredentialMigrationContinuationRecord> CredentialMigrationContinuations => Set<CredentialMigrationContinuationRecord>();
    public DbSet<RecoveryEmailRecord> RecoveryEmails => Set<RecoveryEmailRecord>();
    public DbSet<RecoveryProofChallenge> RecoveryProofChallenges => Set<RecoveryProofChallenge>();
    public DbSet<RecoveryResetApproval> RecoveryResetApprovals => Set<RecoveryResetApproval>();
    public DbSet<NativeRecoveryResetApproval> NativeRecoveryResetApprovals => Set<NativeRecoveryResetApproval>();
    public DbSet<NativeDirectoryRecoveryAttempt> NativeDirectoryRecoveryAttempts => Set<NativeDirectoryRecoveryAttempt>();
    public DbSet<DirectorySettlementPreparation> DirectorySettlementPreparations => Set<DirectorySettlementPreparation>();
    public DbSet<LegacyPasswordSyncAttempt> LegacyPasswordSyncAttempts => Set<LegacyPasswordSyncAttempt>();

    public void Detach<TEntity>(TEntity entity) where TEntity : class
    {
        Entry(entity).State = EntityState.Detached;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.RecoverySourceBootstrapRevokedAtUtc);
        });

        builder.Entity<SecurityPolicy>(entity =>
        {
            entity.Property(policy => policy.ForgotPasswordMode)
                .HasDefaultValue(ForgotPasswordMode.External)
                .ValueGeneratedNever()
                .IsRequired();
        });
        
        // Configure UserCredential
        builder.Entity<ProviderSubjectDirectoryBinding>(entity =>
        {
            entity.ToTable("ProviderSubjectDirectoryBindings");
            entity.HasKey(binding => binding.Id);
            entity.Property(binding => binding.ProviderNamespace).HasMaxLength(200).IsRequired();
            entity.Property(binding => binding.StableSubject).HasMaxLength(256).IsRequired();
            entity.Property(binding => binding.NormalizedCanonicalAccountAlias).HasMaxLength(256);
            entity.Property(binding => binding.CreatedAtUtc).IsRequired();
            entity.HasIndex(binding => new { binding.ProviderNamespace, binding.StableSubject }).IsUnique();
            entity.HasIndex(binding => binding.DirectoryObjectId).IsUnique();
            entity.HasIndex(binding => binding.LocalAccountId).IsUnique();
            var aliasIndex = entity.HasIndex(binding => binding.NormalizedCanonicalAccountAlias).IsUnique();
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                aliasIndex.HasFilter("[NormalizedCanonicalAccountAlias] IS NOT NULL");
            }
            else if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                aliasIndex.HasFilter("\"NormalizedCanonicalAccountAlias\" IS NOT NULL");
            }
        });

        builder.Entity<ProviderMetadataSnapshot>(entity =>
        {
            // The withdrawn draft cache is deliberately not mapped or imported.
            entity.ToTable("ProviderEmailSnapshots");
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.EvidenceState)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(snapshot => snapshot.Email).HasMaxLength(320);
            entity.Property(snapshot => snapshot.EmailTrustOrigin)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(snapshot => snapshot.RefreshedAtUtc).IsRequired();
            entity.HasIndex(snapshot => snapshot.ProviderSubjectDirectoryBindingId).IsUnique();
            entity.HasOne<ProviderSubjectDirectoryBinding>()
                .WithOne()
                .HasForeignKey<ProviderMetadataSnapshot>(snapshot => snapshot.ProviderSubjectDirectoryBindingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CredentialMigrationStateRecord>(entity =>
        {
            entity.ToTable("CredentialMigrationStates");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.State).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(record => record.EffectiveEmailOtpRequirement)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(EffectiveEmailOtpRequirement.Unspecified)
                .IsRequired();
            entity.Property(record => record.CreatedAtUtc).IsRequired();
            entity.Property(record => record.UpdatedAtUtc).IsRequired();
            entity.Property(record => record.Version).IsConcurrencyToken().IsRequired();
            entity.HasIndex(record => record.LocalAccountId).IsUnique();
            entity.HasIndex(record => record.ProviderSubjectDirectoryBindingId).IsUnique();
            entity.HasOne<ProviderSubjectDirectoryBinding>()
                .WithOne()
                .HasForeignKey<CredentialMigrationStateRecord>(record => record.ProviderSubjectDirectoryBindingId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CredentialMigrationContinuationRecord>(entity =>
        {
            entity.ToTable("CredentialMigrationContinuations");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(record => record.ContextHash).HasMaxLength(64).IsRequired();
            entity.Property(record => record.CsrfHash).HasMaxLength(64).IsRequired();
            entity.Property(record => record.CreatedAtUtc).IsRequired();
            entity.Property(record => record.ExpiresAtUtc).IsRequired();
            entity.Property(record => record.Version).IsConcurrencyToken().IsRequired();
            entity.HasIndex(record => record.TokenHash).IsUnique();
            entity.HasIndex(record => new { record.CredentialMigrationStateRecordId, record.ConsumedAtUtc });
            entity.HasOne<CredentialMigrationStateRecord>()
                .WithMany()
                .HasForeignKey(record => record.CredentialMigrationStateRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RecoveryEmailRecord>(entity =>
        {
            entity.ToTable("RecoveryEmails");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Address).HasMaxLength(320).IsRequired();
            entity.Property(record => record.NormalizedAddress).HasMaxLength(320).IsRequired();
            entity.Property(record => record.LastAdministrativeReason).HasMaxLength(500);
            entity.Property(record => record.LastIdentityCheckEvidence).HasMaxLength(500);
            entity.Property(record => record.CreatedAtUtc).IsRequired();
            entity.Property(record => record.UpdatedAtUtc).IsRequired();
            entity.Property(record => record.Version).IsConcurrencyToken().IsRequired();
            entity.HasIndex(record => record.LocalAccountId).IsUnique();
            entity.HasOne<ApplicationUser>()
                .WithOne()
                .HasForeignKey<RecoveryEmailRecord>(record => record.LocalAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RecoveryProofChallenge>(entity =>
        {
            entity.ToTable("RecoveryProofChallenges");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Purpose).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(record => record.CodeHash).HasMaxLength(512).IsRequired();
            entity.Property(record => record.ProofTokenHash).HasMaxLength(64);
            entity.Property(record => record.NativeContextHash).HasMaxLength(256);
            entity.Property(record => record.NativeCsrfHash).HasMaxLength(256);
            entity.Property(record => record.NativeSecurityStamp).HasMaxLength(256);
            entity.Property(record => record.CreatedAtUtc).IsRequired();
            entity.Property(record => record.ExpiresAtUtc).IsRequired();
            entity.Property(record => record.SentAtUtc).IsRequired();
            entity.Property(record => record.Version).IsConcurrencyToken().IsRequired();
            var proofTokenIndex = entity.HasIndex(record => record.ProofTokenHash).IsUnique();
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                proofTokenIndex.HasFilter("[ProofTokenHash] IS NOT NULL");
            }
            else if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                proofTokenIndex.HasFilter("\"ProofTokenHash\" IS NOT NULL");
            }
            entity.HasIndex(record => new
            {
                record.LocalAccountId,
                record.Purpose,
                record.CredentialMigrationContinuationId,
                record.RevokedAtUtc,
                record.ConsumedAtUtc
            });
            entity.HasOne<RecoveryEmailRecord>()
                .WithMany()
                .HasForeignKey(record => record.RecoveryEmailId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CredentialMigrationContinuationRecord>()
                .WithMany()
                .HasForeignKey(record => record.CredentialMigrationContinuationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<NativeRecoveryResetApproval>(entity =>
        {
            entity.ToTable("NativeRecoveryResetApprovals");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.ContextHash).HasMaxLength(256).IsRequired();
            entity.Property(record => record.CsrfHash).HasMaxLength(256).IsRequired();
            entity.Property(record => record.SecurityStamp).HasMaxLength(256).IsRequired();
            entity.Property(record => record.Reason).HasMaxLength(500).IsRequired();
            entity.Property(record => record.IdentityCheckEvidence).HasMaxLength(500).IsRequired();
            entity.Property(record => record.CreatedAtUtc).IsRequired();
            entity.Property(record => record.ExpiresAtUtc).IsRequired();
            entity.Property(record => record.Version).IsConcurrencyToken().IsRequired();
            entity.HasIndex(record => new
            {
                record.RecoveryProofChallengeId,
                record.RevokedAtUtc,
                record.ConsumedAtUtc
            });
            var activeChallengeIndex = entity.HasIndex(record => record.RecoveryProofChallengeId).IsUnique();
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                activeChallengeIndex.HasFilter("[RevokedAtUtc] IS NULL AND [ConsumedAtUtc] IS NULL");
            }
            else if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                activeChallengeIndex.HasFilter("\"RevokedAtUtc\" IS NULL AND \"ConsumedAtUtc\" IS NULL");
            }
            entity.HasOne<RecoveryProofChallenge>()
                .WithMany()
                .HasForeignKey(record => record.RecoveryProofChallengeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RecoveryResetApproval>(entity =>
        {
            entity.ToTable("RecoveryResetApprovals");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Reason).HasMaxLength(500).IsRequired();
            entity.Property(record => record.IdentityCheckEvidence).HasMaxLength(500).IsRequired();
            entity.Property(record => record.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(record => record.CreatedAtUtc).IsRequired();
            entity.Property(record => record.ExpiresAtUtc).IsRequired();
            entity.Property(record => record.Version).IsConcurrencyToken().IsRequired();
            entity.HasIndex(record => record.TokenHash).IsUnique();
            entity.HasIndex(record => new
            {
                record.CredentialMigrationContinuationId,
                record.RevokedAtUtc,
                record.ConsumedAtUtc
            });
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(record => record.LocalAccountId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CredentialMigrationContinuationRecord>()
                .WithMany()
                .HasForeignKey(record => record.CredentialMigrationContinuationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<NativeDirectoryRecoveryAttempt>(entity =>
        {
            entity.ToTable("NativeDirectoryRecoveryAttempts", table => table.HasCheckConstraint(
                "CK_NativeDirectoryRecoveryAttempts_OperationProof",
                Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer"
                    ? "([OperationKind] = N'NativeReset' AND [RecoveryProofChallengeId] IS NOT NULL) OR ([OperationKind] IN (N'AdminTemporaryIssue', N'RequiredChange') AND [RecoveryProofChallengeId] IS NULL)"
                    : "(\"OperationKind\" = 'NativeReset' AND \"RecoveryProofChallengeId\" IS NOT NULL) OR (\"OperationKind\" IN ('AdminTemporaryIssue', 'RequiredChange') AND \"RecoveryProofChallengeId\" IS NULL)"));
            entity.HasKey(record => record.Id);
            entity.Property(record => record.OperationKind).HasConversion<string>().HasMaxLength(40)
                .HasDefaultValue(NativeDirectoryCredentialOperationKind.NativeReset).IsRequired();
            entity.Property(record => record.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(record => record.CreatedAtUtc).IsRequired();
            entity.Property(record => record.UpdatedAtUtc).IsRequired();
            entity.Property(record => record.Version).IsConcurrencyToken().IsRequired();
            entity.HasIndex(record => record.RecoveryProofChallengeId).IsUnique();
            var activeAccountIndex = entity.HasIndex(record => record.LocalAccountId).IsUnique();
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                activeAccountIndex.HasFilter("[Status] IN (N'Reserved', N'ReconciliationRequired')");
            }
            else
            {
                activeAccountIndex.HasFilter("\"Status\" IN ('Reserved', 'ReconciliationRequired')");
            }
            entity.HasOne<RecoveryProofChallenge>()
                .WithOne()
                .HasForeignKey<NativeDirectoryRecoveryAttempt>(record => record.RecoveryProofChallengeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DirectorySettlementPreparation>(entity =>
        {
            entity.ToTable("DirectorySettlementPreparations");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.OperationKind).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(record => record.ExpectedAttemptStatus).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(record => record.Disposition).HasConversion<string>().HasMaxLength(48).IsRequired();
            entity.Property(record => record.EvidenceCategory).HasConversion<string>().HasMaxLength(48).IsRequired();
            entity.Property(record => record.EvidenceReference).HasMaxLength(200).IsRequired();
            entity.Property(record => record.AccountSecurityStamp).HasMaxLength(256).IsRequired();
            entity.Property(record => record.OperatorSecurityStamp).HasMaxLength(256).IsRequired();
            entity.Property(record => record.ContinuationHash).HasMaxLength(64).IsRequired();
            entity.Property(record => record.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(record => record.ContextHash).HasMaxLength(256);
            entity.Property(record => record.CsrfHash).HasMaxLength(256);
            entity.Property(record => record.Version).IsConcurrencyToken().IsRequired();
            entity.HasIndex(record => record.ContinuationHash).IsUnique();
            var activeAttemptIndex = entity.HasIndex(record => record.AttemptId).IsUnique();
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                activeAttemptIndex.HasFilter("[Status] IN (N'Prepared', N'OwnershipPending', N'Verifying')");
            }
            else
            {
                activeAttemptIndex.HasFilter("\"Status\" IN ('Prepared', 'OwnershipPending', 'Verifying')");
            }
            entity.HasOne<NativeDirectoryRecoveryAttempt>()
                .WithMany()
                .HasForeignKey(record => record.AttemptId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RecoveryProofChallenge>()
                .WithMany()
                .HasForeignKey(record => record.OwnershipChallengeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LegacyPasswordSyncAttempt>(entity =>
        {
            entity.ToTable("LegacyPasswordSyncAttempts");
            entity.HasKey(attempt => attempt.OperationId);
            entity.Property(attempt => attempt.SourceKind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(attempt => attempt.SourceCompletion).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(attempt => attempt.MappingVersion).HasMaxLength(128).IsRequired();
            entity.Property(attempt => attempt.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(attempt => attempt.SanitizedOutcome).HasMaxLength(200);
            entity.Property(attempt => attempt.Version).IsConcurrencyToken().IsRequired();
            entity.HasIndex(attempt => new { attempt.SourceKind, attempt.SourceAttemptId }).IsUnique();
            entity.HasIndex(attempt => new { attempt.LocalAccountId, attempt.AccountIdentity, attempt.Generation }).IsUnique();
            var barrierIndex = entity.HasIndex(attempt => new { attempt.LocalAccountId, attempt.AccountIdentity }).IsUnique();
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                barrierIndex.HasFilter("[Status] IN (N'Claimed', N'Unknown')");
            }
            else
            {
                barrierIndex.HasFilter("\"Status\" IN ('Claimed', 'Unknown')");
            }
            entity.HasOne<ProviderSubjectDirectoryBinding>()
                .WithMany()
                .HasForeignKey(attempt => attempt.ProviderSubjectDirectoryBindingId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure UserCredential
        builder.Entity<UserCredential>(entity =>
        {
            entity.HasIndex(e => e.UserId);
            // Ensure CredentialId is unique across the system (or at least per user, but FIDO2 spec usually implies global uniqueness for the ID)
            // Storing as varbinary, good for indexing depending on DB.
        });
        
        // Configure OpenIddict to use the default ASP.NET Core Identity entity types
        builder.UseOpenIddict<Guid>();
        
        // Configure UserClaim entity
        builder.Entity<ClaimDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("ClaimDefinitions");

            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.ClaimType);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.ClaimType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.UserPropertyPath).HasMaxLength(200).IsRequired();
            entity.Property(e => e.DataType).HasMaxLength(50).IsRequired();
        });
        
        // Configure ScopeClaim entity
        builder.Entity<ScopeClaim>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ScopeId, e.ClaimDefinitionId }).IsUnique();
            entity.Property(e => e.ScopeId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ScopeName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.CustomMappingLogic).HasMaxLength(1000);
            
            // Configure relationship with UserClaim
            entity.HasOne(e => e.ClaimDefinition)
                .WithMany(c => c.ScopeClaims)
                .HasForeignKey(e => e.ClaimDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Setting entity
        builder.Entity<Setting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Key).IsUnique();
            entity.Property(e => e.Key).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Value).HasColumnType("text");
            // Store enum as string for readability
            entity.Property(e => e.DataType)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(e => e.UpdatedUtc).IsRequired();
            entity.Property(e => e.UpdatedBy).HasMaxLength(200);
        });

        // Configure ScopeExtension entity
        builder.Entity<ScopeExtension>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ScopeId).IsUnique();
            entity.Property(e => e.ScopeId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ConsentDisplayNameKey).HasMaxLength(200);
            entity.Property(e => e.ConsentDescriptionKey).HasMaxLength(1000);
            entity.Property(e => e.IconUrl).HasMaxLength(500);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.IsRequired).IsRequired();
            entity.Property(e => e.DisplayOrder).IsRequired();
        });

        // Configure Resource entity
        builder.Entity<Resource>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Key, e.Culture }).IsUnique();
            entity.Property(e => e.Key).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Culture).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Value).IsRequired();
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.CreatedUtc).IsRequired();
            entity.Property(e => e.UpdatedUtc).IsRequired();
        });

        // Configure ApiResource entity
        builder.Entity<ApiResource>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.BaseUrl).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        // Configure ApiResourceScope entity
        builder.Entity<ApiResourceScope>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ApiResourceId, e.ScopeId }).IsUnique();
            entity.Property(e => e.ScopeId).HasMaxLength(200).IsRequired();
            
            // Configure relationship with ApiResource
            entity.HasOne(e => e.ApiResource)
                .WithMany(r => r.Scopes)
                .HasForeignKey(e => e.ApiResourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        // Configure LoginHistory entity
        builder.Entity<LoginHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(45); // IPv6 max
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.LoginTime).IsRequired();
            entity.Property(e => e.IsSuccessful).IsRequired();
            entity.Property(e => e.RiskScore).IsRequired();
            entity.Property(e => e.IsFlaggedAbnormal).IsRequired();
            entity.Property(e => e.IsApprovedByAdmin).IsRequired();

            // Configure relationship with ApplicationUser
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure AuditEvent entity
        builder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).HasMaxLength(200).IsRequired();
            entity.Property(e => e.UserId).HasMaxLength(450); // Match ASP.NET Identity User ID length
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.IPAddress).HasMaxLength(45); // IPv6 max
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        // Configure UserSession entity
        builder.Entity<UserSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AuthorizationId).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.AuthorizationId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ClientId).HasMaxLength(200);
            entity.Property(e => e.ClientDisplayName).HasMaxLength(200);
            entity.Property(e => e.CurrentRefreshTokenHash).HasMaxLength(256);
            entity.Property(e => e.PreviousRefreshTokenHash).HasMaxLength(256);
            entity.Property(e => e.DeviceInfo).HasMaxLength(500);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.RevocationReason).HasMaxLength(500);
            
            // Phase 11.1: Optional until the session has a resolved active role.
            entity.HasIndex(e => e.ActiveRoleId);
            
            // Configure FK relationship with ApplicationRole
            entity.HasOne(e => e.ActiveRole)
                .WithMany()
                .HasForeignKey(e => e.ActiveRoleId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent role deletion if sessions exist
        });

        // Configure ClientRequiredScope entity
        builder.Entity<ClientRequiredScope>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ClientId, e.ScopeId }).IsUnique();
            entity.Property(e => e.ClientId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ScopeId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(450); // Match ASP.NET Identity User ID length
        });

        // Configure Person entity (Phase 10.1)
        builder.Entity<Person>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            // Index on Email for matching during JIT provisioning
            entity.HasIndex(e => e.Email);
            
            // Unique index on EmployeeId (if provided)
            entity.HasIndex(e => e.EmployeeId)
                .IsUnique()
                .HasFilter("[EmployeeId] IS NOT NULL"); // SQL Server syntax for filtered unique index
            
            // String length constraints
            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.PhoneNumber).HasMaxLength(50);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.MiddleName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.Nickname).HasMaxLength(100);
            entity.Property(e => e.EmployeeId).HasMaxLength(50);
            entity.Property(e => e.Department).HasMaxLength(200);
            entity.Property(e => e.JobTitle).HasMaxLength(200);
            entity.Property(e => e.ProfileUrl).HasMaxLength(500);
            entity.Property(e => e.PictureUrl).HasMaxLength(500);
            entity.Property(e => e.Website).HasMaxLength(500);
            entity.Property(e => e.Address).HasColumnType("text"); // JSON string
            entity.Property(e => e.Birthdate).HasMaxLength(10); // YYYY-MM-DD
            entity.Property(e => e.Gender).HasMaxLength(50);
            entity.Property(e => e.TimeZone).HasMaxLength(100);
            entity.Property(e => e.Locale).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).IsRequired();
            
            // Phase 10.6: Identity verification fields (stored as SHA256 hash - 64 chars)
            entity.Property(e => e.NationalId).HasMaxLength(64);
            entity.Property(e => e.PassportNumber).HasMaxLength(64);
            entity.Property(e => e.ResidentCertificateNumber).HasMaxLength(64);
            entity.Property(e => e.IdentityDocumentType).HasMaxLength(30);
            
            // Phase 10.6: Unique indexes for identity fields (filtered to allow NULL values)
            entity.HasIndex(e => e.NationalId)
                .IsUnique()
                .HasFilter("[NationalId] IS NOT NULL"); // SQL Server syntax
            
            entity.HasIndex(e => e.PassportNumber)
                .IsUnique()
                .HasFilter("[PassportNumber] IS NOT NULL");
            
            entity.HasIndex(e => e.ResidentCertificateNumber)
                .IsUnique()
                .HasFilter("[ResidentCertificateNumber] IS NOT NULL");
            
            // Phase 18: Lifecycle management fields
            // Store Status as int (default EF behavior for enums)
            entity.Property(e => e.Status).IsRequired();
            entity.HasIndex(e => e.Status); // Index for filtering by status
            entity.HasIndex(e => e.IsDeleted); // Index for soft delete queries
            
            // Configure relationship: One Person can have Many ApplicationUsers
            entity.HasMany(p => p.Accounts)
                .WithOne(u => u.Person)
                .HasForeignKey(u => u.PersonId)
                .OnDelete(DeleteBehavior.SetNull); // When person is deleted, set PersonId to null in users
        });

        // Configure ClientOwnership entity (Phase 13)
        builder.Entity<ClientOwnership>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClientId).IsUnique(); // One client can only have one owner
            entity.Property(e => e.ClientId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.CreatedByPersonId).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            
            // Configure relationship with Person
            entity.HasOne(e => e.CreatedByPerson)
                .WithMany()
                .HasForeignKey(e => e.CreatedByPersonId)
                .OnDelete(DeleteBehavior.Cascade); // When person is deleted, remove ownership records
        });

        // Configure ScopeOwnership entity (Phase 13)
        builder.Entity<ScopeOwnership>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ScopeId).IsUnique(); // One scope can only have one owner
            entity.Property(e => e.ScopeId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.CreatedByPersonId).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            
            // Configure relationship with Person
            entity.HasOne(e => e.CreatedByPerson)
                .WithMany()
                .HasForeignKey(e => e.CreatedByPersonId)
                .OnDelete(DeleteBehavior.Cascade); // When person is deleted, remove ownership records
        });

        // Configure UserAppRole entity (Phase 22.1)
        builder.Entity<UserAppRole>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.ClientId, e.RoleName }).IsUnique();
            entity.Property(e => e.ClientId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.RoleName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasOne(e => e.User) // No navigation property on User side, so WithMany() is empty
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
