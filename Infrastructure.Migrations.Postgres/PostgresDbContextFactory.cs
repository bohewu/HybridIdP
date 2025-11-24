using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Infrastructure;

namespace Infrastructure.Migrations.Postgres;

/// <summary>
/// Design-time factory for PostgreSQL migrations.
/// This is used by EF Core tools to create the DbContext for migrations.
/// </summary>
public class PostgresDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSqlConnection")
            ?? "Host=localhost;Port=5432;Database=hybridauth_idp;Username=user;Password=password";

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.MigrationsAssembly("Infrastructure.Migrations.Postgres");
        });

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
