using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

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
    DbSet<UserClaim> IApplicationDbContext.UserClaims => Set<UserClaim>();
    public DbSet<ScopeClaim> ScopeClaims => Set<ScopeClaim>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<SecurityPolicy> SecurityPolicies { get; set; } = default!;
    public DbSet<LoginHistory> LoginHistories { get; set; } = default!;
    public DbSet<ScopeExtension> ScopeExtensions => Set<ScopeExtension>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ApiResource> ApiResources => Set<ApiResource>();
    public DbSet<ApiResourceScope> ApiResourceScopes => Set<ApiResourceScope>();
    public DbSet<ClientRequiredScope> ClientRequiredScopes => Set<ClientRequiredScope>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Person> Persons => Set<Person>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        
        // Configure OpenIddict to use the default ASP.NET Core Identity entity types
        builder.UseOpenIddict<Guid>();
        
        // Configure UserClaim entity
        builder.Entity<UserClaim>(entity =>
        {
            entity.HasKey(e => e.Id);
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
            entity.HasIndex(e => new { e.ScopeId, e.UserClaimId }).IsUnique();
            entity.Property(e => e.ScopeId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ScopeName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.CustomMappingLogic).HasMaxLength(1000);
            
            // Configure relationship with UserClaim
            entity.HasOne(e => e.UserClaim)
                .WithMany(c => c.ScopeClaims)
                .HasForeignKey(e => e.UserClaimId)
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
            entity.Property(e => e.Value).HasColumnType("text").IsRequired();
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
            
            // Phase 10.6: Identity verification fields
            entity.Property(e => e.NationalId).HasMaxLength(20);
            entity.Property(e => e.PassportNumber).HasMaxLength(20);
            entity.Property(e => e.ResidentCertificateNumber).HasMaxLength(20);
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
            
            // Configure relationship: One Person can have Many ApplicationUsers
            entity.HasMany(p => p.Accounts)
                .WithOne(u => u.Person)
                .HasForeignKey(u => u.PersonId)
                .OnDelete(DeleteBehavior.SetNull); // When person is deleted, set PersonId to null in users
        });
        
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
