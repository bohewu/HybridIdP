using Core.Application;
using Core.Application.DTOs;
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

public sealed class DirectoryRequiredCredentialChangeServiceTests
{
    [Fact]
    public async Task ChangeAsync_CompletedAuthority_ClearsFlagRotatesStampRevokesAndPreservesLocalHash()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddSessionAsync();
        var originalStamp = fixture.User.SecurityStamp;
        fixture.SyncOperation = async (invocation, cancellationToken) =>
        {
            Assert.Null(fixture.Context.Database.CurrentTransaction);
            var attempt = await fixture.Context.NativeDirectoryRecoveryAttempts
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            var user = await fixture.Context.Users.AsNoTracking().SingleAsync(cancellationToken);
            var session = await fixture.Context.UserSessions.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(NativeDirectoryRecoveryStatus.Succeeded, attempt.Status);
            Assert.Equal(attempt.Id, invocation.Source.AttemptId);
            Assert.Equal(attempt.Version, invocation.Source.Version);
            Assert.Equal(user.ConcurrencyStamp, invocation.ConcurrencyStamp);
            Assert.Equal(user.SecurityStamp, invocation.SecurityStamp);
            Assert.NotNull(session.RevokedUtc);
            return new LegacyPasswordSyncResult(LegacyPasswordSyncResultOutcome.Failed);
        };

        var result = await fixture.Service.ChangeAsync(fixture.Request());

