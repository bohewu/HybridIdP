using Microsoft.EntityFrameworkCore;
using Core.Domain; // Assuming ApplicationUser is in Core.Domain

namespace Core.Application;

public interface IApplicationDbContext
{
    DbSet<ApplicationUser> Users { get; }
    // Add other DbSet properties for your domain entities here

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
