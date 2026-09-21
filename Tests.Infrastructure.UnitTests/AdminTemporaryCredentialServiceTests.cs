using System.Text.Json;
using Core.Application;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class AdminTemporaryCredentialServiceTests
{
    [Fact]
    public async Task IssueAsync_LocalAccount_SetsRequiredChangeRetainsHistoryAndRevokesAccess()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Authorizations.Add(new object());
        fixture.Tokens.Add(new object());
        await fixture.AddSessionAsync();

        var result = await fixture.Service.IssueAsync(fixture.Request());

        Assert.Equal(RecoveryProofOutcome.Success, result.Outcome);
        Assert.NotNull(result.TemporaryPassword);
        fixture.Context.ChangeTracker.Clear();
        var user = await fixture.Context.Users.SingleAsync();
        Assert.True(user.RequiresPasswordChange);
        Assert.Equal(PasswordVerificationResult.Success,
            fixture.Hasher.VerifyHashedPassword(user, user.PasswordHash!, result.TemporaryPassword!));
        Assert.Contains(fixture.OriginalHash, JsonSerializer.Deserialize<List<string>>(user.PasswordHistory)!);
        Assert.NotNull((await fixture.Context.UserSessions.SingleAsync()).RevokedUtc);
        Assert.Equal(1, fixture.AuthorizationRevocations);
        Assert.Equal(1, fixture.TokenRevocations);
        Assert.Equal(RecoveryProofAuditCategory.AdminTemporaryCredentialIssued, fixture.AuditCategory);
    }

    [Fact]
    public async Task IssueAsync_ReissueInvalidatesPriorTemporaryPassword()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Service.IssueAsync(fixture.Request());
        var second = await fixture.Service.IssueAsync(fixture.Request());

        fixture.Context.ChangeTracker.Clear();
        var user = await fixture.Context.Users.SingleAsync();
        Assert.Equal(PasswordVerificationResult.Failed,
            fixture.Hasher.VerifyHashedPassword(user, user.PasswordHash!, first.TemporaryPassword!));
        Assert.Equal(PasswordVerificationResult.Success,
            fixture.Hasher.VerifyHashedPassword(user, user.PasswordHash!, second.TemporaryPassword!));
    }

    [Fact]
    public async Task IssueAsync_UnauthorizedActor_DoesNotChangeCredential()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Authorized = false;

        var result = await fixture.Service.IssueAsync(fixture.Request());

        Assert.Equal(RecoveryProofOutcome.Unauthorized, result.Outcome);
        Assert.Equal(fixture.OriginalHash, (await fixture.Context.Users.SingleAsync()).PasswordHash);
        Assert.Equal(0, fixture.ResetCalls);
    }

    [Fact]
    public async Task IssueAsync_TokenRevocationFailure_RollsBackCredentialAndRequiredChange()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Tokens.Add(new object());
        fixture.FailTokenRevocation = true;

        var result = await fixture.Service.IssueAsync(fixture.Request());

        Assert.Equal(RecoveryProofOutcome.Unavailable, result.Outcome);
        fixture.Context.ChangeTracker.Clear();
        var user = await fixture.Context.Users.SingleAsync();
        Assert.Equal(fixture.OriginalHash, user.PasswordHash);
        Assert.False(user.RequiresPasswordChange);
        Assert.Null(fixture.AuditCategory);
    }

    [Fact]
    public async Task IssueAsync_CompletedDirectoryAccount_PreservesLocalHashAndCompletesReservation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.MakeCompletedDirectoryAsync(active: true);

        var result = await fixture.Service.IssueAsync(fixture.Request());

        Assert.Equal(RecoveryProofOutcome.Success, result.Outcome);
        Assert.NotNull(result.TemporaryPassword);
        Assert.Equal(1, fixture.DirectoryIssueCalls);
        fixture.Context.ChangeTracker.Clear();
        var user = await fixture.Context.Users.SingleAsync();
        Assert.Equal(fixture.OriginalHash, user.PasswordHash);
        Assert.True(user.RequiresPasswordChange);
        var attempt = await fixture.Context.NativeDirectoryRecoveryAttempts.SingleAsync();
        Assert.Equal(NativeDirectoryCredentialOperationKind.AdminTemporaryIssue, attempt.OperationKind);
        Assert.Equal(NativeDirectoryRecoveryStatus.Succeeded, attempt.Status);
        Assert.Null(attempt.RecoveryProofChallengeId);
    }

    [Fact]
    public async Task IssueAsync_InactiveCompletedDirectoryAccount_DoesNotCallCapability()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.MakeCompletedDirectoryAsync(active: false);

        var result = await fixture.Service.IssueAsync(fixture.Request());

        Assert.Equal(RecoveryProofOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, fixture.DirectoryIssueCalls);
        Assert.Empty(await fixture.Context.NativeDirectoryRecoveryAttempts.ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, ApplicationDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public ApplicationDbContext Context { get; }
        public PasswordHasher<ApplicationUser> Hasher { get; } = new();
        public AdminTemporaryCredentialService Service { get; private set; } = null!;
        public ApplicationUser User { get; private set; } = null!;
        public Guid ActorId { get; } = Guid.NewGuid();
        public string OriginalHash { get; private set; } = string.Empty;
        public bool Authorized { get; set; } = true;
        public bool FailTokenRevocation { get; set; }
        public int ResetCalls { get; private set; }
        public int AuthorizationRevocations { get; private set; }
        public int TokenRevocations { get; private set; }
        public int DirectoryIssueCalls { get; private set; }
        public RecoveryProofAuditCategory? AuditCategory { get; private set; }
        public List<object> Authorizations { get; } = [];
        public List<object> Tokens { get; } = [];

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var fixture = new Fixture(connection, context);
            fixture.User = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "temporary-user",
                NormalizedUserName = "TEMPORARY-USER",
                IsActive = true,
                SecurityStamp = "original-stamp",
                LastPasswordChangeDate = DateTime.UtcNow.AddDays(-1)
            };
            fixture.User.PasswordHash = fixture.Hasher.HashPassword(fixture.User, "Original!Password1");
            fixture.OriginalHash = fixture.User.PasswordHash;
            context.Users.Add(fixture.User);
            await context.SaveChangesAsync();

            var userManager = CreateUserManager();
            userManager.Setup(manager => manager.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>()))
                .ReturnsAsync("reset-token");
            userManager.Setup(manager => manager.ResetPasswordAsync(
                    It.IsAny<ApplicationUser>(), "reset-token", It.IsAny<string>()))
                .Returns(async (ApplicationUser user, string _, string password) =>
                {
                    fixture.ResetCalls++;
                    user.PasswordHash = fixture.Hasher.HashPassword(user, password);
                    user.SecurityStamp = Guid.NewGuid().ToString();
                    await context.SaveChangesAsync();
                    return IdentityResult.Success;
                });
            userManager.Setup(manager => manager.UpdateSecurityStampAsync(It.IsAny<ApplicationUser>()))
                .ReturnsAsync((ApplicationUser user) =>
                {
                    user.SecurityStamp = Guid.NewGuid().ToString();
                    return IdentityResult.Success;
                });

            var authorizer = new Mock<IRecoveryProofAuthorizer>();
            authorizer.Setup(service => service.IsAdministratorAuthorizedAsync(
                    fixture.ActorId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => fixture.Authorized);
            var audit = new Mock<IRecoveryProofAudit>();
            audit.Setup(service => service.RecordAsync(It.IsAny<RecoveryProofAuditEvent>(), It.IsAny<CancellationToken>()))
                .Callback((RecoveryProofAuditEvent item, CancellationToken _) => fixture.AuditCategory = item.Category)
                .Returns(Task.CompletedTask);
            var policy = new Mock<ISecurityPolicyService>();
            policy.Setup(service => service.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy
            {
                MinPasswordLength = 16,
                PasswordHistoryCount = 5,
                MinPasswordAgeDays = 30
            });
            var authorizations = new Mock<IOpenIddictAuthorizationManager>();
            authorizations.Setup(manager => manager.FindBySubjectAsync(
                    fixture.User.Id.ToString(), It.IsAny<CancellationToken>()))
                .Returns(() => AsAsyncEnumerable(fixture.Authorizations));
            authorizations.Setup(manager => manager.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("authorization-id");
            authorizations.Setup(manager => manager.TryRevokeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { fixture.AuthorizationRevocations++; return true; });
            var tokens = new Mock<IOpenIddictTokenManager>();
            tokens.Setup(manager => manager.FindBySubjectAsync(
                    fixture.User.Id.ToString(), It.IsAny<CancellationToken>()))
                .Returns(() => AsAsyncEnumerable(fixture.Tokens));
            tokens.Setup(manager => manager.FindByAuthorizationIdAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => AsAsyncEnumerable([]));
            tokens.Setup(manager => manager.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((object token, CancellationToken _) => fixture.Tokens.IndexOf(token).ToString());
            tokens.Setup(manager => manager.TryRevokeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    fixture.TokenRevocations++;
                    if (fixture.FailTokenRevocation) throw new InvalidOperationException();
                    return true;
                });
            var directoryCapability = new Mock<IDirectoryTemporaryCredentialCapability>();
            directoryCapability.Setup(capability => capability.IssueTemporaryCredentialAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    fixture.DirectoryIssueCalls++;
                    return new DirectoryCredentialOperationResult(DirectoryCredentialOperationOutcome.Succeeded);
                });
            fixture.Service = new AdminTemporaryCredentialService(
                context,
                userManager.Object,
                authorizations.Object,
                tokens.Object,
                authorizer.Object,
                audit.Object,
                policy.Object,
                Options.Create(new ForgotPasswordRecoveryOptions { AdminTemporaryCredentialsEnabled = true }),
                directoryCapability: directoryCapability.Object);
            return fixture;
        }

        public AdminTemporaryCredentialRequest Request() =>
            new(ActorId, User.Id, "identity checked", "user requested assistance");

        public async Task AddSessionAsync()
        {
            Context.UserSessions.Add(new UserSession { UserId = User.Id, AuthorizationId = Guid.NewGuid().ToString() });
            await Context.SaveChangesAsync();
        }

        public async Task MakeCompletedDirectoryAsync(bool active)
        {
            User.IsActive = active;
            var binding = new ProviderSubjectDirectoryBinding(
                User.Id, "provider", "subject", Guid.NewGuid(), DateTime.UtcNow, User.UserName);
            var state = new CredentialMigrationStateRecord(User.Id, binding.Id, DateTimeOffset.UtcNow);
            state.Advance(CredentialMigrationState.ProofValidated,
                EffectiveEmailOtpRequirement.NotRequired, DateTimeOffset.UtcNow);
            state.Advance(CredentialMigrationState.DirectoryCredentialCommitted, DateTimeOffset.UtcNow);
            state.Advance(CredentialMigrationState.LocalFinalized, DateTimeOffset.UtcNow);
            Context.ProviderSubjectDirectoryBindings.Add(binding);
            Context.CredentialMigrationStateRecords.Add(state);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static Mock<UserManager<ApplicationUser>> CreateUserManager()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(store.Object, null, null, null, null, null, null, null, null);
        }

        private static async IAsyncEnumerable<object> AsAsyncEnumerable(IEnumerable<object> items)
        {
            foreach (var item in items) yield return item;
            await Task.CompletedTask;
        }
    }
}
