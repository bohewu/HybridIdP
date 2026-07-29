using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Web.IdP.Middleware;

namespace Tests.Infrastructure.IntegrationTests;

public sealed class OperationalAdminBootstrapIntegrationTests
{
    private const string Endpoint = "/api/operational-bootstrap/admin";
    private const string HeaderName = "X-HybridAuth-Bootstrap-Token";
    private const string MarkerKey = "system.operationalAdminBootstrap.completed";

    public static TheoryData<string> IneligibleResidueCases() =>
        new()
        {
            "application-user",
            "soft-deleted-application-user",
            "person",
            "soft-deleted-person",
            "user-role",
            "user-login",
            "user-claim",
            "user-token",
            "user-session",
            "user-credential",
            "login-history",
            "user-app-role",
            "completion-marker"
        };

    [Fact]
    public async Task FreshBootstrap_ShouldPersistIdentityMarkerAndSecretFreeAudit_ThenCloseReplay()
    {
        await using var factory = await BootstrapFactory.CreateAsync(AdminRoleShape.Valid);
        var request = factory.Request;

        using var response = await factory.PostAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await AssertMachineResponseAsync(response, "operational_bootstrap_completed", request);

        var snapshot = await factory.SnapshotAsync();
        Assert.Equal(1, snapshot.Users);
        Assert.Equal(1, snapshot.Persons);
        Assert.Equal(1, snapshot.AdminMemberships);
        Assert.Equal(1, snapshot.Markers);
        Assert.Equal(1, snapshot.SuccessAudits);
        Assert.True(snapshot.UserIsActive);
        Assert.True(snapshot.PersonCanAuthenticate);
        Assert.True(snapshot.PasswordValid);
        Assert.True(snapshot.AuditIsSecretFree);

        using var replay = await factory.PostAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);
        await AssertMachineResponseAsync(replay, "operational_bootstrap_unavailable", request);
        Assert.Equal(snapshot, await factory.SnapshotAsync());
    }

    [Theory]
    [MemberData(nameof(IneligibleResidueCases))]
    public async Task Bootstrap_ShouldRejectConservativeIdentityResidueWithoutMutation(string residue)
    {
        await using var factory = await BootstrapFactory.CreateAsync(AdminRoleShape.Valid);
        await factory.AddResidueAsync(residue);
        var before = await factory.SnapshotAsync();
        var request = factory.Request;

        using var response = await factory.PostAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertMachineResponseAsync(response, "operational_bootstrap_unavailable", request);
        Assert.Equal(before, await factory.SnapshotAsync());
    }

    [Theory]
    [InlineData(AdminRoleShape.Missing)]
    [InlineData(AdminRoleShape.Duplicate)]
    [InlineData(AdminRoleShape.Invalid)]
    public async Task Bootstrap_ShouldRejectMissingDuplicateOrInvalidAdminRoleWithoutRepair(
        AdminRoleShape roleShape)
    {
        await using var factory = await BootstrapFactory.CreateAsync(roleShape);
        var before = await factory.SnapshotAsync();
        var request = factory.Request;

        using var response = await factory.PostAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertMachineResponseAsync(response, "operational_bootstrap_unavailable", request);
        Assert.Equal(before, await factory.SnapshotAsync());
    }

    private static async Task AssertMachineResponseAsync(
        HttpResponseMessage response,
        string expectedCode,
        BootstrapRequest request)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(body.Contains(request.Email, StringComparison.Ordinal));
        Assert.False(body.Contains(request.Name, StringComparison.Ordinal));
        Assert.False(body.Contains(request.Password, StringComparison.Ordinal));
        Assert.False(body.Contains(request.Token, StringComparison.Ordinal));
        Assert.False(body.Contains("injected-bootstrap-failure", StringComparison.Ordinal));

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

    public enum AdminRoleShape
    {
        Valid,
        Missing,
        Duplicate,
        Invalid
    }

    private sealed record BootstrapSnapshot(
        int Users,
        int Persons,
        int AdminMemberships,
        int Markers,
        int SuccessAudits,
        bool UserIsActive,
        bool PersonCanAuthenticate,
        bool PasswordValid,
        bool AuditIsSecretFree,
        string StateFingerprint);

    private sealed record BootstrapRequest(
        string Email,
        string Name,
        string Password,
        string Token)
    {
        public static BootstrapRequest Create()
        {
            var id = Guid.NewGuid().ToString("N");
            return new(
                $"bootstrap-{id}@example.invalid",
                $"Bootstrap Operator {id}",
                $"A!a1{id}",
                CreateToken());
        }

        private static string CreateToken()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public override string ToString() => "Operational bootstrap request (redacted)";
    }

    private sealed class BootstrapFactory : WebApplicationFactory<SecurityHeadersMiddleware>
    {
        private readonly string _databaseName =
            $"operational-admin-bootstrap-{Guid.NewGuid():N}";
        private readonly BootstrapRequest _authorization;
        private HttpClient? _client;

        private BootstrapFactory(BootstrapRequest authorization)
        {
            _authorization = authorization;
        }

        public BootstrapRequest Request => _authorization;

        public static async Task<BootstrapFactory> CreateAsync(AdminRoleShape roleShape)
        {
            var factory = new BootstrapFactory(BootstrapRequest.Create());
            factory._client = factory.CreateConfiguredClient();

            await factory.InitializeDatabaseAsync(roleShape);
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
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DatabaseProvider"] = "SqlServer",
                    ["ConnectionStrings:SqlServerConnection"] = "Server=(local);Database=unused",
                    ["Redis:Enabled"] = "false",
                    ["RateLimiting:Enabled"] = "false",
                    ["SeedData:PrivilegedTestAdminBootstrap:Enabled"] = "false",
                    ["OperationalAdminBootstrap:Enabled"] = "true",
                    ["OperationalAdminBootstrap:TokenSha256Digest"] = Digest(_authorization.Token),
                    ["OperationalAdminBootstrap:ExpiresAtUtc"] =
                        DateTimeOffset.UtcNow.AddMinutes(10).ToString("O")
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.RemoveAll<IHostedService>();
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName)
                        .ConfigureWarnings(warnings =>
                            warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }

        public async Task<HttpResponseMessage> PostAsync(BootstrapRequest request)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = JsonContent.Create(new
                {
                    request.Email,
                    request.Name,
                    request.Password
                })
            };
            message.Headers.Add(HeaderName, _authorization.Token);
            return await _client!.SendAsync(message);
        }

        public async Task AddResidueAsync(string residue)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userId = Guid.NewGuid();
            var roleId = await db.Roles
                .Where(role => role.Name == AuthConstants.Roles.Admin)
                .Select(role => role.Id)
                .SingleAsync();

            switch (residue)
            {
                case "application-user":
                case "soft-deleted-application-user":
                    db.Users.Add(new ApplicationUser
                    {
                        Id = userId,
                        UserName = $"existing-{userId:N}@example.invalid",
                        NormalizedUserName = $"EXISTING-{userId:N}@EXAMPLE.INVALID",
                        Email = $"existing-{userId:N}@example.invalid",
                        NormalizedEmail = $"EXISTING-{userId:N}@EXAMPLE.INVALID",
                        IsActive = residue == "application-user",
                        IsDeleted = residue != "application-user"
                    });
                    break;
                case "person":
                case "soft-deleted-person":
                    db.Persons.Add(new Person
                    {
                        Id = Guid.NewGuid(),
                        FirstName = "Existing",
                        IsDeleted = residue != "person",
                        CreatedAt = DateTime.UtcNow
                    });
                    break;
                case "user-role":
                    db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId });
                    break;
                case "user-login":
                    db.UserLogins.Add(new IdentityUserLogin<Guid>
                    {
                        UserId = userId,
                        LoginProvider = "existing-provider",
                        ProviderKey = Guid.NewGuid().ToString("N"),
                        ProviderDisplayName = "Existing"
                    });
                    break;
                case "user-claim":
                    db.UserClaims.Add(new IdentityUserClaim<Guid>
                    {
                        UserId = userId,
                        ClaimType = "existing",
                        ClaimValue = "present"
                    });
                    break;
                case "user-token":
                    db.UserTokens.Add(new IdentityUserToken<Guid>
                    {
                        UserId = userId,
                        LoginProvider = "existing-provider",
                        Name = "existing-token",
                        Value = "non-bootstrap-residue"
                    });
                    break;
                case "user-session":
                    db.UserSessions.Add(new UserSession
                    {
                        UserId = userId,
                        AuthorizationId = Guid.NewGuid().ToString("N"),
                        ActiveRoleId = roleId
                    });
                    break;
                case "user-credential":
                    db.UserCredentials.Add(new UserCredential
                    {
                        UserId = userId,
                        CredentialId = RandomNumberGenerator.GetBytes(32),
                        PublicKey = RandomNumberGenerator.GetBytes(32),
                        RegDate = DateTime.UtcNow
                    });
                    break;
                case "login-history":
                    db.LoginHistories.Add(new LoginHistory
                    {
                        UserId = userId,
                        LoginTime = DateTime.UtcNow
                    });
                    break;
                case "user-app-role":
                    db.UserAppRoles.Add(new UserAppRole
                    {
                        UserId = userId,
                        ClientId = Guid.NewGuid().ToString("N"),
                        RoleName = AuthConstants.Roles.Admin
                    });
                    break;
                case "completion-marker":
                    db.Settings.Add(new Setting
                    {
                        Id = Guid.NewGuid(),
                        Key = MarkerKey,
                        Value = "unexpected-value"
                    });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(residue));
            }

            await db.SaveChangesAsync();
        }

        public async Task<BootstrapSnapshot> SnapshotAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var adminRoleIds = await db.Roles
                .Where(role => role.Name == AuthConstants.Roles.Admin)
                .Select(role => role.Id)
                .ToListAsync();
            var users = await db.Users.AsNoTracking().OrderBy(user => user.Id).ToListAsync();
            var persons = await db.Persons.AsNoTracking().OrderBy(person => person.Id).ToListAsync();
            var auditEvents = await db.AuditEvents.AsNoTracking().OrderBy(audit => audit.Id).ToListAsync();
            var firstUser = users.SingleOrDefault();
            var firstPerson = persons.SingleOrDefault();
            var passwordValid = firstUser is not null &&
                await userManager.CheckPasswordAsync(firstUser, _authorization.Password);
            var secretFree = auditEvents.All(audit =>
                !ContainsSensitive(audit.Details, _authorization));
            var state = string.Join('|',
                users.Select(user =>
                    $"{user.Id}:{user.IsActive}:{user.IsDeleted}:{user.PersonId}:{user.SecurityStamp}:{user.ConcurrencyStamp}"))
                + string.Join('|', persons.Select(person =>
                    $"{person.Id}:{person.Status}:{person.IsDeleted}:{person.StartDate}:{person.EndDate}"));

            return new BootstrapSnapshot(
                users.Count,
                persons.Count,
                await db.UserRoles.CountAsync(role => adminRoleIds.Contains(role.RoleId)),
                await db.Settings.CountAsync(setting => setting.Key == MarkerKey),
                auditEvents.Count(audit =>
                    audit.EventType == "OperationalAdminBootstrapCompleted"),
                firstUser?.IsActive == true,
                firstPerson?.CanAuthenticate() == true,
                passwordValid,
                secretFree,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state))));
        }

        private async Task InitializeDatabaseAsync(AdminRoleShape roleShape)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();

            if (roleShape == AdminRoleShape.Missing)
            {
                return;
            }

            var roles = roleShape == AdminRoleShape.Duplicate ? 2 : 1;
            for (var index = 0; index < roles; index++)
            {
                db.Roles.Add(new ApplicationRole
                {
                    Id = Guid.NewGuid(),
                    Name = AuthConstants.Roles.Admin,
                    NormalizedName = index == 0 ? "ADMIN" : $"ADMIN-DUPLICATE-{index}",
                    IsSystem = roleShape != AdminRoleShape.Invalid,
                    Permissions = roleShape == AdminRoleShape.Invalid
                        ? string.Empty
                        : string.Join(',', Permissions.GetAll())
                });
            }

            await db.SaveChangesAsync();
        }

        public override async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            await base.DisposeAsync();
        }

        private static bool ContainsSensitive(string? value, BootstrapRequest request) =>
            value is not null &&
            (value.Contains(request.Email, StringComparison.Ordinal) ||
             value.Contains(request.Name, StringComparison.Ordinal) ||
             value.Contains(request.Password, StringComparison.Ordinal) ||
             value.Contains(request.Token, StringComparison.Ordinal));
    }

    private static string Digest(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
