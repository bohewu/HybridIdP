using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Core.Application;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Web.IdP.Controllers.Admin;

namespace Tests.Infrastructure.IntegrationTests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class OperationalAdminBootstrapProviderCollection
    : ICollectionFixture<OperationalAdminBootstrapProviderFixture>
{
    public const string CollectionName = "Operational bootstrap real providers";
}

[Collection(OperationalAdminBootstrapProviderCollection.CollectionName)]
public sealed class OperationalAdminBootstrapProviderTests(
    OperationalAdminBootstrapProviderFixture fixture)
{
    [Theory]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer, true)]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql, false)]
    public async Task SetValueAsync_ShouldReserveMarkerUsingProviderEquality(
        string providerName,
        bool casingVariantsAreReserved)
    {
        var database = fixture.GetDatabase(providerName);
        await database.ResetAsync();
        var casingVariants = new[]
        {
            SettingKeys.OperationalAdminBootstrapCompleted.ToUpperInvariant(),
            "system.operationaladminbootstrap.completed"
        };

        await using var services = database.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await settingsService.SetValueAsync(
            SettingKeys.Branding.AppName,
            "Provider equality regression",
            "provider-test");

        await Assert.ThrowsAsync<SystemManagedSettingException>(() =>
            settingsService.SetValueAsync(
                SettingKeys.OperationalAdminBootstrapCompleted,
                "blocked",
                "provider-test"));

        foreach (var candidate in casingVariants)
        {
            if (casingVariantsAreReserved)
            {
                await Assert.ThrowsAsync<SystemManagedSettingException>(() =>
                    settingsService.SetValueAsync(candidate, "blocked", "provider-test"));
            }
            else
            {
                await settingsService.SetValueAsync(candidate, "allowed", "provider-test");
            }
        }

        dbContext.Settings.Add(new Setting
        {
            Id = Guid.NewGuid(),
            Key = SettingKeys.OperationalAdminBootstrapCompleted,
            Value = "completed"
        });
        await dbContext.SaveChangesAsync();

        foreach (var candidate in casingVariants)
        {
            if (casingVariantsAreReserved)
            {
                await Assert.ThrowsAsync<SystemManagedSettingException>(() =>
                    settingsService.SetValueAsync(candidate, "blocked", "provider-test"));
            }
            else
            {
                await settingsService.SetValueAsync(
                    candidate,
                    "allowed-after-marker",
                    "provider-test");
            }
        }

        await Assert.ThrowsAsync<SystemManagedSettingException>(() =>
            settingsService.SetValueAsync(
                SettingKeys.OperationalAdminBootstrapCompleted,
                "blocked",
                "provider-test"));

        var exactMarker = await dbContext.Settings
            .AsNoTracking()
            .SingleAsync(setting =>
                setting.Key == SettingKeys.OperationalAdminBootstrapCompleted);
        Assert.Equal("completed", exactMarker.Value);
        Assert.Equal(
            "Provider equality regression",
            await settingsService.GetValueAsync(SettingKeys.Branding.AppName));

        var markerLikeSettings = await dbContext.Settings
            .AsNoTracking()
            .ToListAsync();
        var markerKeys = casingVariants
            .Append(SettingKeys.OperationalAdminBootstrapCompleted)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            casingVariantsAreReserved ? 1 : 3,
            markerLikeSettings.Count(setting => markerKeys.Contains(setting.Key)));
    }

    [Theory]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer, true)]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql, false)]
    public async Task UpdateSetting_ShouldApplyProviderEqualityToSystemManagedMarker(
        string providerName,
        bool shouldReject)
    {
        var database = fixture.GetDatabase(providerName);
        await database.ResetAsync();
        var providerEquivalentKey =
            SettingKeys.OperationalAdminBootstrapCompleted.ToUpperInvariant();

        await using var services = database.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Settings.Add(new Setting
        {
            Id = Guid.NewGuid(),
            Key = SettingKeys.OperationalAdminBootstrapCompleted,
            Value = "completed"
        });
        await dbContext.SaveChangesAsync();

        var emailOptions = new Mock<IOptionsSnapshot<EmailOptions>>();
        emailOptions.Setup(options => options.Value).Returns(new EmailOptions());
        var controller = new SettingsController(
            settingsService,
            Mock.Of<IEmailService>(),
            new ConfigurationBuilder().Build(),
            emailOptions.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.UpdateSetting(
            providerEquivalentKey,
            new UpdateSettingRequest("tampered"));
        var storedSettings = await dbContext.Settings
            .AsNoTracking()
            .ToListAsync();
        var exactMarker = Assert.Single(storedSettings, setting =>
            string.Equals(
                setting.Key,
                SettingKeys.OperationalAdminBootstrapCompleted,
                StringComparison.Ordinal));
        Assert.Equal("completed", exactMarker.Value);

        if (shouldReject)
        {
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
            var payload = JsonSerializer.SerializeToElement(badRequest.Value);
            Assert.Equal(
                "System-managed settings cannot be modified",
                payload.GetProperty("error").GetString());
            Assert.DoesNotContain(storedSettings, setting =>
                string.Equals(
                    setting.Key,
                    providerEquivalentKey,
                    StringComparison.Ordinal));
        }
        else
        {
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
            var providerSpecificSetting = Assert.Single(storedSettings, setting =>
                string.Equals(
                    setting.Key,
                    providerEquivalentKey,
                    StringComparison.Ordinal));
            Assert.Equal("tampered", providerSpecificSetting.Value);
        }
    }

    [Theory]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer)]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql)]
    public async Task ConcurrentValidAttempts_ShouldCommitExactlyOneWithoutLosingResidue(
        string providerName)
    {
        var database = fixture.GetDatabase(providerName);
        await database.ResetAsync();
        var commands = new[] { CreateCommand(), CreateCommand() };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = commands.Select(command => Task.Run(async () =>
        {
            await using var services = database.CreateServices();
            await using var scope = services.CreateAsyncScope();
            var service = scope.ServiceProvider
                .GetRequiredService<IOperationalAdminBootstrapService>();
            await release.Task;
            return await service.BootstrapAsync(command);
        })).ToArray();

        release.SetResult();
        var results = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(45));
        var snapshot = await database.SnapshotAsync(commands);

        Assert.Equal(1, results.Count(result => result.Succeeded));
        var successfulIndex = Array.FindIndex(
            results,
            result => result.Succeeded);
        AssertCommittedBootstrap(snapshot, successfulIndex);
        for (var index = 0; index < results.Length; index++)
        {
            if (index != successfulIndex)
            {
                AssertNoIdentityResidue(snapshot.Identities[index]);
            }
        }
    }

    [Theory]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer)]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql)]
    public async Task IndependentIdentityWriteRacingFreshness_ShouldPreserveSerializableOwnership(
        string providerName)
    {
        var database = fixture.GetDatabase(providerName);
        await database.ResetAsync();
        var bootstrapCommand = CreateCommand();
        var normalIdentity = CreateCommand();
        var freshnessGate = new FreshnessGateInterceptor();
        var normalRead = new NormalIdentityReadInterceptor();

        await using var bootstrapServices = database.CreateServices(freshnessGate);
        await using var normalServices = database.CreateServices(normalRead);

        var bootstrapTask = Task.Run(async () =>
        {
            await using var scope = bootstrapServices.CreateAsyncScope();
            var service = scope.ServiceProvider
                .GetRequiredService<IOperationalAdminBootstrapService>();
            return await service.BootstrapAsync(bootstrapCommand);
        });

        await freshnessGate.Reached.WaitAsync(TimeSpan.FromSeconds(20));
        var normalTask = Task.Run(async () =>
        {
            await using var scope = normalServices.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var now = DateTime.UtcNow;
            var person = new Person
            {
                Id = Guid.NewGuid(),
                Email = normalIdentity.Email,
                FirstName = normalIdentity.Name,
                CreatedAt = now
            };
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = normalIdentity.Email,
                UserName = normalIdentity.Email,
                FirstName = normalIdentity.Name,
                PersonId = person.Id,
                IsActive = true,
                EmailConfirmed = true,
                LockoutEnabled = true,
                CreatedAt = now
            };

            dbContext.Persons.Add(person);
            return (await userManager.CreateAsync(user, normalIdentity.Password)).Succeeded;
        });

        await normalRead.Reached.WaitAsync(TimeSpan.FromSeconds(20));
        freshnessGate.Release();

        var bootstrapResult = await bootstrapTask.WaitAsync(TimeSpan.FromSeconds(45));
        var normalSucceeded = await normalTask.WaitAsync(TimeSpan.FromSeconds(45));
        var racedSnapshot = await database.SnapshotAsync(
            [bootstrapCommand, normalIdentity]);
        var bootstrapIdentity = racedSnapshot.Identities[0];
        var ordinaryIdentity = racedSnapshot.Identities[1];

        if (bootstrapResult.Succeeded)
        {
            AssertBootstrapIdentity(bootstrapIdentity);
            Assert.Equal(1, racedSnapshot.AdminMemberships);
            Assert.Equal(1, racedSnapshot.Markers);
            Assert.Equal(1, racedSnapshot.SuccessAudits);

            if (normalSucceeded)
            {
                AssertOrdinaryIdentity(ordinaryIdentity);
            }
            else
            {
                AssertNoIdentityResidue(ordinaryIdentity);
            }
        }
        else
        {
            Assert.True(
                normalSucceeded,
                $"{providerName} did not produce a successful serial ordering.");
            AssertNoIdentityResidue(bootstrapIdentity);
            AssertOrdinaryIdentity(ordinaryIdentity);
            Assert.Equal(0, racedSnapshot.AdminMemberships);
            Assert.Equal(0, racedSnapshot.Markers);
            Assert.Equal(0, racedSnapshot.SuccessAudits);
        }

        Assert.Equal(
            bootstrapIdentity.Users + ordinaryIdentity.Users,
            racedSnapshot.Users);
        Assert.Equal(
            bootstrapIdentity.Persons + ordinaryIdentity.Persons,
            racedSnapshot.Persons);
        Assert.Equal(0, racedSnapshot.OtherIdentityResidue);
        Assert.True(racedSnapshot.AuditsAreSecretFree);

        OperationalAdminBootstrapResult replayResult;
        await using (var replayServices = database.CreateServices())
        await using (var replayScope = replayServices.CreateAsyncScope())
        {
            replayResult = await replayScope.ServiceProvider
                .GetRequiredService<IOperationalAdminBootstrapService>()
                .BootstrapAsync(bootstrapCommand);
        }

        var replaySnapshot = await database.SnapshotAsync(
            [bootstrapCommand, normalIdentity]);
        Assert.False(replayResult.Succeeded);
        Assert.Equal(
            racedSnapshot.AdminMemberships,
            replaySnapshot.AdminMemberships);
        Assert.Equal(racedSnapshot.Markers, replaySnapshot.Markers);
        Assert.Equal(
            racedSnapshot.Identities[0].AdminMemberships,
            replaySnapshot.Identities[0].AdminMemberships);
    }

    [Theory]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer, "AspNetUserRoles", false)]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer, "Settings", false)]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer, "AspNetUserRoles", true)]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql, "AspNetUserRoles", false)]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql, "Settings", false)]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql, "AspNetUserRoles", true)]
    public async Task FailureOrCancellationDuringPrivilegedWrites_ShouldRollbackAllState(
        string providerName,
        string targetTable,
        bool cancel)
    {
        var database = fixture.GetDatabase(providerName);
        await database.ResetAsync();
        using var cancellation = new CancellationTokenSource();
        var interceptor = new MutationFailureInterceptor(
            targetTable,
            cancel ? cancellation : null);
        var command = CreateCommand();

        await using var services = database.CreateServices(interceptor);
        await using var scope = services.CreateAsyncScope();
        var service = scope.ServiceProvider
            .GetRequiredService<IOperationalAdminBootstrapService>();

        var result = await service.BootstrapAsync(command, cancellation.Token);
        var snapshot = await database.SnapshotAsync([command]);

        Assert.False(result.Succeeded);
        AssertEmptySnapshot(snapshot);
        Assert.True(interceptor.Triggered, $"The {targetTable} failure boundary was not reached.");
    }

    [Theory]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer)]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql)]
    public async Task LostSuccessResponseThenReplay_ShouldRemainClosed(
        string providerName)
    {
        var database = fixture.GetDatabase(providerName);
        await database.ResetAsync();
        var command = CreateCommand();

        OperationalAdminBootstrapResult firstResult;
        await using (var firstServices = database.CreateServices())
        await using (var firstScope = firstServices.CreateAsyncScope())
        {
            firstResult = await firstScope.ServiceProvider
                .GetRequiredService<IOperationalAdminBootstrapService>()
                .BootstrapAsync(command);
        }

        OperationalAdminBootstrapResult replayResult;
        await using (var replayServices = database.CreateServices())
        await using (var replayScope = replayServices.CreateAsyncScope())
        {
            replayResult = await replayScope.ServiceProvider
                .GetRequiredService<IOperationalAdminBootstrapService>()
                .BootstrapAsync(command);
        }

        var snapshot = await database.SnapshotAsync([command]);
        Assert.True(firstResult.Succeeded);
        Assert.False(replayResult.Succeeded);
        AssertCommittedBootstrap(snapshot, 0);
    }

    private static OperationalAdminBootstrapCommand CreateCommand()
    {
        var id = Guid.NewGuid().ToString("N");
        return new OperationalAdminBootstrapCommand(
            $"bootstrap-{id}@example.invalid",
            $"Bootstrap Operator {id}",
            $"A!a1{id}",
            Guid.NewGuid().ToString("N"));
    }

    private static void AssertCommittedBootstrap(
        ProviderSnapshot snapshot,
        int bootstrapIdentityIndex)
    {
        Assert.Equal(1, snapshot.Users);
        Assert.Equal(1, snapshot.Persons);
        Assert.Equal(1, snapshot.LinkedActiveUsers);
        Assert.Equal(1, snapshot.AdminMemberships);
        Assert.Equal(1, snapshot.Markers);
        Assert.Equal(1, snapshot.SuccessAudits);
        Assert.Equal(0, snapshot.OtherIdentityResidue);
        Assert.True(snapshot.AuditsAreSecretFree);
        AssertBootstrapIdentity(snapshot.Identities[bootstrapIdentityIndex]);
    }

    private static void AssertEmptySnapshot(ProviderSnapshot snapshot)
    {
        Assert.Equal(0, snapshot.Users);
        Assert.Equal(0, snapshot.Persons);
        Assert.Equal(0, snapshot.AdminMemberships);
        Assert.Equal(0, snapshot.Markers);
        Assert.Equal(0, snapshot.SuccessAudits);
        Assert.Equal(0, snapshot.OtherIdentityResidue);
        Assert.All(snapshot.Identities, AssertNoIdentityResidue);
    }

    private static void AssertBootstrapIdentity(ProviderIdentitySnapshot identity)
    {
        Assert.Equal(1, identity.Users);
        Assert.Equal(1, identity.Persons);
        Assert.Equal(1, identity.LinkedActiveUsers);
        Assert.Equal(1, identity.AdminMemberships);
        Assert.Equal(1, identity.SuccessAudits);
    }

    private static void AssertOrdinaryIdentity(ProviderIdentitySnapshot identity)
    {
        Assert.Equal(1, identity.Users);
        Assert.Equal(1, identity.Persons);
        Assert.Equal(1, identity.LinkedActiveUsers);
        Assert.Equal(0, identity.AdminMemberships);
        Assert.Equal(0, identity.SuccessAudits);
    }

    private static void AssertNoIdentityResidue(ProviderIdentitySnapshot identity)
    {
        Assert.Equal(0, identity.Users);
        Assert.Equal(0, identity.Persons);
        Assert.Equal(0, identity.LinkedActiveUsers);
        Assert.Equal(0, identity.AdminMemberships);
        Assert.Equal(0, identity.SuccessAudits);
    }
}

