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
    public DbSet<ScopeExtension> ScopeExtensions => Set<ScopeExtension>();
    public DbSet<Resource> Resources => Set<Resource>();

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
            entity.Property(e => e.ConsentDisplayName).HasMaxLength(200);
            entity.Property(e => e.ConsentDescription).HasMaxLength(1000);
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
        
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
