using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class LegacyPasswordSyncCoordinatorTests
{
    [Fact]
    public async Task SynchronizeAsync_AggregateSuccessBecomesSucceeded()
    {
        LegacyPasswordSyncRequest? sent = null;
        await using var state = await TestState.CreateAsync((request, _) =>
        {
            sent = request;
            return Task.FromResult(Trusted(Response(request.OperationId, "Success")));
        });

        var result = await state.SynchronizeAsync();

        Assert.Equal(LegacyPasswordSyncResultOutcome.Succeeded, result.Outcome);
        Assert.Equal(state.AccountIdentity.ToString("D"), sent!.AccountIdentity);
        Assert.Equal(TestState.Password, sent.Password);
        Assert.Equal(result.OperationId, sent.OperationId);
        var attempt = await state.Store.FindAsync(result.OperationId!.Value);
        Assert.Equal(LegacyPasswordSyncAttemptStatus.Succeeded, attempt!.Status);
        Assert.Equal("legacy_success", attempt.SanitizedOutcome);
    }

    [Theory]
    [InlineData("Failed", "legacy_failed")]
    [InlineData("NoOp", "legacy_no_op")]
    public async Task SynchronizeAsync_RecordsKnownAggregateRejectionWithFixedLocalOutcome(
        string aggregateOutcome,
        string expectedOutcome)
    {
        await using var state = await TestState.CreateAsync((request, _) => Task.FromResult(Trusted(
            Response(request.OperationId, aggregateOutcome))));

        var result = await state.SynchronizeAsync();

        Assert.Equal(LegacyPasswordSyncResultOutcome.Failed, result.Outcome);
        var attempt = await state.Store.FindAsync(result.OperationId!.Value);
        Assert.Equal(LegacyPasswordSyncAttemptStatus.Failed, attempt!.Status);
        Assert.Equal(expectedOutcome, attempt.SanitizedOutcome);
        Assert.DoesNotContain("target", attempt.SanitizedOutcome);
    }

    [Theory]
    [InlineData("PartialSuccess", "legacy_partial_success")]
    [InlineData("CommitUnknown", "legacy_commit_unknown")]
    public async Task SynchronizeAsync_UncertainAggregateRemainsUnknownAndNeverResends(
        string aggregateOutcome,
        string expectedOutcome)
    {
        var sends = 0;
        await using var state = await TestState.CreateAsync((request, _) =>
        {
            sends++;
            return Task.FromResult(Trusted(Response(request.OperationId, aggregateOutcome)));
        });

        var result = await state.SynchronizeAsync();

        Assert.Equal(LegacyPasswordSyncResultOutcome.Unknown, result.Outcome);
        var attempt = await state.Store.FindAsync(result.OperationId!.Value);
        Assert.Equal(LegacyPasswordSyncAttemptStatus.Unknown, attempt!.Status);
        Assert.Equal(expectedOutcome, attempt.SanitizedOutcome);

        var repeated = await state.SynchronizeAsync();
        Assert.Equal(LegacyPasswordSyncResultOutcome.DuplicateSource, repeated.Outcome);
        Assert.Equal(result.OperationId, repeated.OperationId);
        Assert.Equal(1, sends);
    }

    [Theory]
    [InlineData("missing_outcome")]
    [InlineData("wrong_operation")]
    [InlineData("extra_target")]
    [InlineData("undefined_enum")]
    [InlineData("numeric_overall")]
    [InlineData("wrong_case")]
    public async Task SynchronizeAsync_MalformedOrIncompleteResponseCannotDefaultToSuccess(string responseCase)
    {
        await using var state = await TestState.CreateAsync((request, _) =>
        {
            var json = responseCase switch
            {
                "missing_outcome" => $$"""{"operationId":"{{request.OperationId}}"}""",
                "wrong_operation" => Response(Guid.NewGuid(), "Success"),
                "extra_target" => $$"""{"operationId":"{{request.OperationId}}","outcome":"Success","targets":[]}""",
                "undefined_enum" => Response(request.OperationId, "FutureSuccess"),
                "numeric_overall" => $$"""{"operationId":"{{request.OperationId}}","outcome":0}""",
                _ => Response(request.OperationId, "success")
            };
            return Task.FromResult(Trusted(json));
        });

        var result = await state.SynchronizeAsync();

        Assert.Equal(LegacyPasswordSyncResultOutcome.Unknown, result.Outcome);
        var attempt = await state.Store.FindAsync(result.OperationId!.Value);
        Assert.Equal(LegacyPasswordSyncAttemptStatus.Unknown, attempt!.Status);
        Assert.Equal("dispatch_unknown", attempt.SanitizedOutcome);

        var newerSource = await state.AddNewerRequiredChangeAsync();
        var newerResult = await state.SynchronizeAsync(newerSource);
        Assert.Equal(LegacyPasswordSyncResultOutcome.Blocked, newerResult.Outcome);
        Assert.Equal(1, state.Transport.SendCount);
    }

    [Theory]
    [InlineData(LegacyPasswordSyncTransportOutcome.PossibleDispatchFailure)]
    [InlineData(LegacyPasswordSyncTransportOutcome.UntrustedResponse)]
    public async Task SynchronizeAsync_PossibleDispatchOrUntrustedResponseBecomesUnknown(
        LegacyPasswordSyncTransportOutcome transportOutcome)
    {
        await using var state = await TestState.CreateAsync((_, _) =>
            Task.FromResult(new LegacyPasswordSyncTransportResult(transportOutcome)));

        var result = await state.SynchronizeAsync();

        Assert.Equal(LegacyPasswordSyncResultOutcome.Unknown, result.Outcome);
        Assert.Equal(
            LegacyPasswordSyncAttemptStatus.Unknown,
            (await state.Store.FindAsync(result.OperationId!.Value))!.Status);
    }

    [Fact]
    public async Task SynchronizeAsync_ProvenPreDispatchRejectionBecomesFailed()
    {
        await using var state = await TestState.CreateAsync((_, _) => Task.FromResult(
            new LegacyPasswordSyncTransportResult(LegacyPasswordSyncTransportOutcome.PreDispatchRejected)));

        var result = await state.SynchronizeAsync();

        Assert.Equal(LegacyPasswordSyncResultOutcome.Failed, result.Outcome);
        var attempt = await state.Store.FindAsync(result.OperationId!.Value);
        Assert.Equal(LegacyPasswordSyncAttemptStatus.Failed, attempt!.Status);
        Assert.Equal("pre_dispatch_rejected", attempt.SanitizedOutcome);
    }

    [Fact]
    public async Task SynchronizeAsync_DuplicateSourceNeverSendsEvenWhenAlreadyClaimed()
    {
        await using var state = await TestState.CreateAsync((request, _) =>
            Task.FromResult(Trusted(Response(request.OperationId, "Success"))));
        var reservation = await state.Store.ReserveAsync(state.Source, state.Identity);
        var claim = await state.Store.ClaimAsync(
            reservation.Attempt!.OperationId,
            reservation.Attempt.Version,
            state.Source,
            state.Identity);
        Assert.Equal(LegacyPasswordSyncClaimOutcome.Claimed, claim.Outcome);

        var result = await state.SynchronizeAsync();

        Assert.Equal(LegacyPasswordSyncResultOutcome.DuplicateSource, result.Outcome);
        Assert.Equal(0, state.Transport.SendCount);
        Assert.Equal(
            LegacyPasswordSyncAttemptStatus.Claimed,
            (await state.Store.FindAsync(reservation.Attempt.OperationId))!.Status);
    }

    [Fact]
    public async Task SynchronizeAsync_NewerGenerationCannotOvertakeClaimedAndLateResultTouchesOnlyOlderRow()
    {
        var enteredTransport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTransport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var state = await TestState.CreateAsync(async (request, _) =>
        {
            enteredTransport.SetResult();
            await releaseTransport.Task;
            return Trusted(Response(request.OperationId, "Success"));
        });

        var olderTask = state.SynchronizeAsync();
        await enteredTransport.Task;
        var newerSource = await state.AddNewerRequiredChangeAsync();
        var newerResult = await state.SynchronizeAsync(newerSource);
        Assert.Equal(LegacyPasswordSyncResultOutcome.Blocked, newerResult.Outcome);
        Assert.Equal(1, state.Transport.SendCount);

        releaseTransport.SetResult();
        var olderResult = await olderTask;

        Assert.Equal(LegacyPasswordSyncResultOutcome.Succeeded, olderResult.Outcome);
        Assert.Equal(LegacyPasswordSyncAttemptStatus.Succeeded,
            (await state.Store.FindAsync(olderResult.OperationId!.Value))!.Status);
        Assert.Equal(LegacyPasswordSyncAttemptStatus.Reserved,
            (await state.Store.FindAsync(newerResult.OperationId!.Value))!.Status);
    }

    [Fact]
    public async Task SynchronizeAsync_StaleAccountSecurityVersionCreatesNoAttemptOrSend()
    {
        await using var state = await TestState.CreateAsync((request, _) =>
            Task.FromResult(Trusted(Response(request.OperationId, "Success"))));

        var result = await state.Coordinator.SynchronizeAsync(
            state.Source,
            LegacyPasswordSyncCohort.CompletedDirectoryRequiredChange,
            state.AccountId,
            state.BindingId,
            "stale-concurrency",
            state.SecurityStamp,
            TestState.Password);

        Assert.Equal(LegacyPasswordSyncResultOutcome.NotEligible, result.Outcome);
        Assert.Null(result.OperationId);
        Assert.Equal(0, state.Transport.SendCount);
        Assert.Empty(await state.Db.LegacyPasswordSyncAttempts.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SynchronizeAsync_RechecksAccountVersionAfterClaimBeforeSend()
    {
        await using var state = await TestState.CreateAsync((request, _) =>
            Task.FromResult(Trusted(Response(request.OperationId, "Success"))));
        var coordinator = new LegacyPasswordSyncCoordinator(
            state.Db,
            new AccountMutatingClaimStore(state.Store, state.Db, state.AccountId),
            state.RequestFactory,
            state.Transport);

        var result = await coordinator.SynchronizeAsync(
            state.Source,
            LegacyPasswordSyncCohort.CompletedDirectoryRequiredChange,
            state.AccountId,
            state.BindingId,
            state.ConcurrencyStamp,
            state.SecurityStamp,
            TestState.Password);

        Assert.Equal(LegacyPasswordSyncResultOutcome.Failed, result.Outcome);
        Assert.Equal(0, state.Transport.SendCount);
        var attempt = await state.Store.FindAsync(result.OperationId!.Value);
        Assert.Equal(LegacyPasswordSyncAttemptStatus.Failed, attempt!.Status);
        Assert.Equal("pre_dispatch_stale", attempt.SanitizedOutcome);
    }

    [Fact]
    public async Task SynchronizeAsync_TerminalSaveFailureLeavesClaimBarrierAndReturnsUnknown()
    {
        await using var state = await TestState.CreateAsync((request, _) =>
            Task.FromResult(Trusted(Response(request.OperationId, "Success"))));
        var coordinator = new LegacyPasswordSyncCoordinator(
            state.Db,
            new ThrowingResultStore(state.Store),
            state.RequestFactory,
            state.Transport);

        var result = await coordinator.SynchronizeAsync(
            state.Source,
            LegacyPasswordSyncCohort.CompletedDirectoryRequiredChange,
            state.AccountId,
            state.BindingId,
            state.ConcurrencyStamp,
            state.SecurityStamp,
            TestState.Password);

        Assert.Equal(LegacyPasswordSyncResultOutcome.Unknown, result.Outcome);
        Assert.Equal(1, state.Transport.SendCount);
        Assert.Equal(
            LegacyPasswordSyncAttemptStatus.Claimed,
            (await state.Store.FindAsync(result.OperationId!.Value))!.Status);
    }

    [Theory]
    [InlineData("http500")]
    [InlineData("timeout")]
    [InlineData("cancellation")]
    [InlineData("connection_loss")]
    public async Task HttpTransport_MapsEveryPossibleSendFailureToUnknownTransportOutcome(string failure)
    {
        var handler = new CallbackHandler(async (_, cancellationToken) =>
        {
            if (failure == "http500")
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
            if (failure == "connection_loss")
            {
                throw new HttpRequestException("connection lost");
            }
            if (failure == "cancellation")
            {
                throw new TaskCanceledException("interrupted after dispatch");
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        });
        using var client = new HttpClient(handler);
        var options = TestState.EnabledOptions();
        options.Timeout = TimeSpan.FromMilliseconds(20);
        var transport = new LegacyPasswordSyncHttpTransport(new FixedHttpClientFactory(client), Options.Create(options));

        var result = await transport.SendAsync(new LegacyPasswordSyncRequest
        {
            OperationId = Guid.NewGuid(),
            AccountIdentity = TestState.DefaultAccountIdentity.ToString("D"),
            Password = TestState.Password
        });

        Assert.Equal(
            failure == "http500"
                ? LegacyPasswordSyncTransportOutcome.UntrustedResponse
                : LegacyPasswordSyncTransportOutcome.PossibleDispatchFailure,
            result.Outcome);
        Assert.Equal(1, handler.SendCount);
    }

    [Fact]
    public async Task HttpTransport_SendsOneAuthenticatedLegacyRequestAndReturnsBoundedJson()
    {
        string? body = null;
        string? secret = null;
        var operationId = Guid.NewGuid();
        var handler = new CallbackHandler(async (request, cancellationToken) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            secret = request.Headers.GetValues("X-Internal-Secret").Single();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Response(operationId, "Success"), Encoding.UTF8, "application/json")
            };
        });
        using var client = new HttpClient(handler);
        var options = TestState.EnabledOptions();
        var transport = new LegacyPasswordSyncHttpTransport(new FixedHttpClientFactory(client), Options.Create(options));

        var result = await transport.SendAsync(new LegacyPasswordSyncRequest
        {
            OperationId = operationId,
            AccountIdentity = TestState.DefaultAccountIdentity.ToString("D"),
            Password = TestState.Password
        });

        Assert.Equal(LegacyPasswordSyncTransportOutcome.TrustedResponse, result.Outcome);
        Assert.Equal("test-secret", secret);
        using var document = JsonDocument.Parse(body!);
        Assert.Equal(3, document.RootElement.EnumerateObject().Count());
        Assert.Equal(TestState.DefaultAccountIdentity.ToString("D"), document.RootElement.GetProperty("accountIdentity").GetString());
        Assert.False(document.RootElement.TryGetProperty("ssoUserUuid", out _));
        Assert.Equal(1, handler.SendCount);
    }

    [Fact]
    [Trait("Category", "Connected")]
    public async Task ConnectedCoordinator_RealTransport_PersistsChangeAndRestoreWithVerifiedLogins()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_CONNECTED_LEGACY_PASSWORD_SYNC"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var baseUrl = RequiredConnectedValue("T3_PROVIDER_BASE_URL");
        var sharedSecret = RequiredConnectedValue("T3_PROVIDER_SHARED_SECRET");
        var account = RequiredConnectedValue("T3_LEGACY_ACCOUNT");
        var originalPassword = RequiredConnectedValue("T3_LEGACY_ORIGINAL_PASSWORD");
        var replacementPassword = RequiredConnectedValue("T3_LEGACY_REPLACEMENT_PASSWORD");
        var stableSubject = RequiredConnectedValue("T3_LEGACY_STABLE_SUBJECT");
        var attemptDatabase = RequiredConnectedValue("T3_LEGACY_ATTEMPT_DATABASE");
        Assert.True(Guid.TryParse(stableSubject, out var accountIdentity));

        using var capture = new CapturingForwardingHandler(LegacyPasswordSyncHttpClient.CreatePrimaryHandler());
        using var transportClient = new HttpClient(capture);
        var options = TestState.EnabledOptions(
            stableSubject,
            accountIdentity,
            baseUrl + "/api/password-sync",
            sharedSecret);
        options.AllowPrivateNetworkHttp = true;
        options.Timeout = TimeSpan.FromSeconds(15);
        var transport = new LegacyPasswordSyncHttpTransport(
            new FixedHttpClientFactory(transportClient),
            Options.Create(options));
        await using var state = await TestState.CreateAsync(
            (_, _) => throw new InvalidOperationException("Connected test must use the real transport."),
            transport,
            accountIdentity,
            stableSubject,
            $"Data Source={attemptDatabase}",
            options);

        var change = await state.SynchronizeAsync(password: replacementPassword);
        Assert.Equal(LegacyPasswordSyncResultOutcome.Succeeded, change.Outcome);

        var replacementLoginSucceeded = false;
        LegacyPasswordSyncResult? restore = null;
        LegacyPasswordSyncAttemptRecord? restoreAttempt = null;
        var restoredLoginSucceeded = false;
        try
        {
            var changeAttempt = await state.Store.FindAsync(change.OperationId!.Value);
            Assert.Equal(LegacyPasswordSyncAttemptStatus.Succeeded, changeAttempt!.Status);
            Assert.Equal("legacy_success", changeAttempt.SanitizedOutcome);
            Assert.Single(capture.ResponseBodies);
            AssertAggregateSuccess(capture.ResponseBodies[0]);
            replacementLoginSucceeded = await AuthenticateAsync(
                baseUrl,
                sharedSecret,
                account,
                replacementPassword,
                stableSubject);
        }
        finally
        {
            var newerSource = await state.AddNewerRequiredChangeAsync();
            restore = await state.SynchronizeAsync(newerSource, originalPassword);
            if (restore.OperationId.HasValue)
            {
                restoreAttempt = await state.Store.FindAsync(restore.OperationId.Value);
            }
            if (restore.Outcome == LegacyPasswordSyncResultOutcome.Succeeded)
            {
                restoredLoginSucceeded = await AuthenticateAsync(
                    baseUrl,
                    sharedSecret,
                    account,
                    originalPassword,
                    stableSubject);
            }
        }

        Assert.True(replacementLoginSucceeded);
        Assert.NotNull(restore);
        Assert.Equal(LegacyPasswordSyncResultOutcome.Succeeded, restore!.Outcome);
        Assert.Equal(LegacyPasswordSyncAttemptStatus.Succeeded, restoreAttempt!.Status);
        Assert.Equal("legacy_success", restoreAttempt.SanitizedOutcome);
        Assert.Equal(2, capture.ResponseBodies.Count);
        AssertAggregateSuccess(capture.ResponseBodies[1]);
        Assert.True(restoredLoginSucceeded);
    }

    private static LegacyPasswordSyncTransportResult Trusted(string json) =>
        new(LegacyPasswordSyncTransportOutcome.TrustedResponse, json);

    private static string RequiredConnectedValue(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.True(!string.IsNullOrEmpty(value), $"Required connected setting {name} is absent.");
        return value!;
    }

    private static async Task<bool> AuthenticateAsync(
        string baseUrl,
        string sharedSecret,
        string account,
        string password,
        string stableSubject)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/authenticate/login")
        {
            Content = JsonContent.Create(new
            {
                accountName = account,
                password,
                contractVersion = "1.0",
                requestedEmailOtpPolicy = "Disabled"
            })
        };
        request.Headers.Add("X-Internal-Secret", sharedSecret);
        using var response = await client.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return false;
        }
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        return root.TryGetProperty("outcome", out var outcome) && outcome.GetString() == "Authenticated" &&
               root.TryGetProperty("canonicalAccount", out var canonical) && canonical.GetString() == account &&
               root.TryGetProperty("stableSubject", out var subject) && subject.GetString() == stableSubject;
    }

    private static void AssertAggregateSuccess(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
        Assert.Equal("Success", document.RootElement.GetProperty("outcome").GetString());
        Assert.False(document.RootElement.TryGetProperty("targets", out _));
    }

    private static string Response(Guid operationId, string outcome) =>
        JsonSerializer.Serialize(new { operationId, outcome });

    private sealed class TestState : IAsyncDisposable
    {
        public const string Password = "active-request-password";
        public static readonly Guid DefaultAccountIdentity = Guid.Parse("11111111-2222-3333-4444-555555555555");
        private readonly SqliteConnection _connection;

        private TestState(
            SqliteConnection connection,
            ApplicationDbContext db,
            LegacyPasswordSyncAttemptStore store,
            ILegacyPasswordSyncRequestFactory requestFactory,
            CapturingTransport transport,
            LegacyPasswordSyncCoordinator coordinator,
            Guid accountId,
            Guid bindingId,
            string concurrencyStamp,
            string securityStamp,
            LegacyPasswordSyncSourceReference source,
            LegacyPasswordSyncAttemptIdentity identity)
        {
            _connection = connection;
            Db = db;
            Store = store;
            RequestFactory = requestFactory;
            Transport = transport;
            Coordinator = coordinator;
            AccountId = accountId;
            BindingId = bindingId;
            ConcurrencyStamp = concurrencyStamp;
            SecurityStamp = securityStamp;
            Source = source;
            Identity = identity;
        }

        public ApplicationDbContext Db { get; }
        public LegacyPasswordSyncAttemptStore Store { get; }
        public ILegacyPasswordSyncRequestFactory RequestFactory { get; }
        public CapturingTransport Transport { get; }
        public LegacyPasswordSyncCoordinator Coordinator { get; }
        public Guid AccountId { get; }
        public Guid BindingId { get; }
        public string ConcurrencyStamp { get; }
        public string SecurityStamp { get; }
        public Guid AccountIdentity => Identity.AccountIdentity;
        public LegacyPasswordSyncSourceReference Source { get; }
        public LegacyPasswordSyncAttemptIdentity Identity { get; }

        public static async Task<TestState> CreateAsync(
            Func<LegacyPasswordSyncRequest, CancellationToken, Task<LegacyPasswordSyncTransportResult>> send,
            ILegacyPasswordSyncTransport? coordinatorTransport = null,
            Guid? accountIdentity = null,
            string? stableSubject = null,
            string connectionString = "Data Source=:memory:",
            LegacyPasswordSyncOptions? suppliedOptions = null)
        {
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            var accountId = Guid.NewGuid();
            const string concurrencyStamp = "account-version-1";
            const string securityStamp = "security-version-1";
            var user = new ApplicationUser
            {
                Id = accountId,
                UserName = $"sync-{accountId:N}",
                ConcurrencyStamp = concurrencyStamp,
                SecurityStamp = securityStamp,
                IsActive = true
            };
            var binding = new ProviderSubjectDirectoryBinding(
                accountId,
                "example.provider",
                stableSubject ?? $"subject-{accountId:N}",
                Guid.NewGuid(),
                Utc(0).UtcDateTime);
            var sourceAttempt = new NativeDirectoryRecoveryAttempt(
                accountId,
                binding.DirectoryObjectId,
                NativeDirectoryCredentialOperationKind.RequiredChange,
                Utc(0));
            sourceAttempt.Complete(NativeDirectoryRecoveryStatus.Succeeded, Utc(1));
            db.Users.Add(user);
            db.ProviderSubjectDirectoryBindings.Add(binding);
            db.NativeDirectoryRecoveryAttempts.Add(sourceAttempt);
            await db.SaveChangesAsync();

            var effectiveAccountIdentity = accountIdentity ?? DefaultAccountIdentity;
            var options = suppliedOptions ?? EnabledOptions(binding.StableSubject, effectiveAccountIdentity);
            var resolver = new ConfiguredLegacyPasswordSyncTargetResolver(Options.Create(options));
            var requestFactory = new LegacyPasswordSyncRequestFactory(Options.Create(options), resolver);
            var store = new LegacyPasswordSyncAttemptStore(db, resolver, new FixedTimeProvider());
            var transport = new CapturingTransport(send);
            var coordinator = new LegacyPasswordSyncCoordinator(
                db,
                store,
                requestFactory,
                coordinatorTransport ?? transport);
            var identity = new LegacyPasswordSyncAttemptIdentity(
                accountId,
                new(binding.Id, binding.ProviderNamespace, binding.StableSubject, binding.DirectoryObjectId),
                effectiveAccountIdentity,
                "mapping-v1");
            return new(
                connection,
                db,
                store,
                requestFactory,
                transport,
                coordinator,
                accountId,
                binding.Id,
                concurrencyStamp,
                securityStamp,
                new(LegacyPasswordSyncSourceKind.RequiredChange, sourceAttempt.Id, sourceAttempt.Version),
                identity);
        }

        public Task<LegacyPasswordSyncResult> SynchronizeAsync(
            LegacyPasswordSyncSourceReference? source = null,
            string password = Password) =>
            Coordinator.SynchronizeAsync(
                source ?? Source,
                LegacyPasswordSyncCohort.CompletedDirectoryRequiredChange,
                AccountId,
                BindingId,
                ConcurrencyStamp,
                SecurityStamp,
                password);

        public async Task<LegacyPasswordSyncSourceReference> AddNewerRequiredChangeAsync()
        {
            var attempt = new NativeDirectoryRecoveryAttempt(
                AccountId,
                Identity.Binding.DirectoryObjectId,
                NativeDirectoryCredentialOperationKind.RequiredChange,
                Utc(2));
            attempt.Complete(NativeDirectoryRecoveryStatus.Succeeded, Utc(3));
            Db.NativeDirectoryRecoveryAttempts.Add(attempt);
            await Db.SaveChangesAsync();
            return new(LegacyPasswordSyncSourceKind.RequiredChange, attempt.Id, attempt.Version);
        }

        public static LegacyPasswordSyncOptions EnabledOptions(
            string stableSubject = "opaque-subject",
            Guid? accountIdentity = null,
            string endpoint = "https://provider.example.org/api/password-sync",
            string sharedSecret = "test-secret") => new()
        {
            Enabled = true,
            CompletedDirectoryRequiredChangeEnabled = true,
            Endpoint = endpoint,
            SharedSecret = sharedSecret,
            Timeout = TimeSpan.FromSeconds(1),
            TrustedProviderNamespace = "example.provider",
            ActiveMappingVersion = "mapping-v1",
            Mappings =
            [
                new LegacyPasswordSyncMappingOptions
                {
                    Enabled = true,
                    ProviderNamespace = "example.provider",
                    StableSubject = stableSubject,
                    AccountIdentity = accountIdentity ?? DefaultAccountIdentity,
                    MappingVersion = "mapping-v1"
                }
            ]
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class CapturingTransport(
        Func<LegacyPasswordSyncRequest, CancellationToken, Task<LegacyPasswordSyncTransportResult>> send)
        : ILegacyPasswordSyncTransport
    {
        public int SendCount { get; private set; }

        public Task<LegacyPasswordSyncTransportResult> SendAsync(
            LegacyPasswordSyncRequest request,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            return send(request, cancellationToken);
        }
    }

    private sealed class CapturingForwardingHandler(HttpMessageHandler innerHandler)
        : DelegatingHandler(innerHandler)
    {
        public List<string> ResponseBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            ResponseBodies.Add(body);
            response.Content = new StringContent(
                body,
                Encoding.UTF8,
                response.Content.Headers.ContentType?.MediaType ?? "application/json");
            return response;
        }
    }

    private sealed class AccountMutatingClaimStore(
        ILegacyPasswordSyncAttemptStore inner,
        ApplicationDbContext db,
        Guid accountId) : ILegacyPasswordSyncAttemptStore
    {
        public Task<LegacyPasswordSyncReservationResult> ReserveAsync(
            LegacyPasswordSyncSourceReference source,
            LegacyPasswordSyncAttemptIdentity identity,
            CancellationToken cancellationToken = default) =>
            inner.ReserveAsync(source, identity, cancellationToken);

        public async Task<LegacyPasswordSyncClaimResult> ClaimAsync(
            Guid operationId,
            long expectedVersion,
            LegacyPasswordSyncSourceReference source,
            LegacyPasswordSyncAttemptIdentity identity,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.ClaimAsync(
                operationId,
                expectedVersion,
                source,
                identity,
                cancellationToken);
            if (result.Outcome == LegacyPasswordSyncClaimOutcome.Claimed)
            {
                await db.Users.Where(user => user.Id == accountId).ExecuteUpdateAsync(
                    setters => setters.SetProperty(user => user.ConcurrencyStamp, "account-version-2"),
                    cancellationToken);
            }
            return result;
        }

        public Task<bool> RecordResultAsync(
            Guid operationId,
            long expectedVersion,
            LegacyPasswordSyncTerminalStatus status,
            string sanitizedOutcome,
            CancellationToken cancellationToken = default) =>
            inner.RecordResultAsync(operationId, expectedVersion, status, sanitizedOutcome, cancellationToken);

        public Task<LegacyPasswordSyncAttemptRecord?> FindAsync(
            Guid operationId,
            CancellationToken cancellationToken = default) =>
            inner.FindAsync(operationId, cancellationToken);
    }

    private sealed class ThrowingResultStore(ILegacyPasswordSyncAttemptStore inner) : ILegacyPasswordSyncAttemptStore
    {
        public Task<LegacyPasswordSyncReservationResult> ReserveAsync(
            LegacyPasswordSyncSourceReference source,
            LegacyPasswordSyncAttemptIdentity identity,
            CancellationToken cancellationToken = default) =>
            inner.ReserveAsync(source, identity, cancellationToken);

        public Task<LegacyPasswordSyncClaimResult> ClaimAsync(
            Guid operationId,
            long expectedVersion,
            LegacyPasswordSyncSourceReference source,
            LegacyPasswordSyncAttemptIdentity identity,
            CancellationToken cancellationToken = default) =>
            inner.ClaimAsync(operationId, expectedVersion, source, identity, cancellationToken);

        public Task<bool> RecordResultAsync(
            Guid operationId,
            long expectedVersion,
            LegacyPasswordSyncTerminalStatus status,
            string sanitizedOutcome,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("terminal save unavailable");

        public Task<LegacyPasswordSyncAttemptRecord?> FindAsync(
            Guid operationId,
            CancellationToken cancellationToken = default) =>
            inner.FindAsync(operationId, cancellationToken);
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CallbackHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            return send(request, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Utc(10);
    }

    private static DateTimeOffset Utc(int day) =>
        new(2026, 9, 12 + day, 0, 0, 0, TimeSpan.Zero);
}
