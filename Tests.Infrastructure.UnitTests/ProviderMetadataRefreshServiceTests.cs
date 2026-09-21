using System.Net;
using System.Text;
using Core.Application;
using Core.Application.Ports;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class ProviderMetadataRefreshServiceTests
{
    [Fact]
    public async Task RefreshAsync_DefaultDisabled_DoesNotAccessDatabaseOrHttp()
    {
        var context = new Mock<IApplicationDbContext>(MockBehavior.Strict);
        var handler = new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called."));
        var service = new ProviderMetadataRefreshService(
            context.Object,
            new HttpClient(handler),
            Options.Create(new ProviderMetadataRefreshOptions()));

        var outcome = await service.RefreshAsync("example.provider", "subject-1");

        Assert.Equal(ProviderMetadataRefreshOutcome.Disabled, outcome);
        Assert.Equal(0, handler.CallCount);
        context.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RefreshAsync_MissingExactBinding_DoesNotCallProviderOrCreateSnapshot()
    {
        await using var database = await OpenDatabaseAsync();
        await using var context = CreateContext(database);
        var handler = new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called."));
        var service = CreateService(context, handler);

        var outcome = await service.RefreshAsync("example.provider", "missing-subject");

        Assert.Equal(ProviderMetadataRefreshOutcome.BindingNotFound, outcome);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(await context.ProviderMetadataSnapshots.ToListAsync());
    }

    [Fact]
    public async Task RefreshAsync_CollationEquivalentButNotOrdinalBinding_DoesNotCallProvider()
    {
        await using var database = await OpenDatabaseAsync();
        await using var context = CreateContext(database);
        SeedBinding(context);
        var handler = new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called."));
        var service = CreateService(context, handler);

        var outcome = await service.RefreshAsync("EXAMPLE.PROVIDER", "subject-1");

        Assert.Equal(ProviderMetadataRefreshOutcome.BindingNotFound, outcome);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(await context.ProviderMetadataSnapshots.ToListAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{}", ProviderMetadataRefreshOutcome.Unavailable)]
    [InlineData(HttpStatusCode.Unauthorized, "{}", ProviderMetadataRefreshOutcome.AuthenticationFailed)]
    [InlineData(HttpStatusCode.Forbidden, "{}", ProviderMetadataRefreshOutcome.AuthenticationFailed)]
    [InlineData(HttpStatusCode.BadRequest, "{}", ProviderMetadataRefreshOutcome.Unavailable)]
    [InlineData(HttpStatusCode.OK, "null", ProviderMetadataRefreshOutcome.Missing)]
    [InlineData(HttpStatusCode.OK, "", ProviderMetadataRefreshOutcome.Malformed)]
    [InlineData(HttpStatusCode.OK, "{not-json", ProviderMetadataRefreshOutcome.Malformed)]
    [InlineData(HttpStatusCode.OK,
        "{\"contractVersion\":\"1.0\",\"providerNamespace\":\"other\",\"stableSubject\":\"subject-1\",\"legacyCategories\":[],\"legacyStatus\":\"Complete\",\"emailTrustOrigin\":\"Unknown\"}",
        ProviderMetadataRefreshOutcome.Malformed)]
    [InlineData(HttpStatusCode.OK,
        "{\"contractVersion\":\"1.0\",\"providerNamespace\":\"example.provider\",\"stableSubject\":\"subject-1\",\"emailTrustOrigin\":\"FutureOrigin\"}",
        ProviderMetadataRefreshOutcome.Malformed)]
    [InlineData(HttpStatusCode.OK,
        "{\"contractVersion\":\"1.0\",\"providerNamespace\":\"example.provider\",\"stableSubject\":\"subject-1\",\"emailTrustOrigin\":null}",
        ProviderMetadataRefreshOutcome.Malformed)]
    [InlineData(HttpStatusCode.OK,
        "{\"contractVersion\":\"2.0\",\"providerNamespace\":\"example.provider\",\"stableSubject\":\"subject-1\"}",
        ProviderMetadataRefreshOutcome.Unsupported)]
    [InlineData(HttpStatusCode.OK,
        "{\"providerNamespace\":\"example.provider\",\"stableSubject\":\"subject-1\"}",
        ProviderMetadataRefreshOutcome.Malformed)]
    [InlineData(HttpStatusCode.OK,
        "{\"contractVersion\":\"1.0\",\"providerNamespace\":\"example.provider\",\"stableSubject\":\"subject-1\",\"verifiedAt\":\"not-a-timestamp\"}",
        ProviderMetadataRefreshOutcome.Malformed)]
    [InlineData(HttpStatusCode.OK,
        "{\"contractVersion\":\"1.0\",\"providerNamespace\":\"example.provider\",\"stableSubject\":\"subject-1\",\"email\":\"source@example.test\",\"emailTrustOrigin\":\"Unknown\"}",
        ProviderMetadataRefreshOutcome.Untrusted)]
    [InlineData(HttpStatusCode.OK,
        "{\"contractVersion\":\"1.0\",\"providerNamespace\":\"example.provider\",\"stableSubject\":\"subject-1\",\"email\":\"source@example.test\",\"emailTrustOrigin\":\"SourceVerified\",\"verifiedAt\":\"2099-01-01T00:00:00Z\"}",
        ProviderMetadataRefreshOutcome.Untrusted)]
    public async Task RefreshAsync_ProviderFailureOrUntrustedResponse_ClearsAllPriorEvidence(
        HttpStatusCode statusCode,
        string body,
        ProviderMetadataRefreshOutcome expectedOutcome)
    {
        await using var database = await OpenDatabaseAsync();
        await using var context = CreateContext(database);
        var binding = SeedBinding(context);
        var prior = new ProviderMetadataSnapshot(binding.Id, DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
        prior.Refresh(
            "prior@example.test",
            ProviderEmailTrustOrigin.SourceVerified,
            DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-01T00:01:00Z"));
        context.ProviderMetadataSnapshots.Add(prior);
        await context.SaveChangesAsync();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));
        var service = CreateService(context, handler);

        var outcome = await service.RefreshAsync(binding.ProviderNamespace, binding.StableSubject);

        Assert.Equal(expectedOutcome, outcome);
        Assert.Equal(expectedOutcome.ToString(), (await context.ProviderMetadataSnapshots.SingleAsync()).EvidenceState.ToString());
        var snapshot = await context.ProviderMetadataSnapshots.SingleAsync();
        Assert.Null(snapshot.Email);
        Assert.Equal(ProviderEmailTrustOrigin.Unknown, snapshot.EmailTrustOrigin);
        Assert.Null(snapshot.VerifiedAt);
    }

    [Fact]
    public async Task RefreshAsync_UnknownFields_CannotSupplyAuthorityOrObservationEvidence()
    {
        await using var database = await OpenDatabaseAsync();
        await using var context = CreateContext(database);
        var binding = SeedBinding(context);
        var prior = new ProviderMetadataSnapshot(binding.Id, DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
        prior.Refresh(
            "prior@example.test",
            ProviderEmailTrustOrigin.SourceVerified,
            DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-01T00:01:00Z"));
        context.ProviderMetadataSnapshots.Add(prior);
        await context.SaveChangesAsync();
        const string responseJson =
            "{\"contractVersion\":\"1.0\",\"providerNamespace\":\"example.provider\",\"stableSubject\":\"subject-1\"," +
            "\"legacyCategories\":[],\"legacyStatus\":\"Unavailable\"," +
            "\"email\":\"fresh@example.test\",\"emailTrustOrigin\":\"SourceVerified\"," +
            "\"verifiedAt\":\"2026-09-02T03:04:05Z\",\"legacySourceTimestamp\":\"2026-09-02T03:05:00Z\"}";
        var handler = RespondingHandler(responseJson);
        var service = CreateService(context, handler);

        var outcome = await service.RefreshAsync(binding.ProviderNamespace, binding.StableSubject);

        Assert.Equal(ProviderMetadataRefreshOutcome.Refreshed, outcome);
        var snapshot = await context.ProviderMetadataSnapshots.SingleAsync();
        Assert.Equal(ProviderMetadataEvidenceState.Available, snapshot.EvidenceState);
        Assert.Equal("fresh@example.test", snapshot.Email);
        Assert.Equal(ProviderEmailTrustOrigin.SourceVerified, snapshot.EmailTrustOrigin);
        Assert.Equal(DateTimeOffset.Parse("2026-09-02T03:04:05Z"), snapshot.VerifiedAt);
    }

    [Fact]
    public async Task RefreshAsync_MissingOptionalEvidenceFields_RemainsUntrusted()
    {
        await using var database = await OpenDatabaseAsync();
        await using var context = CreateContext(database);
        var binding = SeedBinding(context);
        const string responseJson =
            "{\"contractVersion\":\"1.0\",\"providerNamespace\":\"example.provider\",\"stableSubject\":\"subject-1\"}";
        var service = CreateService(context, RespondingHandler(responseJson));

        var outcome = await service.RefreshAsync(binding.ProviderNamespace, binding.StableSubject);

        Assert.Equal(ProviderMetadataRefreshOutcome.Untrusted, outcome);
        var snapshot = await context.ProviderMetadataSnapshots.SingleAsync();
        Assert.Equal(ProviderMetadataEvidenceState.Untrusted, snapshot.EvidenceState);
        Assert.Null(snapshot.Email);
        Assert.Equal(ProviderEmailTrustOrigin.Unknown, snapshot.EmailTrustOrigin);
        Assert.Null(snapshot.VerifiedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshAsync_TransportFailureOrTimeout_PreservesDistinctOutcome(bool timeout)
    {
        await using var database = await OpenDatabaseAsync();
        await using var context = CreateContext(database);
        var binding = SeedBinding(context);
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            if (timeout)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            throw new HttpRequestException("Synthetic transport failure.");
        });
        var service = CreateService(context, handler, TimeSpan.FromMilliseconds(25));

        var outcome = await service.RefreshAsync(binding.ProviderNamespace, binding.StableSubject);

        Assert.Equal(timeout ? ProviderMetadataRefreshOutcome.TimedOut : ProviderMetadataRefreshOutcome.Unavailable, outcome);
        Assert.Equal(1, handler.CallCount);
        var snapshot = await context.ProviderMetadataSnapshots.SingleAsync();
        Assert.Equal(timeout ? ProviderMetadataEvidenceState.TimedOut : ProviderMetadataEvidenceState.Unavailable,
            snapshot.EvidenceState);
        Assert.Null(snapshot.Email);
        Assert.Equal(ProviderEmailTrustOrigin.Unknown, snapshot.EmailTrustOrigin);
        Assert.Null(snapshot.VerifiedAt);
    }

    [Fact]
    public async Task RefreshAsync_CallerCancellation_PropagatesWithoutRetry()
    {
        await using var database = await OpenDatabaseAsync();
        await using var context = CreateContext(database);
        var binding = SeedBinding(context);
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHttpMessageHandler((_, cancellationToken) =>
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation must propagate.");
        });
        var service = CreateService(context, handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RefreshAsync(
            binding.ProviderNamespace, binding.StableSubject, cancellation.Token));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task LegacyStoredCache_RemainsQuarantinedUntilIndependentRefresh()
    {
        await using var database = await OpenDatabaseAsync();
        await using var context = CreateContext(database);
        var binding = SeedBinding(context);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE ProviderMetadataSnapshots (
                Id TEXT NOT NULL, ProviderSubjectDirectoryBindingId TEXT NOT NULL,
                Email TEXT, EmailTrustOrigin TEXT, LegacyCategoriesJson TEXT,
                LegacyStatus TEXT, LegacySourceTimestamp TEXT);
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ProviderMetadataSnapshots
            VALUES ({Guid.NewGuid()}, {binding.Id}, {"legacy@example.test"}, {"SourceVerified"},
                {"[\"synthetic-category\"]"}, {"Complete"}, {"2099-01-01T00:00:00Z"});
            """);
        var policy = new RecoveryVerificationPolicyEvaluator(context,
            Options.Create(new RecoveryVerificationPolicyOptions
            {
                Enabled = true,
                CurrentPeriodId = "period-a",
                EffectiveAtUtc = DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
                GraceEndsAtUtc = DateTimeOffset.Parse("2026-09-02T00:00:00Z"),
                AcceptSourceVerifiedEmails = true
            }), new FixedTimeProvider());

        var before = await policy.EvaluateAsync(binding.LocalAccountId);

        Assert.Empty(context.ProviderMetadataSnapshots);
        Assert.Equal(ProviderMetadataEvidenceState.Missing, Assert.Single(before.MetadataEvidenceStates));
        Assert.True(before.RequiresCurrentPeriodVerification);
        Assert.False(before.RecoveryEmail.CanReceiveRecoveryOtp);
        var service = CreateService(context, RespondingHandler("""
            {"contractVersion":"1.0","providerNamespace":"example.provider","stableSubject":"subject-1",
             "email":"fresh@example.test","emailTrustOrigin":"SourceVerified",
             "extensions":{"legacyCategories":["synthetic-category"],"canReset":true},
             "legacyCategories":["synthetic-category"],"legacyGroups":["synthetic-group"],"legacyRoles":["synthetic-role"]}
            """));
        Assert.Equal(ProviderMetadataRefreshOutcome.Refreshed,
            await service.RefreshAsync(binding.ProviderNamespace, binding.StableSubject));

        var after = await policy.EvaluateAsync(binding.LocalAccountId);

        Assert.True(after.RequiresCurrentPeriodVerification);
        Assert.True(after.RecoveryEmail.CanReceiveRecoveryOtp);
        Assert.Equal("fresh@example.test", after.RecoveryEmail.Address);
        Assert.False(after.HasCurrentPeriodVerification);
        await using var retained = database.CreateCommand();
        retained.CommandText = "SELECT Email FROM ProviderMetadataSnapshots";
        Assert.Equal("legacy@example.test", await retained.ExecuteScalarAsync());
    }

    private static ProviderMetadataRefreshService CreateService(
        IApplicationDbContext context,
        HttpMessageHandler handler,
        TimeSpan? timeout = null) =>
        new(
            context,
            new HttpClient(handler),
            Options.Create(new ProviderMetadataRefreshOptions
            {
                Enabled = true,
                Endpoint = "https://provider.example.test/api/authenticate/metadata",
                SharedSecret = "test-secret",
                Timeout = timeout ?? TimeSpan.FromSeconds(5)
            }),
            new FixedTimeProvider());

    private static StubHttpMessageHandler RespondingHandler(string body) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));

    private static ProviderSubjectDirectoryBinding SeedBinding(ApplicationDbContext context)
    {
        var binding = new ProviderSubjectDirectoryBinding(
            Guid.NewGuid(),
            "example.provider",
            "subject-1",
            Guid.NewGuid(),
            DateTime.UtcNow);
        context.ProviderSubjectDirectoryBindings.Add(binding);
        context.SaveChanges();
        return binding;
    }

    private static async Task<SqliteConnection> OpenDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await CreateContext(connection).Database.EnsureCreatedAsync();
        return connection;
    }

    private static ApplicationDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return responseFactory(request, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-09-08T00:00:00Z");
    }
}
