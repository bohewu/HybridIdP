using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Ports;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class ProviderLifecycleClientTests
{
    private static ProviderLifecycleBinding Binding => new() { ProviderNamespace = "example.provider", StableSubject = "subject-001" };
    private static ProviderLifecycleOptions Settings() => new()
    {
        Enabled = true, Endpoint = "https://provider.example.invalid/exact/path", SharedSecret = "synthetic-test-secret",
        TimeoutSeconds = 1, RequiredAccounts = [new() { LocalAccountId = Guid.Parse("cb229486-2d0f-4bd1-a975-9b49c9dcd0fa"),
            BindingKind = ProviderLifecycleRequiredAccount.DirectoryBinding, ProviderNamespace = "example.provider",
            SourceAuthority = "authority-1", MappingVersion = "mapping-1" }]
    };
    private static async Task<string> ResponseBody(HttpRequestMessage message, CancellationToken token)
    {
        using var request = JsonDocument.Parse(await message.Content!.ReadAsByteArrayAsync(token));
        return JsonSerializer.Serialize(new { contractType = "lifecycle-status", contractVersion = "1.0",
            requestId = request.RootElement.GetProperty("requestId").GetString(), outcome = "Found",
            binding = new { providerNamespace = Binding.ProviderNamespace, stableSubject = Binding.StableSubject }, accountState = "Enabled",
            evidence = new { sourceAuthority = "authority-1", mappingVersion = "mapping-1", snapshotVersion = "snapshot-1",
                observedAt = "2026-10-03T00:00:00.000Z", effectiveFrom = "2026-10-02T00:00:00.000Z", effectiveUntil = "2026-10-04T00:00:00.000Z" } });
    }
    private static HttpResponseMessage Response(string body, int status = 200)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };
        response.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        return response;
    }

    [Fact]
    public async Task LookupAsync_ShouldSendExactFixedRequestWithFreshCorrelationEachCall()
    {
        var ids = new HashSet<string>();
        var settings = Settings();
        using var handler = new Handler(async (message, token) =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal(settings.Endpoint, message.RequestUri!.AbsoluteUri);
            Assert.Equal(settings.SharedSecret, Assert.Single(message.Headers.GetValues("X-Internal-Secret")));
            Assert.Equal("lifecycle-status/1.0", Assert.Single(message.Headers.GetValues("X-Provider-Contract")));
            Assert.Equal("application/json", message.Content!.Headers.ContentType!.ToString());
            using var body = JsonDocument.Parse(await message.Content.ReadAsByteArrayAsync(token));
            Assert.Equal(5, body.RootElement.EnumerateObject().Count());
            var id = body.RootElement.GetProperty("requestId").GetString()!;
            Assert.True(ProviderLifecycleContract.IsRequestId(id));
            Assert.True(ids.Add(id));
            Assert.Equal("lifecycle-status", body.RootElement.GetProperty("contractType").GetString());
            Assert.Equal("1.0", body.RootElement.GetProperty("contractVersion").GetString());
            Assert.Equal(Binding.ProviderNamespace, body.RootElement.GetProperty("providerNamespace").GetString());
            Assert.Equal(Binding.StableSubject, body.RootElement.GetProperty("stableSubject").GetString());
            return Response(await ResponseBody(message, token));
        });
        using var http = new HttpClient(handler);
        var client = new ProviderLifecycleClient(http, Options.Create(settings));
        for (var i = 0; i < 2; i++)
        {
            var result = await client.LookupAsync(Binding);
            Assert.Equal(ProviderLifecycleFailure.None, result.Failure);
            Assert.Equal(ProviderLifecycleOutcome.Found, result.Response!.Outcome);
        }
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task LookupAsync_ShouldNotDispatchWhenDisabledOrInvalidBinding()
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No dispatch expected"));
        using var http = new HttpClient(handler);
        Assert.Equal(ProviderLifecycleFailure.Disabled, (await new ProviderLifecycleClient(http,
            Options.Create(new ProviderLifecycleOptions())).LookupAsync(Binding)).Failure);
        Assert.Equal(ProviderLifecycleFailure.Malformed, (await new ProviderLifecycleClient(http,
            Options.Create(Settings())).LookupAsync(Binding with { StableSubject = " " })).Failure);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(201, ProviderLifecycleFailure.Unavailable)]
    [InlineData(302, ProviderLifecycleFailure.Unavailable)]
    [InlineData(401, ProviderLifecycleFailure.AuthenticationFailed)]
    [InlineData(403, ProviderLifecycleFailure.AuthenticationFailed)]
    [InlineData(413, ProviderLifecycleFailure.Unavailable)]
    [InlineData(415, ProviderLifecycleFailure.Unavailable)]
    [InlineData(429, ProviderLifecycleFailure.Unavailable)]
    [InlineData(500, ProviderLifecycleFailure.Unavailable)]
    public async Task LookupAsync_ShouldRejectAdmissionAndOtherStatusWithoutRetry(int status, ProviderLifecycleFailure failure)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response("untrusted admission body", status)));
        using var http = new HttpClient(handler);
        var result = await new ProviderLifecycleClient(http, Options.Create(Settings())).LookupAsync(Binding);
        Assert.Equal(failure, result.Failure);
        Assert.Null(result.Response);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("Content-Encoding", "gzip")]
    [InlineData("Content-Encoding", "identity")]
    [InlineData("Content-Type", "application/json; unexpected=x")]
    [InlineData("Content-Type", "application/json; charset=utf-16")]
    public async Task LookupAsync_ShouldRejectTransportHeaders(string name, string value)
    {
        using var handler = new Handler(async (message, token) =>
        {
            var response = Response(await ResponseBody(message, token));
            response.Content.Headers.Remove(name);
            response.Content.Headers.TryAddWithoutValidation(name, value);
            return response;
        });
        using var http = new HttpClient(handler);
        Assert.Equal(ProviderLifecycleFailure.Malformed, (await new ProviderLifecycleClient(http, Options.Create(Settings())).LookupAsync(Binding)).Failure);
    }

    [Theory]
    [InlineData(8192, false, true, 8192)]
    [InlineData(8193, false, false, 8193)]
    [InlineData(10000, false, false, 8193)]
    [InlineData(10000, true, false, 0)]
    public async Task LookupAsync_ShouldBoundStreamingBytesWithOrWithoutLength(int size, bool declared, bool valid, int expectedRead)
    {
        CountingStream? stream = null;
        using var handler = new Handler(async (message, token) =>
        {
            stream = new CountingStream(Encoding.UTF8.GetBytes((await ResponseBody(message, token)).PadRight(size)));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            if (declared) response.Content.Headers.ContentLength = size;
            return response;
        });
        using var http = new HttpClient(handler);
        var result = await new ProviderLifecycleClient(http, Options.Create(Settings())).LookupAsync(Binding);
        Assert.Equal(valid, result.Response is not null);
        Assert.Equal(expectedRead, stream!.BytesRead);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task LookupAsync_ShouldRejectMismatchedDeclaredLengthAndCorrelation()
    {
        foreach (var wrongLength in new[] { false, true })
        {
            using var handler = new Handler(async (message, token) =>
            {
                var body = await ResponseBody(message, token);
                if (!wrongLength) body = body.Replace("subject-001", "different-subject");
                var response = Response(body);
                if (wrongLength) response.Content.Headers.ContentLength = 1;
                return response;
            });
            using var http = new HttpClient(handler);
            Assert.Equal(ProviderLifecycleFailure.Malformed, (await new ProviderLifecycleClient(http, Options.Create(Settings())).LookupAsync(Binding)).Failure);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LookupAsync_ShouldTimeOutDuringHeadersOrCompleteBody(bool bodyPhase)
    {
        using var stream = new WaitingStream();
        using var handler = new Handler(async (_, token) =>
        {
            if (!bodyPhase) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            return response;
        });
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var started = Stopwatch.StartNew();
        var result = await new ProviderLifecycleClient(http, Options.Create(Settings())).LookupAsync(Binding);
        Assert.Equal(ProviderLifecycleFailure.Timeout, result.Failure);
        Assert.InRange(started.Elapsed.TotalSeconds, 0.8, 5);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LookupAsync_ShouldPropagateCallerCancellationDuringHeadersOrBody(bool bodyPhase)
    {
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stream = new WaitingStream(entered);
        using var handler = new Handler(async (_, token) =>
        {
            if (!bodyPhase) { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            return response;
        });
        using var http = new HttpClient(handler);
        var client = new ProviderLifecycleClient(http, Options.Create(Settings()));
        var pending = client.LookupAsync(Binding, cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.LookupAsync(Binding, cancel.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task LookupAsync_ShouldReturnUnavailableOnTransportFailureWithoutRetry()
    {
        using var handler = new Handler((_, _) => throw new HttpRequestException("Sensitive provider details"));
        using var http = new HttpClient(handler);
        var result = await new ProviderLifecycleClient(http, Options.Create(Settings())).LookupAsync(Binding);
        Assert.Equal(ProviderLifecycleFailure.Unavailable, result.Failure);
        Assert.Equal("ProviderLifecycleLookupResult [redacted]", result.ToString());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void PrimaryHandler_ShouldDisableRedirectCookiesDecompressionAndRetainCertificateValidation()
    {
        using var handler = ProviderLifecycleClient.CreatePrimaryHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Null(handler.ServerCertificateCustomValidationCallback);
    }

    [Theory]
    [InlineData("http://example.invalid/path")]
    [InlineData("https://user:dummy@example.invalid/path")]
    [InlineData("https://example.invalid/path?x=1")]
    [InlineData("https://example.invalid/path#fragment")]
    [InlineData("/relative")]
    [InlineData(null)]
    public void Options_ShouldRejectUnsafeOrIncompleteEndpoints(string? endpoint)
    {
        var settings = Settings(); settings.Endpoint = endpoint;
        Assert.True(new ProviderLifecycleOptionsValidator().Validate(null, settings).Failed);
        using var http = new HttpClient();
        Assert.Throws<OptionsValidationException>(() => new ProviderLifecycleClient(http, Options.Create(settings)));
    }

    [Theory]
    [InlineData(0, 60, false)]
    [InlineData(1, 1, true)]
    [InlineData(10, 300, true)]
    [InlineData(11, 60, false)]
    [InlineData(5, 0, false)]
    [InlineData(5, 301, false)]
    public void Options_ShouldValidateDeadlineAndFreshnessBoundaries(int timeout, int maxAge, bool valid)
    {
        var settings = Settings(); settings.TimeoutSeconds = timeout; settings.MaxAgeSeconds = maxAge;
        Assert.Equal(valid, new ProviderLifecycleOptionsValidator().Validate(null, settings).Succeeded);
    }

    [Fact]
    public void Options_ShouldRequireExplicitScopeAuthorityMappingAndSafeSecret()
    {
        var changes = new Action<ProviderLifecycleOptions>[] {
            o => o.RequiredAccounts.Clear(), o => o.RequiredAccounts.Add(o.RequiredAccounts[0]),
            o => o.RequiredAccounts[0].LocalAccountId = Guid.Empty,
            o => o.RequiredAccounts[0].BindingKind = "directorybinding",
            o => o.RequiredAccounts[0].ProviderNamespace = " ",
            o => o.RequiredAccounts[0].SourceAuthority = "",
            o => o.RequiredAccounts[0].MappingVersion = "",
            o => o.RequiredAccounts[0].ExternalLoginProvider = "unapproved",
            o => o.RequiredAccounts[0].BindingKind = ProviderLifecycleRequiredAccount.ExternalLogin,
            o => o.SharedSecret = "", o => o.SharedSecret = "unsafe\r\nheader" };
        foreach (var change in changes)
        {
            var settings = Settings(); change(settings);
            Assert.True(new ProviderLifecycleOptionsValidator().Validate(null, settings).Failed);
        }
        var external = Settings();
        external.RequiredAccounts[0].BindingKind = ProviderLifecycleRequiredAccount.ExternalLogin;
        external.RequiredAccounts[0].ExternalLoginProvider = "ExplicitProvider";
        Assert.True(new ProviderLifecycleOptionsValidator().Validate(null, external).Succeeded);
        var disabled = new ProviderLifecycleOptions();
        Assert.False(disabled.Enabled);
        Assert.Equal(5, disabled.TimeoutSeconds);
        Assert.Equal(60, disabled.MaxAgeSeconds);
        Assert.True(new ProviderLifecycleOptionsValidator().Validate(null, disabled).Succeeded);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token)
        { Calls++; return respond(message, token); }
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { var count = await base.ReadAsync(buffer, cancellationToken); BytesRead += count; return count; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class WaitingStream(TaskCompletionSource? entered = null) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { entered?.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
