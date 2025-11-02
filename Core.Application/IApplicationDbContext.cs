using Microsoft.EntityFrameworkCore;
using Core.Domain; // Assuming ApplicationUser is in Core.Domain
using Core.Domain.Entities;

namespace Core.Application;

public interface IApplicationDbContext
{
    DbSet<ApplicationUser> Users { get; }
    DbSet<UserClaim> UserClaims { get; }
    DbSet<ScopeClaim> ScopeClaims { get; }
    // Add other DbSet properties for your domain entities here

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
