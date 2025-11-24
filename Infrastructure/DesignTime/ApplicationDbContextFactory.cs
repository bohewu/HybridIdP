using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.DesignTime;

/// <summary>
/// Design-time factory for ApplicationDbContext.
/// Reads DATABASE_PROVIDER environment variable (default: "SqlServer") to determine which provider to use.
/// Set DATABASE_PROVIDER=SqlServer for SQL Server migrations.
/// Set DATABASE_PROVIDER=PostgreSQL for PostgreSQL migrations.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Default to SqlServer if not specified
        var databaseProvider = Environment.GetEnvironmentVariable("DATABASE_PROVIDER") ?? "SqlServer";
        
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        if (databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSqlConnection")
                ?? "Host=localhost;Port=5432;Database=hybridauth_idp;Username=user;Password=password";
            
            optionsBuilder.UseNpgsql(connectionString);
        }
        else
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerConnection")
                ?? "Server=localhost,1433;Database=hybridauth_idp;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False";
            
            optionsBuilder.UseSqlServer(connectionString);
        }

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
