using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Web.IdP.Middleware;

namespace Tests.SystemTests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class OperationalAdminBootstrapRealHostCollection
    : ICollectionFixture<OperationalAdminBootstrapRealHostFixture>
{
    public const string CollectionName = "Operational bootstrap real host";
}

[Collection(OperationalAdminBootstrapRealHostCollection.CollectionName)]
public sealed class OperationalAdminBootstrapLoginSystemTests(
    OperationalAdminBootstrapRealHostFixture fixture)
{
    [Fact]
    public async Task DirectHttpsBootstrap_ShouldCreateLoginCompatibleAdministrator()
    {
        await fixture.ResetAsync();
        var request = HostBootstrapRequest.Create();
        await using var factory = await OperationalBootstrapHostFactory.CreateAsync(
            fixture.ConnectionString,
            request,
            proxyEnabled: false,
            knownProxies: null,
            remoteAddress: IPAddress.Parse("203.0.113.77"));

        using var response = await factory.PostBootstrapAsync(
            useHttps: true,
            forwardedProto: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await AssertMachineResponseAsync(
            response,
            "operational_bootstrap_completed",
            request);

        using var loginPage = await factory.Client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var antiForgeryToken = ExtractAntiForgeryToken(loginHtml);
        using var loginForm = new FormUrlEncodedContent(
        [
            new("Input.Login", request.Email),
            new("Input.Password", request.Password),
            new("__RequestVerificationToken", antiForgeryToken)
        ]);

        using var loginResponse = await factory.Client.PostAsync(
            "/Account/Login",
            loginForm);
        var cookieCount = loginResponse.Headers.TryGetValues(
            "Set-Cookie",
            out var cookies)
            ? cookies.Count()
            : 0;
        var snapshot = await fixture.SnapshotAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.True(cookieCount > 0, "Normal login did not issue an authentication cookie.");
        Assert.Equal(1, snapshot.Users);
        Assert.Equal(1, snapshot.Persons);
        Assert.Equal(1, snapshot.LinkedActiveUsers);
        Assert.Equal(1, snapshot.AdminMemberships);
        Assert.Equal(1, snapshot.Markers);
        Assert.Equal(1, snapshot.SuccessAudits);
        Assert.True(snapshot.AuditsAreSecretFree);
    }

    [Theory]
    [InlineData(true, true, true, "127.0.0.1")]
    [InlineData(false, false, false, null)]
    [InlineData(false, true, false, "127.0.0.1")]
    public async Task ForwardedAndPlainHttpShapes_ShouldRespectTrustedHttpsBoundary(
        bool trustedProxy,
        bool sendForwardedHttps,
        bool shouldSucceed,
        string? knownProxies)
    {
        await fixture.ResetAsync();
        var request = HostBootstrapRequest.Create();
        var remoteAddress = trustedProxy
            ? IPAddress.Loopback
            : IPAddress.Parse("203.0.113.88");
        await using var factory = await OperationalBootstrapHostFactory.CreateAsync(
            fixture.ConnectionString,
            request,
            proxyEnabled: sendForwardedHttps,
            knownProxies,
            remoteAddress);

        using var response = await factory.PostBootstrapAsync(
            useHttps: false,
            forwardedProto: sendForwardedHttps ? "https" : null);
        var snapshot = await fixture.SnapshotAsync(request);

        if (shouldSucceed)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            await AssertMachineResponseAsync(
                response,
                "operational_bootstrap_completed",
                request);
            Assert.Equal(1, snapshot.Users);
            Assert.Equal(1, snapshot.AdminMemberships);
            Assert.Equal(1, snapshot.Markers);
        }
        else
        {
            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(0, snapshot.Users);
            Assert.Equal(0, snapshot.Persons);
            Assert.Equal(0, snapshot.AdminMemberships);
            Assert.Equal(0, snapshot.Markers);
        }
    }

    private static async Task AssertMachineResponseAsync(
        HttpResponseMessage response,
        string expectedCode,
        HostBootstrapRequest request)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        var containsSensitive =
            body.Contains(request.Email, StringComparison.Ordinal) ||
            body.Contains(request.Name, StringComparison.Ordinal) ||
            body.Contains(request.Password, StringComparison.Ordinal) ||
            body.Contains(request.Token, StringComparison.Ordinal) ||
            body.Contains(Digest(request.Token), StringComparison.OrdinalIgnoreCase);
        Assert.False(containsSensitive, "Bootstrap response contained sensitive request data.");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.True(root.TryGetProperty("correlationId", out var correlation));
        Assert.False(string.IsNullOrWhiteSpace(correlation.GetString()));
        Assert.Equal(2, root.EnumerateObject().Count());
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        if (!match.Success)
        {
            match = Regex.Match(
                html,
                @"value=""([^""]+)""[^>]*name=""__RequestVerificationToken""");
        }

        Assert.True(match.Success, "Login page did not contain an antiforgery token.");
        return match.Groups[1].Value;
    }

    private static string Digest(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed class OperationalAdminBootstrapRealHostFixture : IAsyncLifetime
{
    private readonly string _resourceSuffix = Guid.NewGuid().ToString("N");
    private PostgreSqlContainer? _container;

    public string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("The disposable PostgreSQL fixture is not running.");

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithName($"oss-bootstrap-t3-host-postgres-{_resourceSuffix}")
            .WithLabel("hybridauth.test-run", "20260728-oss-admin-bootstrap-t3")
            .WithDatabase("bootstrap_host_t3")
            .Build();
        try
        {
            await _container.StartAsync();
        }
        catch
        {
            await _container.DisposeAsync();
            throw;
        }
    }

    public async Task ResetAsync()
    {
        await using var dbContext = CreateDbContext();
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
        dbContext.SecurityPolicies.Add(new SecurityPolicy
        {
            Id = Guid.NewGuid(),
            MinPasswordLength = 8,
            RequireUppercase = true,
            RequireLowercase = true,
            RequireDigit = true,
            RequireNonAlphanumeric = true,
            PasswordHistoryCount = 5,
            PasswordExpirationDays = 180,
            MaxFailedAccessAttempts = 5,
            LockoutDurationMinutes = 15,
            UpdatedUtc = DateTime.UtcNow,
            UpdatedBy = "System",
            EnablePasskey = true,
            EnableTotpMfa = true,
            EnableEmailMfa = true,
            MaxPasskeysPerUser = 5,
            EnforceMandatoryMfaEnrollment = false,
            MfaEnforcementGracePeriodDays = 3
        });
        await dbContext.SaveChangesAsync();
    }

    public async Task<RealHostSnapshot> SnapshotAsync(HostBootstrapRequest request)
    {
        await using var dbContext = CreateDbContext();
        var users = await dbContext.Users.AsNoTracking().ToListAsync();
        var persons = await dbContext.Persons.AsNoTracking().ToListAsync();
        var adminRoleIds = await dbContext.Roles
            .Where(role => role.Name == AuthConstants.Roles.Admin)
            .Select(role => role.Id)
            .ToListAsync();
        var audits = await dbContext.AuditEvents.AsNoTracking().ToListAsync();
        var sensitiveValues = new[]
        {
            request.Email,
            request.Name,
            request.Password,
            request.Token,
            Digest(request.Token)
        };
        var auditsAreSecretFree = audits.All(audit =>
            sensitiveValues.All(value =>
                string.IsNullOrEmpty(audit.Details) ||
                !audit.Details.Contains(value, StringComparison.OrdinalIgnoreCase)));

        return new RealHostSnapshot(
            users.Count,
            persons.Count,
            users.Count(user =>
                user.IsActive &&
                !user.IsDeleted &&
                user.PersonId.HasValue &&
                persons.Any(person =>
                    person.Id == user.PersonId &&
                    person.CanAuthenticate())),
            await dbContext.UserRoles.CountAsync(
                membership => adminRoleIds.Contains(membership.RoleId)),
            await dbContext.Settings.CountAsync(setting =>
                setting.Key == "system.operationalAdminBootstrap.completed"),
            audits.Count(audit =>
                audit.EventType == "OperationalAdminBootstrapCompleted"),
            auditsAreSecretFree);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private ApplicationDbContext CreateDbContext()
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        builder.UseNpgsql(
            ConnectionString,
            provider => provider.MigrationsAssembly(
                "Infrastructure.Migrations.Postgres"));
        builder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        builder.UseOpenIddict<Guid>();
        return new ApplicationDbContext(builder.Options);
    }

    private static string Digest(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed record RealHostSnapshot(
    int Users,
    int Persons,
    int LinkedActiveUsers,
    int AdminMemberships,
    int Markers,
    int SuccessAudits,
    bool AuditsAreSecretFree);

public sealed record HostBootstrapRequest(
    string Email,
    string Name,
    string Password,
    string Token)
{
    public static HostBootstrapRequest Create()
    {
        var id = Guid.NewGuid().ToString("N");
        Span<byte> tokenBytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(tokenBytes);
        var token = Convert.ToBase64String(tokenBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return new HostBootstrapRequest(
            $"bootstrap-{id}@example.invalid",
            $"Bootstrap Operator {id}",
            $"A!a1{id}",
            token);
    }

    public override string ToString() => "Operational bootstrap request (redacted)";
}

public sealed class OperationalBootstrapHostFactory
    : WebApplicationFactory<SecurityHeadersMiddleware>
{
    private const string Endpoint = "/api/operational-bootstrap/admin";
    private const string HeaderName = "X-HybridAuth-Bootstrap-Token";
    private static readonly SemaphoreSlim EnvironmentLock = new(1, 1);
    private readonly string _connectionString;
    private readonly HostBootstrapRequest _request;
    private readonly bool _proxyEnabled;
    private readonly string? _knownProxies;
    private readonly IPAddress _remoteAddress;

    private OperationalBootstrapHostFactory(
        string connectionString,
        HostBootstrapRequest request,
        bool proxyEnabled,
        string? knownProxies,
        IPAddress remoteAddress)
    {
        _connectionString = connectionString;
        _request = request;
        _proxyEnabled = proxyEnabled;
        _knownProxies = knownProxies;
        _remoteAddress = remoteAddress;
    }

    public HttpClient Client { get; private set; } = null!;

    public static async Task<OperationalBootstrapHostFactory> CreateAsync(
        string connectionString,
        HostBootstrapRequest request,
        bool proxyEnabled,
        string? knownProxies,
        IPAddress remoteAddress)
    {
        var factory = new OperationalBootstrapHostFactory(
            connectionString,
            request,
            proxyEnabled,
            knownProxies,
            remoteAddress);
        await EnvironmentLock.WaitAsync();
        const string providerVariable = "DATABASE_PROVIDER";
        const string connectionVariable =
            "ConnectionStrings__PostgreSqlConnection";
        var previousProvider = Environment.GetEnvironmentVariable(providerVariable);
        var previousConnection = Environment.GetEnvironmentVariable(connectionVariable);
        try
        {
            Environment.SetEnvironmentVariable(providerVariable, "PostgreSQL");
            Environment.SetEnvironmentVariable(connectionVariable, connectionString);
            factory.Client = factory.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false,
                    BaseAddress = new Uri("https://localhost"),
                    HandleCookies = true
                });
        }
        finally
        {
            Environment.SetEnvironmentVariable(providerVariable, previousProvider);
            Environment.SetEnvironmentVariable(connectionVariable, previousConnection);
            EnvironmentLock.Release();
        }

        return factory;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "PostgreSQL",
                ["ConnectionStrings:PostgreSqlConnection"] = _connectionString,
                ["Redis:Enabled"] = "false",
                ["RateLimiting:Enabled"] = "false",
                ["SeedData:PrivilegedTestAdminBootstrap:Enabled"] = "false",
                ["OperationalAdminBootstrap:Enabled"] = "true",
                ["OperationalAdminBootstrap:TokenSha256Digest"] =
                    Digest(_request.Token),
                ["OperationalAdminBootstrap:ExpiresAtUtc"] =
                    DateTimeOffset.UtcNow.AddMinutes(10).ToString("O"),
                ["Proxy:Enabled"] = _proxyEnabled.ToString(),
                ["Proxy:KnownProxies"] = _knownProxies,
                ["Turnstile:Enabled"] = "false"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<IStartupFilter>(
                new RemoteAddressStartupFilter(_remoteAddress));
        });
    }

    public async Task<HttpResponseMessage> PostBootstrapAsync(
        bool useHttps,
        string? forwardedProto)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"{(useHttps ? "https" : "http")}://localhost{Endpoint}")
        {
            Content = JsonContent.Create(new
            {
                _request.Email,
                _request.Name,
                _request.Password
            })
        };
        message.Headers.TryAddWithoutValidation(HeaderName, _request.Token);
        if (forwardedProto is not null)
        {
            message.Headers.TryAddWithoutValidation(
                "X-Forwarded-Proto",
                forwardedProto);
        }

        return await Client.SendAsync(message);
    }

    public override async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        await base.DisposeAsync();
    }

    private static string Digest(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed class RemoteAddressStartupFilter(IPAddress address)
        : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(
            Action<IApplicationBuilder> next) =>
            application =>
            {
                application.Use(async (context, continuation) =>
                {
                    context.Connection.RemoteIpAddress = address;
                    context.Connection.RemotePort = 44321;
                    await continuation();
                });
                next(application);
            };
    }
}
