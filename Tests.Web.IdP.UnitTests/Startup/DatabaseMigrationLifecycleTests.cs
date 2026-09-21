using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Web.IdP.Startup;

namespace Tests.Web.IdP.UnitTests.Startup;

public sealed class DatabaseMigrationLifecycleTests
{
    [Fact]
    public void IsMigrateOnly_WithMigrateOnlyArgument_ReturnsTrue()
    {
        Assert.True(DatabaseMigrationLifecycle.IsMigrateOnly(["--migrate-only"]));
        Assert.False(DatabaseMigrationLifecycle.IsMigrateOnly([]));
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSQL")]
    public void IsSupportedProvider_WithConfiguredProvider_ReturnsTrue(string databaseProvider)
    {
        Assert.True(DatabaseMigrationLifecycle.IsSupportedProvider(databaseProvider));
    }

    [Fact]
    public void IsSupportedProvider_WithUnsupportedProvider_ReturnsFalse()
    {
        Assert.False(DatabaseMigrationLifecycle.IsSupportedProvider("unsupported-provider"));
    }

    [Theory]
    [InlineData("SqlServer", "SqlServerConnection")]
    [InlineData("PostgreSQL", "PostgreSqlConnection")]
    public void HasConfiguredConnectionString_WithSelectedProviderConnection_ReturnsTrue(
        string databaseProvider,
        string connectionStringName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{connectionStringName}"] = "configured"
            })
            .Build();

        Assert.True(DatabaseMigrationLifecycle.HasConfiguredConnectionString(configuration, databaseProvider));
    }

    [Fact]
    public void HasConfiguredConnectionString_WithoutSelectedConnection_ReturnsFalse()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.False(DatabaseMigrationLifecycle.HasConfiguredConnectionString(configuration, "SqlServer"));
    }

    [Fact]
    public void IsAutoMigrationEnabled_WithoutSetting_DefaultsToEnabled()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.True(DatabaseMigrationLifecycle.IsAutoMigrationEnabled(configuration));
    }

    [Fact]
    public void IsAutoMigrationEnabled_WithDisabledSetting_ReturnsFalse()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseMigrationLifecycle.ApplyOnStartupConfigurationKey] = "false"
            })
            .Build();

        Assert.False(DatabaseMigrationLifecycle.IsAutoMigrationEnabled(configuration));
    }

    [Fact]
    public async Task ApplyMigrationsAsync_WhenMigrationSucceeds_AppliesOnce()
    {
        var applied = 0;

        await DatabaseMigrationLifecycle.ApplyMigrationsAsync(
            _ =>
            {
                applied++;
                return Task.CompletedTask;
            },
            NullLogger.Instance);

        Assert.Equal(1, applied);
    }

    [Fact]
    public async Task ApplyMigrationsAsync_WhenMigrationFails_ThrowsSanitizedFailure()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseMigrationLifecycle.ApplyMigrationsAsync(
                _ => throw new InvalidOperationException("Server=database;Password=secret"),
                NullLogger.Instance));

        Assert.Equal("Database migration failed.", exception.Message);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyStartupPolicyAsync_WhenAutoMigrationEnabled_AppliesWithoutCheckingPendingMigrations()
    {
        var applied = 0;
        var pendingChecked = 0;

        await DatabaseMigrationLifecycle.ApplyStartupPolicyAsync(
            autoMigrationEnabled: true,
            _ =>
            {
                applied++;
                return Task.CompletedTask;
            },
            _ =>
            {
                pendingChecked++;
                return Task.FromResult<IEnumerable<string>>([]);
            },
            NullLogger.Instance);

        Assert.Equal(1, applied);
        Assert.Equal(0, pendingChecked);
    }

    [Fact]
    public async Task ApplyStartupPolicyAsync_WhenDisabledWithoutPendingMigrations_DoesNotApplyMigrations()
    {
        var applied = 0;

        await DatabaseMigrationLifecycle.ApplyStartupPolicyAsync(
            autoMigrationEnabled: false,
            _ =>
            {
                applied++;
                return Task.CompletedTask;
            },
            _ => Task.FromResult<IEnumerable<string>>([]),
            NullLogger.Instance);

        Assert.Equal(0, applied);
    }

    [Fact]
    public async Task ApplyStartupPolicyAsync_WhenDisabledWithPendingMigrations_ThrowsBeforeApplying()
    {
        var applied = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseMigrationLifecycle.ApplyStartupPolicyAsync(
                autoMigrationEnabled: false,
                _ =>
                {
                    applied++;
                    return Task.CompletedTask;
                },
                _ => Task.FromResult<IEnumerable<string>>(["pending"]),
                NullLogger.Instance));

        Assert.Equal("Database migrations are pending.", exception.Message);
        Assert.Equal(0, applied);
    }
}
