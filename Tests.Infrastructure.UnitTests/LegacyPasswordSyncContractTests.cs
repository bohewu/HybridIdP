using System.Net;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Ports;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class LegacyPasswordSyncContractTests
{
    private static readonly Guid AccountIdentity =
        Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Defaults_KeepEndpointAndEveryCohortOff()
    {
        var options = new LegacyPasswordSyncOptions();

        Assert.False(options.Enabled);
        Assert.False(options.CompletedDirectoryRecoveryEnabled);
        Assert.False(options.CompletedDirectoryRequiredChangeEnabled);
        Assert.False(options.Stage2MigrationEnabled);
        Assert.Empty(options.Mappings);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Timeout);
    }

    [Fact]
    public void Validator_RequiresProtectedAuthenticatedExactConfigurationBeforeEnablement()
    {
        var validator = new LegacyPasswordSyncOptionsValidator();
        var options = EnabledOptions();
        Assert.True(validator.Validate(null, options).Succeeded);

        options.Endpoint = "http://provider.example.org/api/password-sync";
        Assert.True(validator.Validate(null, options).Failed);
        options.Endpoint = "https://provider.example.org/api/password-sync";
        options.SharedSecret = null;
        Assert.True(validator.Validate(null, options).Failed);
        options.SharedSecret = " test-secret";
        Assert.True(validator.Validate(null, options).Failed);
        options.SharedSecret = "test-secret";
        options.Mappings[0].ProviderNamespace = "other.provider";
        Assert.True(validator.Validate(null, options).Failed);
    }

    [Theory]
    [InlineData("https://provider.example.org/api/password-sync", false, true)]
    [InlineData("http://provider.example.org/api/password-sync", false, false)]
    [InlineData("http://provider.example.org/api/password-sync", true, true)]
    [InlineData("https://user@provider.example.org/api/password-sync", false, false)]
    [InlineData("https://provider.example.org/api/password-sync#fragment", false, false)]
    public void Validator_RequiresProtectedEndpoint(string endpoint, bool allowPrivateHttp, bool expected)
    {
        var options = EnabledOptions();
        options.Endpoint = endpoint;
        options.AllowPrivateNetworkHttp = allowPrivateHttp;

        Assert.Equal(expected, new LegacyPasswordSyncOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void Validator_BoundsTimeout(int seconds, bool expected)
    {
        var options = EnabledOptions();
        options.Timeout = TimeSpan.FromSeconds(seconds);

        Assert.Equal(expected, new LegacyPasswordSyncOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Validator_DiagnosticsAndOptionsString_DoNotExposeCallerSecret()
    {
        var options = EnabledOptions();
        options.Endpoint = null;
        const string secret = "caller-secret-must-not-appear";
        options.SharedSecret = secret;

        var result = new LegacyPasswordSyncOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.DoesNotContain(secret, string.Join(" ", result.Failures));
        Assert.DoesNotContain(secret, options.ToString());
    }

    [Fact]
    public void Validator_AllowsDormantConfigurationWhileFactoryHonorsMasterDisableGate()
    {
        var options = EnabledOptions();
        options.Enabled = false;
        options.Endpoint = null;
        options.SharedSecret = null;
        var factory = new LegacyPasswordSyncRequestFactory(Options.Create(options), CreateResolver(options));

        var validation = new LegacyPasswordSyncOptionsValidator().Validate(null, options);
        var created = factory.TryCreate(
            Guid.NewGuid(),
            LegacyPasswordSyncCohort.Stage2Migration,
            "example.provider",
            "opaque-subject",
            "mapping-v1",
            "password",
            out var request,
            out var target);

        Assert.True(validation.Succeeded);
        Assert.False(created);
        Assert.Null(request);
        Assert.Null(target);
    }

    [Fact]
    public void PublicContract_HasExactMemberSetsAndAggregateOutcomes()
    {
        Assert.Equal(
            ["AccountIdentity", "OperationId", "Password"],
            typeof(LegacyPasswordSyncRequest).GetProperties().Select(property => property.Name).Order().ToArray());
        Assert.Equal(
            ["OperationId", "Outcome"],
            typeof(LegacyPasswordSyncResponse).GetProperties().Select(property => property.Name).Order().ToArray());
        Assert.Equal(
            ["CommitUnknown", "Failed", "NoOp", "PartialSuccess", "Success"],
            Enum.GetNames<LegacyPasswordSyncOutcome>().Order().ToArray());

        var request = new LegacyPasswordSyncRequest
        {
            OperationId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"),
            AccountIdentity = AccountIdentity.ToString("D"),
            Password = "active-request-password"
        };
        var response = new LegacyPasswordSyncResponse
        {
            OperationId = request.OperationId,
            Outcome = LegacyPasswordSyncOutcome.PartialSuccess
        };
        using var requestJson = JsonDocument.Parse(JsonSerializer.Serialize(
            request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var responseJson = JsonDocument.Parse(JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(
            ["accountIdentity", "operationId", "password"],
            requestJson.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(
            ["operationId", "outcome"],
            responseJson.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.DoesNotContain("sso", requestJson.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("target", responseJson.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(request.Password, request.ToString());
    }

    [Theory]
    [InlineData("11111111-2222-3333-4444-555555555555", true)]
    [InlineData("11111111-2222-3333-4444-55555555555A", false)]
    [InlineData(" 11111111-2222-3333-4444-555555555555", false)]
    [InlineData("11111111222233334444555555555555", false)]
    [InlineData("", false)]
    [InlineData("11111111-2222-3333-4444-555555555555\n", false)]
    public void AccountIdentity_RequiresCanonicalLowercaseGuidDForm(string value, bool expected) =>
        Assert.Equal(expected, LegacyPasswordSyncContract.IsCanonicalAccountIdentity(value));

    [Fact]
    public void AccountIdentity_RejectsValueBeyondPublicBound() =>
        Assert.False(LegacyPasswordSyncContract.IsCanonicalAccountIdentity(new string('a', 257)));

    [Theory]
    [InlineData("{\"operationId\":\"aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb\",\"outcome\":\"Success\"}", true)]
    [InlineData("{\"operationId\":\"aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb\",\"outcome\":\"success\"}", false)]
    [InlineData("{\"operationId\":\"aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb\",\"outcome\":0}", false)]
    [InlineData("{\"operationId\":\"aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb\",\"outcome\":\"Success\",\"targets\":[]}", false)]
    [InlineData("{\"operationId\":\"bbbbbbbb-1111-2222-3333-aaaaaaaaaaaa\",\"outcome\":\"Success\"}", false)]
    public void ResponseParser_RequiresExactAggregateOnlyCorrelatedResponse(string json, bool expected)
    {
        var operationId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");

        var parsed = LegacyPasswordSyncContract.TryParseResponse(json, operationId, out var response);

        Assert.Equal(expected, parsed);
        Assert.Equal(expected, response is not null);
    }

    [Fact]
    public void ResolverAndFactory_RenderMappedGuidAsCanonicalOpaqueIdentity()
    {
        var options = EnabledOptions();
        var resolver = CreateResolver(options);
        var factory = new LegacyPasswordSyncRequestFactory(Options.Create(options), resolver);
        var operationId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");

        var exact = resolver.Resolve("example.provider", "opaque-subject", "mapping-v1");
        var wrongCase = resolver.Resolve("EXAMPLE.PROVIDER", "opaque-subject", "mapping-v1");
        var created = factory.TryCreate(
            operationId,
            LegacyPasswordSyncCohort.Stage2Migration,
            "example.provider",
            "opaque-subject",
            "mapping-v1",
            "active-request-password",
            out var request,
            out var target);

        Assert.Equal(LegacyPasswordSyncMappingOutcome.Resolved, exact.Outcome);
        Assert.Equal(LegacyPasswordSyncMappingOutcome.Missing, wrongCase.Outcome);
        Assert.True(created);
        Assert.Equal(AccountIdentity, target!.AccountIdentityGuid);
        Assert.Equal(AccountIdentity.ToString("D"), request!.AccountIdentity);
        Assert.True(LegacyPasswordSyncContract.TryValidateRequest(request));
    }

    [Fact]
    public void Resolver_FailsClosedForDuplicateStaleContradictoryAndMalformedMappings()
    {
        var duplicateOptions = EnabledOptions();
        duplicateOptions.Mappings.Add(Clone(duplicateOptions.Mappings[0]));
        var duplicate = CreateResolver(duplicateOptions).Resolve("example.provider", "opaque-subject");

        var stale = CreateResolver(EnabledOptions()).Resolve(
            "example.provider", "opaque-subject", "mapping-v0");

        var contradictoryOptions = EnabledOptions();
        var contradictoryMapping = Clone(contradictoryOptions.Mappings[0]);
        contradictoryMapping.AccountIdentity = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        contradictoryOptions.Mappings.Add(contradictoryMapping);
        var contradictory = CreateResolver(contradictoryOptions).Resolve(
            "example.provider", "opaque-subject");

        var malformedOptions = EnabledOptions();
        malformedOptions.Mappings[0].AccountIdentity = Guid.Empty;
        var malformed = CreateResolver(malformedOptions).Resolve("example.provider", "opaque-subject");

        Assert.Equal(LegacyPasswordSyncMappingOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(LegacyPasswordSyncMappingOutcome.Stale, stale.Outcome);
        Assert.Equal(LegacyPasswordSyncMappingOutcome.Contradictory, contradictory.Outcome);
        Assert.Equal(LegacyPasswordSyncMappingOutcome.Malformed, malformed.Outcome);
    }

    [Fact]
    public void HttpHandler_DisablesRedirectsAndCookies()
    {
        using var handler = Assert.IsType<HttpClientHandler>(LegacyPasswordSyncHttpClient.CreatePrimaryHandler());

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
    }

    [Fact]
    public async Task Transport_SendsExactAuthenticatedRequestAndAcceptsExactCorrelatedResponse()
    {
        var operationId = Guid.NewGuid();
        string? requestBody = null;
        string? secret = null;
        var handler = new CallbackHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            secret = request.Headers.GetValues("X-Internal-Secret").Single();
            return JsonResponse(Response(operationId, "Success"));
        });
        using var client = new HttpClient(handler);
        var transport = new LegacyPasswordSyncHttpTransport(
            new FixedHttpClientFactory(client),
            Options.Create(EnabledOptions()));

        var result = await transport.SendAsync(Request(operationId));

        Assert.Equal(LegacyPasswordSyncTransportOutcome.TrustedResponse, result.Outcome);
        Assert.Equal("test-secret", secret);
        using var json = JsonDocument.Parse(requestBody!);
        Assert.Equal(3, json.RootElement.EnumerateObject().Count());
        Assert.Equal(AccountIdentity.ToString("D"), json.RootElement.GetProperty("accountIdentity").GetString());
        Assert.False(json.RootElement.TryGetProperty("ssoUserUuid", out _));
        Assert.Equal(1, handler.SendCount);
    }

    [Theory]
    [InlineData("wrong-operation")]
    [InlineData("wrong-content-type")]
    [InlineData("status")]
    [InlineData("malformed")]
    [InlineData("extra-field")]
    [InlineData("oversized")]
    public async Task Transport_FailsClosedForUntrustedResponses(string failure)
    {
        var operationId = Guid.NewGuid();
        var handler = new CallbackHandler((_, _) => Task.FromResult(failure switch
        {
            "wrong-operation" => JsonResponse(Response(Guid.NewGuid(), "Success")),
            "wrong-content-type" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Response(operationId, "Success"), Encoding.UTF8, "text/plain")
            },
            "status" => new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            },
            "malformed" => JsonResponse("{"),
            "extra-field" => JsonResponse($$"""{"operationId":"{{operationId}}","outcome":"Success","targets":[]}"""),
            _ => JsonResponse(new string('x', 64 * 1024 + 1))
        }));
        using var client = new HttpClient(handler);
        var transport = new LegacyPasswordSyncHttpTransport(
            new FixedHttpClientFactory(client),
            Options.Create(EnabledOptions()));

        var result = await transport.SendAsync(Request(operationId));

        Assert.Equal(LegacyPasswordSyncTransportOutcome.UntrustedResponse, result.Outcome);
    }

    [Fact]
    public async Task Transport_DisabledConfigurationRejectsBeforeDispatch()
    {
        var options = EnabledOptions();
        options.Enabled = false;
        var handler = new CallbackHandler((_, _) => throw new InvalidOperationException("must not dispatch"));
        using var client = new HttpClient(handler);
        var transport = new LegacyPasswordSyncHttpTransport(
            new FixedHttpClientFactory(client),
            Options.Create(options));

        var result = await transport.SendAsync(Request(Guid.NewGuid()));

        Assert.Equal(LegacyPasswordSyncTransportOutcome.PreDispatchRejected, result.Outcome);
        Assert.Equal(0, handler.SendCount);
    }

    [Fact]
    public async Task Transport_LogsNoPasswordSecretOrResponseBody()
    {
        const string password = "password-must-not-appear";
        const string secret = "secret-must-not-appear";
        const string responseValue = "response-value-must-not-appear";
        var options = EnabledOptions();
        options.SharedSecret = secret;
        var logger = new CapturingLogger<LegacyPasswordSyncHttpTransport>();
        var handler = new CallbackHandler((_, _) => Task.FromResult(JsonResponse(responseValue)));
        using var client = new HttpClient(handler);
        var transport = new LegacyPasswordSyncHttpTransport(
            new FixedHttpClientFactory(client),
            Options.Create(options),
            logger);
        var request = Request(Guid.NewGuid(), password);

        var result = await transport.SendAsync(request);
        var logs = string.Join(" ", logger.Messages);

        Assert.Equal(LegacyPasswordSyncTransportOutcome.UntrustedResponse, result.Outcome);
        Assert.DoesNotContain(password, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(responseValue, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(request.AccountIdentity, logs, StringComparison.Ordinal);
    }

    private static ConfiguredLegacyPasswordSyncTargetResolver CreateResolver(
        LegacyPasswordSyncOptions options) => new(Options.Create(options));

    private static LegacyPasswordSyncOptions EnabledOptions() => new()
    {
        Enabled = true,
        Stage2MigrationEnabled = true,
        Endpoint = "https://provider.example.org/api/password-sync",
        SharedSecret = "test-secret",
        Timeout = TimeSpan.FromSeconds(1),
        TrustedProviderNamespace = "example.provider",
        ActiveMappingVersion = "mapping-v1",
        Mappings =
        [
            new LegacyPasswordSyncMappingOptions
            {
                Enabled = true,
                ProviderNamespace = "example.provider",
                StableSubject = "opaque-subject",
                AccountIdentity = AccountIdentity,
                MappingVersion = "mapping-v1"
            }
        ]
    };

    private static LegacyPasswordSyncMappingOptions Clone(LegacyPasswordSyncMappingOptions mapping) => new()
    {
        Enabled = mapping.Enabled,
        ProviderNamespace = mapping.ProviderNamespace,
        StableSubject = mapping.StableSubject,
        AccountIdentity = mapping.AccountIdentity,
        MappingVersion = mapping.MappingVersion
    };

    private static LegacyPasswordSyncRequest Request(Guid operationId, string password = "active-request-password") =>
        new()
        {
            OperationId = operationId,
            AccountIdentity = AccountIdentity.ToString("D"),
            Password = password
        };

    private static string Response(Guid operationId, string outcome) =>
        $$"""{"operationId":"{{operationId}}","outcome":"{{outcome}}"}""";

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

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

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
