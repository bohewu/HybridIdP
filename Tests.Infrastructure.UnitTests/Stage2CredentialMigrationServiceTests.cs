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
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class Stage2CredentialMigrationServiceTests
{
    private static readonly MigrationContinuationContext BrowserContext = new("browser-hash", "csrf-hash");

    [Fact]
    public async Task CommitAsync_LocalFinalizedAndAuditedBeforeCancelledSync_PreservesCompletedResult()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.SyncOperation = async (invocation, cancellationToken) =>
        {
            var state = await fixture.Context.CredentialMigrationStateRecords
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            Assert.Equal(CredentialMigrationState.LocalFinalized, state.State);
            Assert.Contains(fixture.AuditEvents, audit =>
                audit.Category == SanitizedMigrationAuditCategory.Completed &&
                audit.State == CredentialMigrationState.LocalFinalized);
            Assert.Equal(state.Id, invocation.Source.AttemptId);
            Assert.Equal(state.Version, invocation.Source.Version);
            Assert.Equal(fixture.Binding.Id, invocation.BindingId);
            Assert.Equal(fixture.User.ConcurrencyStamp, invocation.ConcurrencyStamp);
            Assert.Equal(fixture.User.SecurityStamp, invocation.SecurityStamp);
            throw new OperationCanceledException("secondary cancellation");
        };

        var result = await fixture.Service.CommitAsync(new MigrationCommitCeremonyRequest(
            "continuation",
            "final-user-password",
            BrowserContext));

        Assert.Equal(MigrationCeremonyOutcome.Completed, result.Outcome);
        Assert.Equal(1, fixture.SyncCalls);
        Assert.Equal(LegacyPasswordSyncSourceKind.Stage2Migration, fixture.LastSync!.Source.Kind);
        Assert.Equal(LegacyPasswordSyncCohort.Stage2Migration, fixture.LastSync.Cohort);
        Assert.Equal("final-user-password", fixture.LastSync.Password);
    }

    [Fact]
    public async Task CommitAsync_DirectoryRejected_DoesNotSynchronizeOrFinalize()
    {
        await using var fixture = await Fixture.CreateAsync(DirectoryCredentialOperationOutcome.Rejected);

        var result = await fixture.Service.CommitAsync(new MigrationCommitCeremonyRequest(
            "continuation",
            "final-user-password",
            BrowserContext));

        Assert.Equal(MigrationCeremonyOutcome.Denied, result.Outcome);
        Assert.Equal(0, fixture.SyncCalls);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(
            CredentialMigrationState.ProofValidated,
            (await fixture.Context.CredentialMigrationStateRecords.SingleAsync()).State);
    }

    [Theory]
    [InlineData(true, false, 1, 0)]
    [InlineData(false, true, 0, 1)]
    [InlineData(true, true, 1, 1)]
    [InlineData(false, false, 0, 0)]
    public async Task CommitAsync_DestinationMatrix_PreservesDirectoryAuthorityAndExactDispatchCounts(
        bool directoryEnabled,
        bool legacyEnabled,
        int expectedDirectoryWrites,
        int expectedLegacyDispatches)
    {
        await using var fixture = await Fixture.CreateAsync(
            directoryDestinationEnabled: directoryEnabled,
            legacyDestinationEnabled: legacyEnabled);

        var result = await fixture.Service.CommitAsync(new MigrationCommitCeremonyRequest(
            "continuation",
            "final-user-password",
            BrowserContext));

        Assert.Equal(
            directoryEnabled || legacyEnabled ? MigrationCeremonyOutcome.Completed : MigrationCeremonyOutcome.Denied,
            result.Outcome);
        Assert.Equal(expectedDirectoryWrites, fixture.DirectoryResetCalls);
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
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(
            directoryEnabled ? CredentialMigrationState.LocalFinalized : CredentialMigrationState.ProofValidated,
            (await fixture.Context.CredentialMigrationStateRecords.SingleAsync()).State);
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
        public Stage2CredentialMigrationService Service { get; private set; } = null!;
        public ApplicationUser User { get; private set; } = null!;
        public ProviderSubjectDirectoryBinding Binding { get; private set; } = null!;
        public List<SanitizedMigrationAuditEvent> AuditEvents { get; } = [];
        public List<string> DestinationEvents { get; } = [];
        public int DirectoryResetCalls { get; private set; }
        public int SyncCalls { get; private set; }
        public int LegacyDispatchCalls { get; private set; }
        public SyncInvocation? LastSync { get; private set; }
        public Func<SyncInvocation, CancellationToken, Task<LegacyPasswordSyncResult>>? SyncOperation { get; set; }

        public static async Task<Fixture> CreateAsync(
            DirectoryCredentialOperationOutcome resetOutcome = DirectoryCredentialOperationOutcome.Succeeded,
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
                Id = Guid.NewGuid(),
                UserName = "stage2-user",
                NormalizedUserName = "STAGE2-USER",
                IsActive = true,
                ConcurrencyStamp = "stage2-account-version",
                SecurityStamp = "stage2-security-stamp",
                PasswordHash = "retained-local-hash"
            };
            fixture.Binding = new ProviderSubjectDirectoryBinding(
                fixture.User.Id,
                "provider",
                "subject",
                Guid.NewGuid(),
                DateTime.UtcNow,
                fixture.User.UserName);
            var state = new CredentialMigrationStateRecord(
                fixture.User.Id,
                fixture.Binding.Id,
                DateTimeOffset.UtcNow);
            state.Advance(
                CredentialMigrationState.ProofValidated,
                EffectiveEmailOtpRequirement.NotRequired,
                DateTimeOffset.UtcNow);
            context.Users.Add(fixture.User);
            context.ProviderSubjectDirectoryBindings.Add(fixture.Binding);
            context.CredentialMigrationStateRecords.Add(state);
            await context.SaveChangesAsync();

            var stateStore = new CredentialMigrationStateStore(context, TimeProvider.System);
            var migrationRecord = await stateStore.FindAsync(fixture.User.Id) ??
                throw new InvalidOperationException("Missing migration fixture state.");
            var userManager = CreateUserManager();
            userManager.Setup(manager => manager.FindByIdAsync(fixture.User.Id.ToString()))
                .ReturnsAsync(fixture.User);
            userManager.Setup(manager => manager.IsLockedOutAsync(fixture.User)).ReturnsAsync(false);

            var continuations = new Mock<IMigrationContinuationStore>();
            var continuationSourceId = Guid.NewGuid();
            continuations.Setup(store => store.InspectAsync(
                    "continuation", BrowserContext, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MigrationContinuationConsumption(
                    MigrationContinuationConsumptionOutcome.Consumed,
                    migrationRecord));
            continuations.Setup(store => store.ConsumeAsync(
                    "continuation", BrowserContext, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MigrationContinuationConsumption(
                    MigrationContinuationConsumptionOutcome.Consumed,
                    migrationRecord,
                    continuationSourceId,
                    2));

            var directoryOptions = new DirectoryIntegrationOptions
            {
                Enabled = true,
                AuthenticationEnabled = directoryDestinationEnabled
            };
            var legacyOptions = new LegacyPasswordSyncOptions
            {
                Enabled = legacyDestinationEnabled,
                Stage2MigrationEnabled = legacyDestinationEnabled
            };
            var policy = new CredentialMigrationPolicy(Options.Create(new CredentialMigrationOptions
            {
                Enabled = directoryDestinationEnabled || legacyDestinationEnabled
            }));
            var resetter = new Mock<IDirectoryCredentialResetter>();
            resetter.Setup(service => service.ResetCredentialAsync(
                    fixture.Binding.DirectoryObjectId,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    fixture.DirectoryResetCalls++;
                    fixture.DestinationEvents.Add("directory");
                    return new DirectoryCredentialOperationResult(resetOutcome);
                });
            var verifier = new Mock<IDirectoryCredentialVerifier>();
            verifier.Setup(service => service.VerifyCredentialAsync(
                    fixture.Binding.DirectoryObjectId,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DirectoryCredentialVerificationResult(
                    DirectoryCredentialOutcome.Authenticated,
                    new ManagedDirectoryIdentity(
                        fixture.Binding.DirectoryObjectId,
                        "stage2-user",
                        true,
                        true,
                        false)));
            var bindingRefresh = new Mock<IStage1BindingRefreshService>();
            bindingRefresh.Setup(service => service.BindAndRefreshAsync(
                    It.IsAny<Stage1BindingRefreshRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Stage1BindingRefreshOutcome.ExistingBindingRefreshed);
            var audit = new Mock<ISanitizedMigrationAudit>();
            audit.Setup(service => service.RecordAsync(
                    It.IsAny<SanitizedMigrationAuditEvent>(),
                    It.IsAny<CancellationToken>()))
                .Callback((SanitizedMigrationAuditEvent auditEvent, CancellationToken _) =>
                    fixture.AuditEvents.Add(auditEvent))
                .Returns(Task.CompletedTask);
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

            fixture.Service = new Stage2CredentialMigrationService(
                userManager.Object,
                Mock.Of<IProofProvider>(),
                Mock.Of<IDirectoryIdentityLookup>(),
                Mock.Of<IDirectoryCredentialAuthenticator>(),
                resetter.Object,
                verifier.Object,
                policy,
                stateStore,
                continuations.Object,
                Mock.Of<IMigrationOtpProofService>(),
                Mock.Of<IRecoveryAssistanceService>(),
                bindingRefresh.Object,
                context,
                audit.Object,
                passwordSyncCoordinator.Object,
                Options.Create(directoryOptions),
                Options.Create(legacyOptions),
                TimeProvider.System);
            return fixture;
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
