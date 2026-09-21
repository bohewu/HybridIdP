using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Web.IdP.Startup;

public static class DatabaseMigrationLifecycle
{
    public const string ApplyOnStartupConfigurationKey = "DatabaseMigration:ApplyOnStartup";

    public static bool IsMigrateOnly(IEnumerable<string> args) =>
        args.Any(argument => string.Equals(argument, "--migrate-only", StringComparison.Ordinal));

    public static bool IsSupportedProvider(string databaseProvider) =>
        databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ||
        databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase);

    public static bool HasConfiguredConnectionString(
        IConfiguration configuration,
        string databaseProvider) =>
        !string.IsNullOrWhiteSpace(configuration.GetConnectionString(
            databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase)
                ? "PostgreSqlConnection"
                : "SqlServerConnection"));

    public static bool IsAutoMigrationEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool?>(ApplyOnStartupConfigurationKey) ?? true;

    public static async Task ApplyMigrationsAsync(
        Func<CancellationToken, Task> applyMigrationsAsync,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await applyMigrationsAsync(cancellationToken);
            logger.LogInformation("Database migration completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError("Database migration failed. Category: execution.");
            throw new InvalidOperationException("Database migration failed.");
        }
    }

    public static async Task ApplyStartupPolicyAsync(
        bool autoMigrationEnabled,
        Func<CancellationToken, Task> applyMigrationsAsync,
        Func<CancellationToken, Task<IEnumerable<string>>> getPendingMigrationsAsync,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (autoMigrationEnabled)
        {
            await ApplyMigrationsAsync(applyMigrationsAsync, logger, cancellationToken);
            return;
        }

        IEnumerable<string> pendingMigrations;
        try
        {
            pendingMigrations = await getPendingMigrationsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError("Database migration check failed. Category: inspection.");
            throw new InvalidOperationException("Database migration check failed.");
        }

        if (pendingMigrations.Any())
        {
            logger.LogCritical("Database migration check failed. Category: pending.");
            throw new InvalidOperationException("Database migrations are pending.");
        }

        logger.LogInformation("Database migration check completed.");
    }
}
