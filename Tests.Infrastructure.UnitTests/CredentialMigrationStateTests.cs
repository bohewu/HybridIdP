using System.Security.Cryptography;
using System.Text;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class CredentialMigrationStateTests
{
    [Fact]
    public async Task EnsureRequiredAsync_ExactBinding_CreatesRequiredStateBeforeAnyTransition()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var accountId = await SeedAccountAsync(context);
        var binding = NewBinding();
        var store = new CredentialMigrationStateStore(context, new FixedTimeProvider());

        var record = await store.EnsureRequiredAsync(accountId, binding);
        var repeated = await store.EnsureRequiredAsync(accountId, binding);

        Assert.Equal(CredentialMigrationState.Required, record.State);
        Assert.Equal(EffectiveEmailOtpRequirement.Unspecified, record.EffectiveEmailOtpRequirement);
        Assert.Equal(record, repeated);
        Assert.Equal(1, await context.ProviderSubjectDirectoryBindings.CountAsync());
        Assert.Equal(1, await context.CredentialMigrationStateRecords.CountAsync());
        var persisted = await context.CredentialMigrationStateRecords.SingleAsync();
        Assert.Equal(1, persisted.Version);
        Assert.Equal(binding.DirectoryObjectId, (await context.ProviderSubjectDirectoryBindings.SingleAsync()).DirectoryObjectId);
    }

    [Fact]
    public async Task AdvanceAsync_OnlyPermitsMonotonicAdjacentTransitions()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var accountId = await SeedAccountAsync(context);
        var store = new CredentialMigrationStateStore(context, new FixedTimeProvider());
        await store.EnsureRequiredAsync(accountId, NewBinding());

        await Assert.ThrowsAsync<ArgumentException>(() => store.AdvanceAsync(
            accountId,
            CredentialMigrationState.Required,
            CredentialMigrationState.DirectoryCredentialCommitted));

        await Assert.ThrowsAsync<ArgumentException>(() => store.AdvanceAsync(
            accountId,
            CredentialMigrationState.Required,
            CredentialMigrationState.ProofValidated));

        var proofValidated = await store.AdvanceToProofValidatedAsync(
            accountId,
            EffectiveEmailOtpRequirement.Required);
        Assert.Equal(CredentialMigrationState.ProofValidated, proofValidated.State);
        Assert.Equal(EffectiveEmailOtpRequirement.Required, proofValidated.EffectiveEmailOtpRequirement);
        Assert.Equal(CredentialMigrationState.DirectoryCredentialCommitted, (await store.AdvanceAsync(
            accountId,
            CredentialMigrationState.ProofValidated,
            CredentialMigrationState.DirectoryCredentialCommitted)).State);
        Assert.Equal(CredentialMigrationState.LocalFinalized, (await store.AdvanceAsync(
            accountId,
            CredentialMigrationState.DirectoryCredentialCommitted,
            CredentialMigrationState.LocalFinalized)).State);
        Assert.Equal(4, (await context.CredentialMigrationStateRecords.AsNoTracking().SingleAsync()).Version);
    }

    [Fact]
    public async Task AdvanceAsync_CompetingExpectedStateTransitions_OnlyOneAdvances()
    {
        var connectionString = SharedConnectionString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var setup = CreateContext(connectionString))
        {
            await setup.Database.EnsureCreatedAsync();
            var accountId = await SeedAccountAsync(setup);
            var store = new CredentialMigrationStateStore(setup, new FixedTimeProvider());
            await store.EnsureRequiredAsync(accountId, NewBinding());

            var contenders = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var contenderContext = CreateContext(connectionString);
                var contender = new CredentialMigrationStateStore(contenderContext, new FixedTimeProvider());
                try
                {
                    await contender.AdvanceToProofValidatedAsync(
                        accountId,
                        EffectiveEmailOtpRequirement.NotRequired);
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }));

            Assert.Equal(1, contenders.Count(result => result));
            var record = (await store.FindAsync(accountId))!;
            Assert.Equal(CredentialMigrationState.ProofValidated, record.State);
            Assert.Equal(EffectiveEmailOtpRequirement.NotRequired, record.EffectiveEmailOtpRequirement);
        }
    }

    [Fact]
    public async Task ConsumeAsync_ContextBoundContinuation_IsSingleUseAndDoesNotPersistOpaqueValue()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var time = new FixedTimeProvider();
        var accountId = await SeedAccountAsync(context);
        var binding = NewBinding();
        var stateStore = new CredentialMigrationStateStore(context, time);
        await stateStore.EnsureRequiredAsync(accountId, binding);
        await stateStore.AdvanceToProofValidatedAsync(accountId, EffectiveEmailOtpRequirement.NotRequired);
        var continuationStore = new MigrationContinuationStore(context, time);
        var requestContext = Context("browser-context", "csrf");
        var continuation = await continuationStore.CreateAsync(new MigrationContinuationRequest(
            accountId,
            binding,
            requestContext));

        var persisted = await context.CredentialMigrationContinuations.SingleAsync();
        Assert.NotEqual(continuation.ProtectedValue, persisted.TokenHash);
        Assert.Equal(Hash("browser-context"), persisted.ContextHash);
        Assert.Equal(Hash("csrf"), persisted.CsrfHash);
        Assert.Equal(MigrationContinuationConsumptionOutcome.ContextMismatch, (await continuationStore.ConsumeAsync(
            continuation.ProtectedValue,
            Context("other-browser-context", "csrf"))).Outcome);
        Assert.Equal(MigrationContinuationConsumptionOutcome.Consumed, (await continuationStore.ConsumeAsync(
            continuation.ProtectedValue,
            requestContext)).Outcome);
        Assert.Equal(MigrationContinuationConsumptionOutcome.AlreadyUsed, (await continuationStore.ConsumeAsync(
            continuation.ProtectedValue,
            requestContext)).Outcome);
    }

    [Fact]
    public async Task ConsumeAsync_ExpiredContinuationAndRecovery_DoNotReplayProofOrTicket()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var time = new FixedTimeProvider();
        var accountId = await SeedAccountAsync(context);
        var binding = NewBinding();
        var stateStore = new CredentialMigrationStateStore(context, time);
        await stateStore.EnsureRequiredAsync(accountId, binding);
        await stateStore.AdvanceToProofValidatedAsync(accountId, EffectiveEmailOtpRequirement.NotRequired);
        var continuationStore = new MigrationContinuationStore(context, time);
        var requestContext = Context("browser-context", "csrf");
        var continuation = await continuationStore.CreateAsync(new MigrationContinuationRequest(
            accountId,
            binding,
            requestContext,
            time.GetUtcNow().AddMinutes(1)));

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(MigrationContinuationConsumptionOutcome.Expired, (await continuationStore.ConsumeAsync(
            continuation.ProtectedValue,
            requestContext)).Outcome);

        var evidence = new FixedRecoveryEvidence(MigrationCommittedEvidence.None);
        var recovery = new CredentialMigrationRecoveryService(
            stateStore,
            continuationStore,
            new FixedRecoveryAuthorizer(true),
            evidence);
        var recoveryContinuation = await recovery.BeginDirectoryRecoveryAsync(
            new MigrationRecoveryBeginRequest(accountId, requestContext));
        Assert.Equal(MigrationRecoveryOutcome.Reconciled, recoveryContinuation.Outcome);
        Assert.NotNull(recoveryContinuation.Continuation);
        Assert.Equal(MigrationRecoveryOutcome.Unresolved, (await recovery.RecoverAsync(new MigrationRecoveryRequest(
            Guid.Empty,
            recoveryContinuation.Continuation,
            "new-password",
            requestContext))).Outcome);
        var secondContinuation = await recovery.BeginDirectoryRecoveryAsync(
            new MigrationRecoveryBeginRequest(accountId, requestContext));
        evidence.DirectoryValue = MigrationCommittedEvidence.DirectoryCredentialCommitted;
        Assert.Equal(MigrationRecoveryOutcome.Reconciled, (await recovery.RecoverAsync(new MigrationRecoveryRequest(
            Guid.Empty,
            secondContinuation.Continuation,
            "new-password",
            requestContext))).Outcome);
        evidence.LocalValue = MigrationCommittedEvidence.LocalFinalized;
        Assert.Equal(MigrationRecoveryOutcome.Reconciled, (await recovery.RecoverAsync(new MigrationRecoveryRequest(accountId))).Outcome);
        Assert.Equal(MigrationRecoveryOutcome.Reconciled, (await recovery.RecoverAsync(new MigrationRecoveryRequest(accountId))).Outcome);
        Assert.Equal(CredentialMigrationState.LocalFinalized, (await stateStore.FindAsync(accountId))!.State);
        Assert.Equal(3, await context.CredentialMigrationContinuations.CountAsync());
        Assert.Single(await context.CredentialMigrationContinuations.ToListAsync(), record => record.ConsumedAtUtc is null);
    }

    [Fact]
    public async Task RecoverAsync_ForgedCommitmentWithoutInjectedAuthorization_DoesNotAdvanceState()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var accountId = await SeedAccountAsync(context);
        var stateStore = new CredentialMigrationStateStore(context, new FixedTimeProvider());
        await stateStore.EnsureRequiredAsync(accountId, NewBinding());
        await stateStore.AdvanceToProofValidatedAsync(accountId, EffectiveEmailOtpRequirement.NotRequired);
        var recovery = new CredentialMigrationRecoveryService(
            stateStore,
            new MigrationContinuationStore(context, new FixedTimeProvider()),
            new FixedRecoveryAuthorizer(false),
            new FixedRecoveryEvidence(MigrationCommittedEvidence.LocalFinalized));

        var begin = await recovery.BeginDirectoryRecoveryAsync(
            new MigrationRecoveryBeginRequest(accountId, Context("recovery-browser", "recovery-csrf")));
        var result = await recovery.RecoverAsync(new MigrationRecoveryRequest(accountId));

        Assert.Equal(MigrationRecoveryOutcome.Unauthorized, begin.Outcome);
        Assert.Equal(MigrationRecoveryOutcome.Unauthorized, result.Outcome);
        Assert.Equal(CredentialMigrationState.ProofValidated, (await stateStore.FindAsync(accountId))!.State);
    }

    [Fact]
    public async Task RecoverAsync_UnspecifiedContinuationRequirement_DoesNotReadEvidenceOrAdvance()
    {
        var accountId = Guid.NewGuid();
        var record = new CredentialMigrationRecord(
            accountId,
            NewBinding(),
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.Unspecified);
        var continuations = new Mock<IMigrationContinuationStore>(MockBehavior.Strict);
        continuations.Setup(store => store.ConsumeAsync("opaque", It.IsAny<MigrationContinuationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Consumed, record));
        var evidence = new Mock<IMigrationRecoveryEvidenceReader>(MockBehavior.Strict);
        var recovery = new CredentialMigrationRecoveryService(
            new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict).Object,
            continuations.Object,
            new FixedRecoveryAuthorizer(true),
            evidence.Object);

        var result = await recovery.RecoverAsync(new MigrationRecoveryRequest(
            Guid.Empty,
            "opaque",
            "new-password",
            Context("recovery-browser", "recovery-csrf")));

        Assert.Equal(MigrationRecoveryOutcome.Unresolved, result.Outcome);
        continuations.VerifyAll();
        evidence.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecoveryEvidenceReader_ExactEphemeralPasswordBind_ProvidesCommitmentWithoutPersistenceOrAudit()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var accountId = await SeedAccountAsync(context);
        var binding = NewBinding();
        var stateStore = new CredentialMigrationStateStore(context, new FixedTimeProvider());
        var record = await stateStore.EnsureRequiredAsync(accountId, binding);
        record = await stateStore.AdvanceToProofValidatedAsync(accountId, EffectiveEmailOtpRequirement.NotRequired);
        var verifier = new Mock<IDirectoryCredentialVerifier>(MockBehavior.Strict);
        verifier.Setup(service => service.VerifyCredentialAsync(binding.DirectoryObjectId, "ephemeral-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialVerificationResult(
                DirectoryCredentialOutcome.Authenticated,
                new ManagedDirectoryIdentity(binding.DirectoryObjectId, "account", true, true, false)));
        var reader = new MigrationRecoveryEvidenceReader(verifier.Object, context);
        context.ChangeTracker.Clear();

        var evidence = await reader.ReadDirectoryCommitmentAsync(record, "ephemeral-password");

        Assert.Equal(MigrationCommittedEvidence.DirectoryCredentialCommitted, evidence);
        Assert.False(context.ChangeTracker.HasChanges());
        Assert.Empty(await context.CredentialMigrationContinuations.ToListAsync());
        verifier.VerifyAll();
    }

    [Fact]
    public async Task RecoveryEvidenceReader_InvalidPasswordOrObjectStateMismatch_IsUnresolved()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var accountId = await SeedAccountAsync(context);
        var binding = NewBinding();
        var stateStore = new CredentialMigrationStateStore(context, new FixedTimeProvider());
        var record = await stateStore.EnsureRequiredAsync(accountId, binding);
        record = await stateStore.AdvanceToProofValidatedAsync(accountId, EffectiveEmailOtpRequirement.NotRequired);
        var verifier = new Mock<IDirectoryCredentialVerifier>();
        verifier.SetupSequence(service => service.VerifyCredentialAsync(binding.DirectoryObjectId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialVerificationResult(DirectoryCredentialOutcome.InvalidCredentials))
            .ReturnsAsync(new DirectoryCredentialVerificationResult(
                DirectoryCredentialOutcome.Authenticated,
                new ManagedDirectoryIdentity(Guid.NewGuid(), "account", true, true, false)))
            .ReturnsAsync(new DirectoryCredentialVerificationResult(
                DirectoryCredentialOutcome.Authenticated,
                new ManagedDirectoryIdentity(binding.DirectoryObjectId, "account", true, false, false)));
        var reader = new MigrationRecoveryEvidenceReader(verifier.Object, context);

        Assert.Equal(MigrationCommittedEvidence.None, await reader.ReadDirectoryCommitmentAsync(record, "wrong-password"));
        Assert.Equal(MigrationCommittedEvidence.None, await reader.ReadDirectoryCommitmentAsync(record, "mismatched-object"));
        Assert.Equal(MigrationCommittedEvidence.None, await reader.ReadDirectoryCommitmentAsync(record, "disabled-object"));
    }

    [Fact]
    public async Task RecoveryEvidenceReader_CurrentLocalUserAndBinding_ReconcilesLocalFinalization()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var accountId = await SeedAccountAsync(context);
        var binding = NewBinding();
        var stateStore = new CredentialMigrationStateStore(context, new FixedTimeProvider());
        await stateStore.EnsureRequiredAsync(accountId, binding);
        await stateStore.AdvanceToProofValidatedAsync(accountId, EffectiveEmailOtpRequirement.NotRequired);
        var record = await stateStore.AdvanceAsync(
            accountId,
            CredentialMigrationState.ProofValidated,
            CredentialMigrationState.DirectoryCredentialCommitted);
        var reader = new MigrationRecoveryEvidenceReader(Mock.Of<IDirectoryCredentialVerifier>(), context);

        var evidence = await reader.ReadLocalFinalizationAsync(record);

        Assert.Equal(MigrationCommittedEvidence.LocalFinalized, evidence);
    }

    [Fact]
    public async Task RecoveryEvidenceReader_RequiredEmailOtp_NeedsCurrentConsistentMfaEvidenceBeforeFinalizing()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var accountId = await SeedAccountAsync(context);
        var personId = Guid.NewGuid();
        var user = await context.Users.SingleAsync(candidate => candidate.Id == accountId);
        user.Email = "member@example.test";
        user.PersonId = personId;
        context.Persons.Add(new Person { Id = personId, Email = "member@example.test" });
        await context.SaveChangesAsync();

        var stateStore = new CredentialMigrationStateStore(context, new FixedTimeProvider());
        await stateStore.EnsureRequiredAsync(accountId, NewBinding());
        await stateStore.AdvanceToProofValidatedAsync(accountId, EffectiveEmailOtpRequirement.Required);
        var record = await stateStore.AdvanceAsync(
            accountId,
            CredentialMigrationState.ProofValidated,
            CredentialMigrationState.DirectoryCredentialCommitted);
        var reader = new MigrationRecoveryEvidenceReader(Mock.Of<IDirectoryCredentialVerifier>(), context);

        Assert.Equal(MigrationCommittedEvidence.None, await reader.ReadLocalFinalizationAsync(record));

        user.EmailMfaEnabled = true;
        var person = await context.Persons.SingleAsync(candidate => candidate.Id == personId);
        person.Email = "conflicting@example.test";
        await context.SaveChangesAsync();
        Assert.Equal(MigrationCommittedEvidence.None, await reader.ReadLocalFinalizationAsync(record));

        person.Email = "member@example.test";
        await context.SaveChangesAsync();
        Assert.Equal(MigrationCommittedEvidence.LocalFinalized, await reader.ReadLocalFinalizationAsync(record));

        var recovery = new CredentialMigrationRecoveryService(
            stateStore,
            new MigrationContinuationStore(context, new FixedTimeProvider()),
            new FixedRecoveryAuthorizer(true),
            reader);
        user.EmailMfaEnabled = false;
        await context.SaveChangesAsync();
        Assert.Equal(MigrationRecoveryOutcome.Unresolved, (await recovery.RecoverAsync(new MigrationRecoveryRequest(accountId))).Outcome);
        Assert.Equal(CredentialMigrationState.DirectoryCredentialCommitted, (await stateStore.FindAsync(accountId))!.State);

        user.EmailMfaEnabled = true;
        await context.SaveChangesAsync();
        Assert.Equal(MigrationRecoveryOutcome.Reconciled, (await recovery.RecoverAsync(new MigrationRecoveryRequest(accountId))).Outcome);
        Assert.Equal(MigrationRecoveryOutcome.Reconciled, (await recovery.RecoverAsync(new MigrationRecoveryRequest(accountId))).Outcome);
    }

    private static async Task<SqliteConnection> OpenDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        return connection;
    }

    private static ApplicationDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options);

    private static ApplicationDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString)
            .Options);

    private static async Task<Guid> SeedAccountAsync(ApplicationDbContext context)
    {
        var accountId = Guid.NewGuid();
        context.Users.Add(new ApplicationUser
        {
            Id = accountId,
            UserName = $"migration-{accountId:N}"
        });
        await context.SaveChangesAsync();
        return accountId;
    }

    private static DirectoryObjectBinding NewBinding() =>
        new("example.provider", $"subject-{Guid.NewGuid():N}", Guid.NewGuid());

    private static MigrationContinuationContext Context(string context, string csrf) =>
        new(Hash(context), Hash(csrf));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string SharedConnectionString() =>
        $"Data Source=file:credential-migration-{Guid.NewGuid():N}?mode=memory&cache=shared;Default Timeout=5";

    private sealed class FixedTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private sealed class FixedRecoveryAuthorizer(bool authorized) : IMigrationRecoveryAuthorizer
    {
        public Task<bool> IsAuthorizedAsync(CancellationToken cancellationToken = default) => Task.FromResult(authorized);
    }

    private sealed class FixedRecoveryEvidence(MigrationCommittedEvidence value) : IMigrationRecoveryEvidenceReader
    {
        public MigrationCommittedEvidence DirectoryValue { get; set; } = value;
        public MigrationCommittedEvidence LocalValue { get; set; } = value;

        public Task<MigrationCommittedEvidence> ReadDirectoryCommitmentAsync(
            CredentialMigrationRecord record,
            string newPassword,
            CancellationToken cancellationToken = default) => Task.FromResult(DirectoryValue);

        public Task<MigrationCommittedEvidence> ReadLocalFinalizationAsync(
            CredentialMigrationRecord record,
            CancellationToken cancellationToken = default) => Task.FromResult(LocalValue);
    }
}