        Assert.Equal(RecoveryProofOutcome.Success, result);
        fixture.Context.ChangeTracker.Clear();
        var user = await fixture.Context.Users.SingleAsync();
        Assert.False(user.RequiresPasswordChange);
        Assert.Equal(fixture.OriginalHash, user.PasswordHash);
        Assert.NotEqual(originalStamp, user.SecurityStamp);
        Assert.NotNull((await fixture.Context.UserSessions.SingleAsync()).RevokedUtc);
        var attempt = await fixture.Context.NativeDirectoryRecoveryAttempts.SingleAsync();
        Assert.Equal(NativeDirectoryCredentialOperationKind.RequiredChange, attempt.OperationKind);
        Assert.Equal(NativeDirectoryRecoveryStatus.Succeeded, attempt.Status);
        Assert.Equal(1, fixture.SyncCalls);
        Assert.Equal(LegacyPasswordSyncSourceKind.RequiredChange, fixture.LastSync!.Source.Kind);
        Assert.Equal(LegacyPasswordSyncCohort.CompletedDirectoryRequiredChange, fixture.LastSync.Cohort);
        Assert.Equal("replacement-value", fixture.LastSync.Password);
    }

    [Fact]
    public async Task ChangeAsync_UncertainWrite_KeepsBarrierAndPreventsSecondWrite()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Outcome = DirectoryCredentialOperationOutcome.Timeout;

        Assert.Equal(RecoveryProofOutcome.Unavailable, await fixture.Service.ChangeAsync(fixture.Request()));
        Assert.Equal(RecoveryProofOutcome.Unavailable, await fixture.Service.ChangeAsync(fixture.Request()));

        Assert.Equal(1, fixture.CapabilityCalls);
        Assert.Equal(NativeDirectoryRecoveryStatus.ReconciliationRequired,
            (await fixture.Context.NativeDirectoryRecoveryAttempts.SingleAsync()).Status);
    }

    [Fact]
    public async Task ChangeAsync_MismatchedImmutableBinding_ContactsNoCapability()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.ChangeAsync(fixture.Request(Guid.NewGuid()));

        Assert.Equal(RecoveryProofOutcome.Unavailable, result);
        Assert.Equal(0, fixture.CapabilityCalls);
        Assert.Empty(await fixture.Context.NativeDirectoryRecoveryAttempts.ToListAsync());
        Assert.Equal(0, fixture.SyncCalls);
    }

    [Theory]
    [InlineData(true, false, 1, 0)]
    [InlineData(false, true, 0, 1)]
    [InlineData(true, true, 1, 1)]
    [InlineData(false, false, 0, 0)]
    public async Task ChangeAsync_DestinationMatrix_PreservesDirectoryAuthorityAndExactDispatchCounts(
        bool directoryEnabled,
        bool legacyEnabled,
        int expectedDirectoryWrites,
        int expectedLegacyDispatches)
    {
        await using var fixture = await Fixture.CreateAsync(directoryEnabled, legacyEnabled);

        var result = await fixture.Service.ChangeAsync(fixture.Request());

        Assert.Equal(
            directoryEnabled || legacyEnabled ? RecoveryProofOutcome.Success : RecoveryProofOutcome.Invalid,
            result);
        Assert.Equal(expectedDirectoryWrites, fixture.CapabilityCalls);
        Assert.Equal(expectedLegacyDispatches, fixture.LegacyDispatchCalls);
        Assert.Equal(legacyEnabled ? 1 : 0, fixture.SyncCalls);
        Assert.Equal(
            (directoryEnabled, legacyEnabled) switch
            {
                (true, true) => ["directory", "legacy"],
                (true, false) => ["directory"],
                (false, true) => ["legacy"],
                _ => []
            },
            fixture.DestinationEvents);
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
        public DirectoryRequiredCredentialChangeService Service { get; private set; } = null!;
        public ApplicationUser User { get; private set; } = null!;
        public Guid DirectoryObjectId { get; private set; }
        public string OriginalHash { get; private set; } = null!;
        public int CapabilityCalls { get; private set; }
        public int SyncCalls { get; private set; }
        public int LegacyDispatchCalls { get; private set; }
        public List<string> DestinationEvents { get; } = [];
        public SyncInvocation? LastSync { get; private set; }
        public Func<SyncInvocation, CancellationToken, Task<LegacyPasswordSyncResult>>? SyncOperation { get; set; }
        public DirectoryCredentialOperationOutcome Outcome { get; set; } = DirectoryCredentialOperationOutcome.Succeeded;

        public static async Task<Fixture> CreateAsync(
            bool directoryDestinationEnabled = true,
            bool legacyDestinationEnabled = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var fixture = new Fixture(connection, context);
            fixture.User = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = "directory-user", NormalizedUserName = "DIRECTORY-USER",
                IsActive = true, RequiresPasswordChange = true, SecurityStamp = "original-stamp",
                PasswordHash = "retained-local-hash"
            };
            fixture.OriginalHash = fixture.User.PasswordHash;
            fixture.DirectoryObjectId = Guid.NewGuid();
            var binding = new ProviderSubjectDirectoryBinding(
                fixture.User.Id, "provider", "subject", directoryObjectId: fixture.DirectoryObjectId,
                createdAtUtc: DateTime.UtcNow, canonicalAccountAlias: fixture.User.UserName);
            var state = new CredentialMigrationStateRecord(fixture.User.Id, binding.Id, DateTimeOffset.UtcNow);
            state.Advance(CredentialMigrationState.ProofValidated,
                EffectiveEmailOtpRequirement.NotRequired, DateTimeOffset.UtcNow);
            state.Advance(CredentialMigrationState.DirectoryCredentialCommitted, DateTimeOffset.UtcNow);
            state.Advance(CredentialMigrationState.LocalFinalized, DateTimeOffset.UtcNow);
            context.Users.Add(fixture.User);
            context.ProviderSubjectDirectoryBindings.Add(binding);
            context.CredentialMigrationStateRecords.Add(state);
            await context.SaveChangesAsync();

            var userManager = CreateUserManager();
            userManager.Setup(manager => manager.UpdateSecurityStampAsync(It.IsAny<ApplicationUser>()))
                .ReturnsAsync((ApplicationUser user) =>
                {
                    user.SecurityStamp = Guid.NewGuid().ToString();
                    return IdentityResult.Success;
                });
            userManager.Setup(manager => manager.ResetAccessFailedCountAsync(It.IsAny<ApplicationUser>()))
                .ReturnsAsync(IdentityResult.Success);
            var directoryOptions = new DirectoryIntegrationOptions
            {
                Enabled = true,
                AuthenticationEnabled = true,
                TemporaryCredentialCapabilityEnabled = directoryDestinationEnabled
            };
            var legacyOptions = new LegacyPasswordSyncOptions
            {
                Enabled = legacyDestinationEnabled,
                CompletedDirectoryRequiredChangeEnabled = legacyDestinationEnabled
            };
            var capability = new Mock<IDirectoryTemporaryCredentialCapability>();
            capability.Setup(service => service.ChangeRequiredCredentialAsync(
                    fixture.DirectoryObjectId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    if (!directoryOptions.Enabled || !directoryOptions.AuthenticationEnabled ||
                        !directoryOptions.TemporaryCredentialCapabilityEnabled)
                    {
                        return new DirectoryCredentialOperationResult(
                            DirectoryCredentialOperationOutcome.Unsupported);
                    }

                    fixture.CapabilityCalls++;
                    fixture.DestinationEvents.Add("directory");
                    return new DirectoryCredentialOperationResult(fixture.Outcome);
                });
            var policy = new Mock<ISecurityPolicyService>();
            policy.Setup(service => service.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());
            var authorizations = new Mock<IOpenIddictAuthorizationManager>();
            authorizations.Setup(manager => manager.FindBySubjectAsync(
                    fixture.User.Id.ToString(), It.IsAny<CancellationToken>()))
                .Returns(EmptyAsync());
            var tokens = new Mock<IOpenIddictTokenManager>();
            tokens.Setup(manager => manager.FindBySubjectAsync(
                    fixture.User.Id.ToString(), It.IsAny<CancellationToken>()))
                .Returns(EmptyAsync());
            var passwordSyncCoordinator = new Mock<ILegacyPasswordSyncCoordinator>();
            passwordSyncCoordinator.Setup(service => service.SynchronizeAsync(
                    It.IsAny<LegacyPasswordSyncSourceReference>(),
                    It.IsAny<LegacyPasswordSyncCohort>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((LegacyPasswordSyncSourceReference source, LegacyPasswordSyncCohort cohort,
                    Guid accountId, Guid bindingId, string concurrencyStamp, string securityStamp,
                    string password, CancellationToken cancellationToken) =>
                {
                    fixture.SyncCalls++;
                    fixture.LastSync = new SyncInvocation(
                        source, cohort, accountId, bindingId, concurrencyStamp, securityStamp, password);
                    if (!legacyOptions.Enabled || !legacyOptions.IsCohortEnabled(cohort))
                    {
                        return Task.FromResult(
                            new LegacyPasswordSyncResult(LegacyPasswordSyncResultOutcome.NotEligible));
                    }

                    fixture.LegacyDispatchCalls++;
                    fixture.DestinationEvents.Add("legacy");
                    return fixture.SyncOperation?.Invoke(fixture.LastSync, cancellationToken) ??
                        Task.FromResult(new LegacyPasswordSyncResult(LegacyPasswordSyncResultOutcome.Succeeded));
                });

            fixture.Service = new DirectoryRequiredCredentialChangeService(
                context, userManager.Object, capability.Object, policy.Object,
                authorizations.Object, tokens.Object, passwordSyncCoordinator.Object,
                Options.Create(directoryOptions), Options.Create(legacyOptions), TimeProvider.System);
            return fixture;
        }

        public DirectoryRequiredCredentialChangeRequest Request(Guid? directoryObjectId = null) =>
            new(User.Id, directoryObjectId ?? DirectoryObjectId, "temporary-value", "replacement-value");

        public async Task AddSessionAsync()
        {
            Context.UserSessions.Add(new UserSession
            {
                UserId = User.Id,
                AuthorizationId = Guid.NewGuid().ToString()
            });
            await Context.SaveChangesAsync();
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

        private static async IAsyncEnumerable<object> EmptyAsync()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed record SyncInvocation(
        LegacyPasswordSyncSourceReference Source,
        LegacyPasswordSyncCohort Cohort,
        Guid AccountId,
        Guid BindingId,
        string ConcurrencyStamp,
        string SecurityStamp,
        string Password);
}