public sealed class OperationalAdminBootstrapProviderFixture : IAsyncLifetime
{
    public const string SqlServer = "SqlServer";
    public const string PostgreSql = "PostgreSQL";
    private readonly string _resourceSuffix = Guid.NewGuid().ToString("N");
    private MsSqlContainer? _sqlServerContainer;
    private PostgreSqlContainer? _postgreSqlContainer;

    public ProviderDatabase SqlServerDatabase { get; private set; } = null!;
    public ProviderDatabase PostgreSqlDatabase { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _sqlServerContainer = new MsSqlBuilder(
                "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithName($"oss-bootstrap-t3-mssql-{_resourceSuffix}")
            .WithLabel("hybridauth.test-run", "20260728-oss-admin-bootstrap-t3")
            .Build();
        _postgreSqlContainer = new PostgreSqlBuilder("postgres:17-alpine")
            .WithName($"oss-bootstrap-t3-postgres-{_resourceSuffix}")
            .WithLabel("hybridauth.test-run", "20260728-oss-admin-bootstrap-t3")
            .WithDatabase("bootstrap_t3")
            .Build();

        try
        {
            await _sqlServerContainer.StartAsync();
            await CreateSqlServerDatabaseAsync(_sqlServerContainer);
            await _postgreSqlContainer.StartAsync();

            SqlServerDatabase = new ProviderDatabase(
                SqlServer,
                GetSqlServerDatabaseConnectionString(_sqlServerContainer),
                "Infrastructure.Migrations.SqlServer");
            PostgreSqlDatabase = new ProviderDatabase(
                PostgreSql,
                _postgreSqlContainer.GetConnectionString(),
                "Infrastructure.Migrations.Postgres");
        }
        catch
        {
            await DisposeContainersAsync();
            throw;
        }
    }

    public ProviderDatabase GetDatabase(string providerName) =>
        providerName switch
        {
            SqlServer => SqlServerDatabase,
            PostgreSql => PostgreSqlDatabase,
            _ => throw new ArgumentOutOfRangeException(nameof(providerName))
        };

    public async Task DisposeAsync() => await DisposeContainersAsync();

    private async Task DisposeContainersAsync()
    {
        if (_postgreSqlContainer is not null)
        {
            await _postgreSqlContainer.DisposeAsync();
        }

        if (_sqlServerContainer is not null)
        {
            await _sqlServerContainer.DisposeAsync();
        }
    }

    private async Task CreateSqlServerDatabaseAsync(MsSqlContainer container)
    {
        await using var connection = new SqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [bootstrap_t3_{_resourceSuffix}]";
        await command.ExecuteNonQueryAsync();
    }

    private string GetSqlServerDatabaseConnectionString(MsSqlContainer container)
    {
        var builder = new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = $"bootstrap_t3_{_resourceSuffix}"
        };
        return builder.ConnectionString;
    }
}

