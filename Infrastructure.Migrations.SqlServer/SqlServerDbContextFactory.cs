using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Infrastructure;

namespace Infrastructure.Migrations.SqlServer;

/// <summary>
/// Design-time factory for SQL Server migrations.
/// This is used by EF Core tools to create the DbContext for migrations.
/// </summary>
public class SqlServerDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerConnection")
            ?? "Server=localhost,1433;Database=hybridauth_idp;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False";

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsAssembly("Infrastructure.Migrations.SqlServer");
        });

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
