using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Constants;
using Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Web.IdP.Middleware;

namespace Tests.SystemTests;

public sealed class OperationalAdminBootstrapEndpointTests
{
    private const string Endpoint = "/api/operational-bootstrap/admin";
    private const string HeaderName = "X-HybridAuth-Bootstrap-Token";

    [Fact]
    public async Task Post_ShouldBeDefaultClosedWithGenericMachineResponse()
    {
        await using var factory = await EndpointFactory.CreateAsync(enabled: null);
        var request = factory.Request;

        using var response = await factory.PostAsync(request, includeToken: true, useHttps: true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertResponseAsync(response, "operational_bootstrap_unavailable", request);
        Assert.Equal(0, await factory.UserCountAsync());
    }

    [Fact]
    public async Task Post_ShouldBindShippedDisabledEnvironmentExampleAndRemainUnavailable()
    {
        var exampleConfiguration = ReadOperationalBootstrapExampleConfiguration();
        Assert.Equal("false", exampleConfiguration["OperationalAdminBootstrap:Enabled"]);
        Assert.Equal(string.Empty, exampleConfiguration["OperationalAdminBootstrap:TokenSha256Digest"]);
        Assert.Equal(string.Empty, exampleConfiguration["OperationalAdminBootstrap:ExpiresAtUtc"]);

        await using var factory = await EndpointFactory.CreateAsync(
            enabled: null,
            operationalBootstrapConfiguration: exampleConfiguration);
        var request = factory.Request;
        var options = factory.GetOperationalBootstrapOptions();

        Assert.False(options.Enabled);
        Assert.True(string.IsNullOrEmpty(options.TokenSha256Digest));
        Assert.Null(options.ExpiresAtUtc);

        using var response = await factory.PostAsync(request, includeToken: true, useHttps: true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertResponseAsync(response, "operational_bootstrap_unavailable", request);
        Assert.Equal(0, await factory.UserCountAsync());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("malformed")]
    [InlineData("incorrect")]
    [InlineData("expired")]
    public async Task Post_ShouldRejectInvalidOrExpiredDedicatedHeaderToken(string scenario)
    {
        await using var factory = await EndpointFactory.CreateAsync(
            enabled: true,
            expired: scenario == "expired");
        var request = factory.Request;

        using var response = await factory.PostAsync(
            request,
            includeToken: scenario != "missing",
            useHttps: true,
            tokenOverride: scenario switch
            {
                "empty" => string.Empty,
                "malformed" => "not-a-token",
                "incorrect" => EndpointRequest.Create().Token,
                _ => null
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertResponseAsync(response, "operational_bootstrap_unavailable", request);
        Assert.Equal(0, await factory.UserCountAsync());
    }

    [Fact]
    public async Task Post_ShouldBeAnonymousAndHeaderAuthorized_NotCookieOrCsrfAuthorized()
    {
        await using var factory = await EndpointFactory.CreateAsync(enabled: true);
        var request = factory.Request;

        using var cookieOnly = await factory.PostAsync(
            request,
            includeToken: false,
            useHttps: true,
            cookie: ".AspNetCore.Identity.Application=non-authorizing-value");
        Assert.Equal(HttpStatusCode.NotFound, cookieOnly.StatusCode);
        await AssertResponseAsync(cookieOnly, "operational_bootstrap_unavailable", request);

        using var headerOnly = await factory.PostAsync(
            request,
            includeToken: true,
            useHttps: true);
        Assert.Equal(HttpStatusCode.Created, headerOnly.StatusCode);
        await AssertResponseAsync(headerOnly, "operational_bootstrap_completed", request);
        Assert.Equal(1, await factory.UserCountAsync());
    }

    [Fact]
    public async Task Post_ShouldAcceptDirectHttpsWithoutUsingSourceAddressAsAuthorization()
    {
        await using var factory = await EndpointFactory.CreateAsync(
            enabled: true,
            remoteAddress: IPAddress.Parse("203.0.113.77"));
        var request = factory.Request;

        using var response = await factory.PostAsync(request, includeToken: true, useHttps: true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await AssertResponseAsync(response, "operational_bootstrap_completed", request);
    }

    [Fact]
    public async Task Post_ShouldAcceptHttpsEstablishedByConfiguredTrustedForwarding()
    {
        await using var factory = await EndpointFactory.CreateAsync(
            enabled: true,
            proxyEnabled: true,
            knownProxies: "127.0.0.1",
            remoteAddress: IPAddress.Loopback);
        var request = factory.Request;

        using var response = await factory.PostAsync(
            request,
            includeToken: true,
            useHttps: false,
            forwardedProto: "https");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await AssertResponseAsync(response, "operational_bootstrap_completed", request);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_ShouldRejectPlainHttpAndUntrustedForwardedHttps(bool spoofForwarding)
    {
        await using var factory = await EndpointFactory.CreateAsync(
            enabled: true,
            proxyEnabled: spoofForwarding,
            knownProxies: spoofForwarding ? "127.0.0.1" : null,
            remoteAddress: IPAddress.Parse("203.0.113.88"));
        var request = factory.Request;

        using var response = await factory.PostAsync(
            request,
            includeToken: true,
            useHttps: false,
            forwardedProto: spoofForwarding ? "https" : null);

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(0, await factory.UserCountAsync());
    }

    private static async Task AssertResponseAsync(
        HttpResponseMessage response,
        string expectedCode,
        EndpointRequest request)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(body.Contains(request.Email, StringComparison.Ordinal));
        Assert.False(body.Contains(request.Name, StringComparison.Ordinal));
        Assert.False(body.Contains(request.Password, StringComparison.Ordinal));
        Assert.False(body.Contains(request.Token, StringComparison.Ordinal));
        Assert.False(body.Contains(Digest(request.Token), StringComparison.OrdinalIgnoreCase));

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            Assert.Fail("Operational bootstrap response was not valid JSON.");
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            Assert.Equal(expectedCode, root.GetProperty("code").GetString());
            Assert.True(root.TryGetProperty("correlationId", out var correlation));
            Assert.False(string.IsNullOrWhiteSpace(correlation.GetString()));
            Assert.All(root.EnumerateObject(), property =>
                Assert.Contains(property.Name, new[] { "code", "correlationId" }));
        }
    }

    private static IReadOnlyDictionary<string, string?>
        ReadOperationalBootstrapExampleConfiguration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? examplePath = null;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "deployment", ".env.example");
            if (File.Exists(candidate))
            {
                examplePath = candidate;
                break;
            }

            directory = directory.Parent;
        }

        if (examplePath is null)
        {
            throw new FileNotFoundException(
                "Could not locate the shipped deployment environment example.");
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var rawLine in File.ReadLines(examplePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            if (!key.StartsWith("OperationalAdminBootstrap__", StringComparison.Ordinal))
            {
                continue;
            }

            values.Add(
                key.Replace("__", ":", StringComparison.Ordinal),
                line[(separator + 1)..].Trim());
        }

        return values;
    }

    private sealed record EndpointRequest(
        string Email,
        string Name,
        string Password,
        string Token)
    {
        public static EndpointRequest Create()
        {
            var id = Guid.NewGuid().ToString("N");
            Span<byte> tokenBytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(tokenBytes);
            var token = Convert.ToBase64String(tokenBytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            return new(
                $"bootstrap-{id}@example.invalid",
                $"Bootstrap Operator {id}",
                $"A!a1{id}",
                token);
        }

        public override string ToString() => "Operational bootstrap request (redacted)";
    }

    private sealed class EndpointFactory : WebApplicationFactory<SecurityHeadersMiddleware>
    {
        private readonly string _databaseName =
            $"operational-admin-bootstrap-{Guid.NewGuid():N}";
        private readonly bool? _enabled;
        private readonly bool _expired;
        private readonly bool _proxyEnabled;
        private readonly string? _knownProxies;
        private readonly IPAddress _remoteAddress;
        private readonly IReadOnlyDictionary<string, string?>?
            _operationalBootstrapConfiguration;
        private HttpClient? _client;

        private EndpointFactory(
            bool? enabled,
            bool expired,
            bool proxyEnabled,
            string? knownProxies,
            IPAddress remoteAddress,
            IReadOnlyDictionary<string, string?>? operationalBootstrapConfiguration)
        {
            _enabled = enabled;
            _expired = expired;
            _proxyEnabled = proxyEnabled;
            _knownProxies = knownProxies;
            _remoteAddress = remoteAddress;
            _operationalBootstrapConfiguration = operationalBootstrapConfiguration;
            Request = EndpointRequest.Create();
        }

        public EndpointRequest Request { get; }

        public static async Task<EndpointFactory> CreateAsync(
            bool? enabled,
            bool expired = false,
            bool proxyEnabled = false,
            string? knownProxies = null,
            IPAddress? remoteAddress = null,
            IReadOnlyDictionary<string, string?>? operationalBootstrapConfiguration = null)
        {
            var factory = new EndpointFactory(
                enabled,
                expired,
                proxyEnabled,
                knownProxies,
                remoteAddress ?? IPAddress.Loopback,
                operationalBootstrapConfiguration);
            factory._client = factory.CreateConfiguredClient();
            await factory.InitializeDatabaseAsync();
            return factory;
        }

        private HttpClient CreateConfiguredClient()
        {
            const string providerVariable = "DATABASE_PROVIDER";
            const string connectionVariable = "ConnectionStrings__SqlServerConnection";
            var previousProvider = Environment.GetEnvironmentVariable(providerVariable);
            var previousConnection = Environment.GetEnvironmentVariable(connectionVariable);

            try
            {
                Environment.SetEnvironmentVariable(providerVariable, "SqlServer");
                Environment.SetEnvironmentVariable(
                    connectionVariable,
                    "Server=(local);Database=unused");
                return CreateClient(new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false,
                    BaseAddress = new Uri("https://localhost")
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable(providerVariable, previousProvider);
                Environment.SetEnvironmentVariable(connectionVariable, previousConnection);
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["DatabaseProvider"] = "SqlServer",
                    ["ConnectionStrings:SqlServerConnection"] = "Server=(local);Database=unused",
                    ["Redis:Enabled"] = "false",
                    ["RateLimiting:Enabled"] = "false",
                    ["SeedData:PrivilegedTestAdminBootstrap:Enabled"] = "false",
                    ["Proxy:Enabled"] = _proxyEnabled.ToString(),
                    ["Proxy:KnownProxies"] = _knownProxies
                };
                if (_operationalBootstrapConfiguration is not null)
                {
                    foreach (var entry in _operationalBootstrapConfiguration)
                    {
                        values[entry.Key] = entry.Value;
                    }
                }

                if (_enabled.HasValue)
                {
                    values["OperationalAdminBootstrap:Enabled"] = _enabled.Value.ToString();
                    values["OperationalAdminBootstrap:TokenSha256Digest"] = Digest(Request.Token);
                    values["OperationalAdminBootstrap:ExpiresAtUtc"] =
                        DateTimeOffset.UtcNow.AddMinutes(_expired ? -1 : 10).ToString("O");
                }

                configuration.AddInMemoryCollection(values);
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IStartupFilter>(new RemoteAddressStartupFilter(_remoteAddress));
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName)
                        .ConfigureWarnings(warnings =>
                            warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }

        public async Task<HttpResponseMessage> PostAsync(
            EndpointRequest request,
            bool includeToken,
            bool useHttps,
            string? tokenOverride = null,
            string? forwardedProto = null,
            string? cookie = null)
        {
            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                $"{(useHttps ? "https" : "http")}://localhost{Endpoint}")
            {
                Content = JsonContent.Create(new
                {
                    request.Email,
                    request.Name,
                    request.Password
                })
            };
            if (includeToken)
            {
                message.Headers.TryAddWithoutValidation(
                    HeaderName,
                    tokenOverride ?? request.Token);
            }

            if (forwardedProto is not null)
            {
                message.Headers.TryAddWithoutValidation("X-Forwarded-Proto", forwardedProto);
            }

            if (cookie is not null)
            {
                message.Headers.TryAddWithoutValidation("Cookie", cookie);
            }

            return await _client!.SendAsync(message);
        }

        public OperationalAdminBootstrapOptions GetOperationalBootstrapOptions() =>
            Services
                .GetRequiredService<IOptions<OperationalAdminBootstrapOptions>>()
                .Value;

        public async Task<int> UserCountAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.Users.CountAsync();
        }

        private async Task InitializeDatabaseAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Roles.Add(new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = AuthConstants.Roles.Admin,
                NormalizedName = "ADMIN",
                IsSystem = true,
                Permissions = string.Join(',', Permissions.GetAll())
            });
            await db.SaveChangesAsync();
        }

        public override async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            await base.DisposeAsync();
        }
    }

    private sealed class RemoteAddressStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
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

    private static string Digest(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