public sealed class ProviderDatabase(
    string providerName,
    string connectionString,
    string migrationsAssembly)
{
    private const string MarkerKey = "system.operationalAdminBootstrap.completed";

    public async Task ResetAsync()
    {
        await using var dbContext = new ApplicationDbContext(CreateOptions());
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
        dbContext.Roles.Add(new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = AuthConstants.Roles.Admin,
            NormalizedName = "ADMIN",
            IsSystem = true,
            Permissions = string.Join(',', Permissions.GetAll())
        });
        await dbContext.SaveChangesAsync();
    }

    public ServiceProvider CreateServices(params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IDataProtectionProvider, EphemeralDataProtectionProvider>();
        services.AddDbContext<ApplicationDbContext>(
            options => Configure(options, interceptors));
        services.AddScoped<IApplicationDbContext>(
            provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IOperationalAdminBootstrapService,
            OperationalAdminBootstrapService>();
        return services.BuildServiceProvider();
    }

    public async Task<ProviderSnapshot> SnapshotAsync(
        IReadOnlyCollection<OperationalAdminBootstrapCommand> commands)
    {
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = await dbContext.Users.AsNoTracking().ToListAsync();
        var persons = await dbContext.Persons.AsNoTracking().ToListAsync();
        var adminRoleIds = await dbContext.Roles
            .Where(role => role.Name == AuthConstants.Roles.Admin)
            .Select(role => role.Id)
            .ToListAsync();
        var userRoles = await dbContext.UserRoles.AsNoTracking().ToListAsync();
        var audits = await dbContext.AuditEvents.AsNoTracking().ToListAsync();
        var identities = commands.Select(command =>
        {
            var identityUsers = users
                .Where(user => string.Equals(
                    user.Email,
                    command.Email,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            var identityPersons = persons
                .Where(person => string.Equals(
                    person.Email,
                    command.Email,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            return new ProviderIdentitySnapshot(
                identityUsers.Count,
                identityPersons.Count,
                identityUsers.Count(user =>
                    user.IsActive &&
                    !user.IsDeleted &&
                    user.PersonId.HasValue &&
                    identityPersons.Any(person =>
                        person.Id == user.PersonId &&
                        person.CanAuthenticate())),
                userRoles.Count(membership =>
                    identityUsers.Any(user => user.Id == membership.UserId) &&
                    adminRoleIds.Contains(membership.RoleId)),
                audits.Count(audit =>
                    audit.EventType == "OperationalAdminBootstrapCompleted" &&
                    !string.IsNullOrEmpty(audit.Details) &&
                    audit.Details.Contains(
                        command.CorrelationId,
                        StringComparison.Ordinal)));
        }).ToArray();
        var sensitiveValues = commands
            .SelectMany(command => new[]
            {
                command.Email,
                command.Name,
                command.Password
            })
            .ToArray();
        var auditsAreSecretFree = audits.All(audit =>
            sensitiveValues.All(value =>
                string.IsNullOrEmpty(audit.Details) ||
                !audit.Details.Contains(value, StringComparison.Ordinal)));
        var linkedActiveUsers = users.Count(user =>
            user.IsActive &&
            !user.IsDeleted &&
            user.PersonId.HasValue &&
            persons.Any(person =>
                person.Id == user.PersonId &&
                person.CanAuthenticate()));
        var otherResidue =
            await dbContext.UserClaims.CountAsync() +
            await dbContext.UserLogins.CountAsync() +
            await dbContext.UserTokens.CountAsync() +
            await dbContext.UserSessions.CountAsync() +
            await dbContext.UserCredentials.CountAsync() +
            await dbContext.LoginHistories.CountAsync() +
            await dbContext.UserAppRoles.CountAsync() +
            await dbContext.ClientOwnerships.CountAsync() +
            await dbContext.ScopeOwnerships.CountAsync();

        return new ProviderSnapshot(
            users.Count,
            persons.Count,
            linkedActiveUsers,
            await dbContext.UserRoles.CountAsync(
                membership => adminRoleIds.Contains(membership.RoleId)),
            await dbContext.Settings.CountAsync(setting => setting.Key == MarkerKey),
            audits.Count(audit =>
                audit.EventType == "OperationalAdminBootstrapCompleted"),
            otherResidue,
            auditsAreSecretFree,
            identities);
    }

    private DbContextOptions<ApplicationDbContext> CreateOptions(
        params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        Configure(builder, interceptors);
        return builder.Options;
    }

    private void Configure(
        DbContextOptionsBuilder options,
        IReadOnlyCollection<IInterceptor> interceptors)
    {
        if (providerName == OperationalAdminBootstrapProviderFixture.PostgreSql)
        {
            options.UseNpgsql(
                connectionString,
                provider => provider.MigrationsAssembly(migrationsAssembly));
        }
        else
        {
            options.UseSqlServer(
                connectionString,
                provider => provider.MigrationsAssembly(migrationsAssembly));
        }

        options.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        options.UseOpenIddict<Guid>();
        if (interceptors.Count > 0)
        {
            options.AddInterceptors(interceptors);
        }
    }
}

public sealed record ProviderSnapshot(
    int Users,
    int Persons,
    int LinkedActiveUsers,
    int AdminMemberships,
    int Markers,
    int SuccessAudits,
    int OtherIdentityResidue,
    bool AuditsAreSecretFree,
    IReadOnlyList<ProviderIdentitySnapshot> Identities);

public sealed record ProviderIdentitySnapshot(
    int Users,
    int Persons,
    int LinkedActiveUsers,
    int AdminMemberships,
    int SuccessAudits);

internal sealed class FreshnessGateInterceptor : DbCommandInterceptor
{
    private readonly TaskCompletionSource _reached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Reached => _reached.Task;

    public void Release() => _release.TrySetResult();

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("ScopeOwnerships", StringComparison.Ordinal) &&
            command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            _reached.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        return result;
    }
}

internal sealed class NormalIdentityReadInterceptor : DbCommandInterceptor
{
    private readonly TaskCompletionSource _reached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Reached => _reached.Task;

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("AspNetUsers", StringComparison.Ordinal) &&
            command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            _reached.TrySetResult();
        }

        return ValueTask.FromResult(result);
    }
}

internal sealed class MutationFailureInterceptor(
    string targetTable,
    CancellationTokenSource? cancellation) : DbCommandInterceptor
{
    private int _triggered;

    public bool Triggered => Volatile.Read(ref _triggered) == 1;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains(targetTable, StringComparison.Ordinal) &&
            Interlocked.CompareExchange(ref _triggered, 1, 0) == 0)
        {
            if (cancellation is not null)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }

            throw new InvalidOperationException("Injected provider write failure.");
        }

        return ValueTask.FromResult(result);
    }
}
