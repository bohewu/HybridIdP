using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Utilities;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class RecoveryIdentityVerificationClientTests
{
    private static RecoveryIdentityVerificationRequest Request(string evidence = " test-ID-0001 ") => new()
    {
        RequestId = "73aab4b5-2dfa-471a-8c2b-1d70d7540092",
        ProviderNamespace = "example.provider",
        StableSubject = "synthetic-subject-001",
        Evidence = new() { IdentityIdentifier = evidence }
    };

    private static RecoveryIdentityVerificationOptions Settings() => new()
    {
        Enabled = true, RequireForDirectoryAccounts = true,
        Endpoint = "https://provider.example.invalid/exact/configured/path",
        SharedSecret = Guid.NewGuid().ToString("N"),
        TimeoutSeconds = 1
    };

    private static string Verified => JsonSerializer.Serialize(new
    {
        contractVersion = "1.0", requestId = Request().RequestId, outcome = "Verified",
        binding = new { providerNamespace = Request().ProviderNamespace, stableSubject = Request().StableSubject, scheme = Request().Scheme }
    });

    private static HttpResponseMessage Response(string body, int status = 200, string contentType = "application/json")
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };
        response.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        return response;
    }

    [Fact]
    public async Task VerifyAsync_ShouldSendExactRawEvidenceAndConfiguredEndpoint_WithoutCachingOrResourceRouting()
    {
        var settings = Settings();
        settings.LabelResourceKey = "arbitrary.other.verifier";
        settings.HelpResourceKey = "untrusted.presentation";
        using var handler = new Handler(async (message, token) =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal(settings.Endpoint, message.RequestUri!.AbsoluteUri);
            Assert.Equal(settings.SharedSecret, Assert.Single(message.Headers.GetValues("X-Internal-Secret")));
            Assert.Equal("application/json", message.Content!.Headers.ContentType!.MediaType);
            var bytes = await message.Content.ReadAsByteArrayAsync(token);
            Assert.InRange(bytes.Length, 1, 8192);
            Assert.True(RecoveryIdentityVerificationJson.TryDeserializeRequest(bytes, out var sent));
            Assert.Equal(Request(), sent);
            Assert.DoesNotContain(settings.SharedSecret!, Encoding.UTF8.GetString(bytes));
            return Response(Verified);
        });
        using var http = new HttpClient(handler);
        var client = new RecoveryIdentityVerificationClient(http, Options.Create(settings));
        Assert.Equal(RecoveryIdentityVerificationOutcome.Verified, (await client.VerifyAsync(Request()))!.Outcome);
        Assert.Equal(RecoveryIdentityVerificationOutcome.Verified, (await client.VerifyAsync(Request()))!.Outcome);
        Assert.Equal(2, handler.Calls); // C21: even the same requestId is a new exchange.
    }

    public static IEnumerable<object[]> Envelopes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HybridAuthIdP.sln"))) directory = directory.Parent;
        foreach (var number in new[] { 1, 2, 3, 4, 5, 6, 7, 13, 14, 15, 16, 17, 20, 22, 27 })
        {
            using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory!.FullName,
                "docs/examples/recovery-identity-verification", $"C{number:00}.json")));
            var root = fixture.RootElement;
            yield return new object[] { $"C{number:00}", root.GetProperty("httpStatus").GetInt32(),
                root.GetProperty("contentType").GetString()!, root.GetProperty("responseJson").GetString()!,
                root.GetProperty("consumerAcceptsEnvelope").GetBoolean() };
            if (root.TryGetProperty("responseVariants", out var variants))
                foreach (var variant in variants.EnumerateArray())
                    yield return new object[] { $"C{number:00}-{variant.GetProperty("name").GetString()}", 200,
                        "application/json", variant.GetProperty("json").GetString()!, false };
        }
        yield return new object[] { "wrong-media", 200, "text/html", Verified, false };
        yield return new object[] { "wrong-charset", 200, "application/json; charset=utf-16", Verified, false };
        yield return new object[] { "duplicate", 200, "application/json", Verified.Replace("\"outcome\":", "\"outcome\":\"Denied\",\"outcome\":"), false };
        yield return new object[] { "unknown-member", 200, "application/json", Verified.Insert(1, "\"Email\":\"synthetic@example.invalid\","), false };
        yield return new object[] { "depth", 200, "application/json", "[[[[[[[[[{}]]]]]]]]]", false };
        yield return new object[] { "case", 200, "application/json", Verified.Replace("Verified", "verified"), false };
        yield return new object[] { "wrong-version", 200, "application/json", Verified.Replace("1.0", "2.0"), false };
        yield return new object[] { "partial", 206, "application/json", Verified, false };
    }

    [Theory]
    [MemberData(nameof(Envelopes))]
    public async Task VerifyAsync_ShouldAcceptOnlyFrozenCorrelatedEnvelope(string name, int status, string mediaType, string body, bool accepts)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(body, status, mediaType)));
        using var http = new HttpClient(handler);
        var result = await new RecoveryIdentityVerificationClient(http, Options.Create(Settings())).VerifyAsync(Request());
        var accepted = result is not null;
        Assert.True(accepted == accepts, $"{name}: expected envelope acceptance {accepts}, actual {accepted}.");
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public async Task VerifyAsync_ShouldEnforceUnicodeScalarBoundary(int count, bool valid)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(Verified)));
        using var http = new HttpClient(handler);
        var result = await new RecoveryIdentityVerificationClient(http, Options.Create(Settings()))
            .VerifyAsync(Request(string.Concat(Enumerable.Repeat("\U0001F642", count))));
        Assert.Equal(valid, result is not null);
        Assert.Equal(valid ? 1 : 0, handler.Calls);
    }

    [Fact]
    public async Task VerifyAsync_ShouldRejectInvalidRequestsAndDisabledFeature_BeforeDispatch()
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Must not dispatch"));
        using var http = new HttpClient(handler);
        var client = new RecoveryIdentityVerificationClient(http, Options.Create(Settings()));
        foreach (var request in new[] { Request("\ud800"), Request("  "), Request("\0"), Request() with { Scheme = "browser-verifier" },
            Request() with { RequestId = Guid.Empty.ToString("D") }, Request() with { Evidence = null! } })
            Assert.Null(await client.VerifyAsync(request));
        var disabled = new RecoveryIdentityVerificationClient(http, Options.Create(new RecoveryIdentityVerificationOptions()));
        Assert.Null(await disabled.VerifyAsync(Request()));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifyAsync_ShouldEnforceBodyLimit_WithOrWithoutDeclaredLength(bool declared)
    {
        using var stream = new CountingStream(Encoding.UTF8.GetBytes(Verified.PadRight(10000)));
        using var handler = new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            if (declared) response.Content.Headers.ContentLength = 10000;
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        Assert.Null(await new RecoveryIdentityVerificationClient(http, Options.Create(Settings())).VerifyAsync(Request()));
        Assert.Equal(declared ? 0 : 4097, stream.BytesRead);
    }

    [Theory]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    public async Task VerifyAsync_ShouldAcceptExactResponseLimit(int length, bool accepts)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(Verified.PadRight(length))));
        using var http = new HttpClient(handler);
        var result = await new RecoveryIdentityVerificationClient(http, Options.Create(Settings())).VerifyAsync(Request());
        Assert.Equal(accepts, result is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifyAsync_ShouldPropagateCallerCancellation_DuringHeadersOrBody(bool bodyPhase)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stream = new WaitingStream(entered);
        using var handler = new Handler(async (_, token) =>
        {
            if (!bodyPhase)
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            return response;
        });
        using var http = new HttpClient(handler);
        using var cancel = new CancellationTokenSource();
        var pending = new RecoveryIdentityVerificationClient(http, Options.Create(Settings())).VerifyAsync(Request(), cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task VerifyAsync_ShouldSanitizeTransportExceptions_AndNeverRetry()
    {
        using var handler = new Handler((_, _) => throw new HttpRequestException(Request().Evidence.IdentityIdentifier));
        using var http = new HttpClient(handler);
        Assert.Null(await new RecoveryIdentityVerificationClient(http, Options.Create(Settings())).VerifyAsync(Request()));
        Assert.Equal(1, handler.Calls);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RecoveryIdentityVerificationClient(http, Options.Create(Settings())).VerifyAsync(Request(), cancel.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("redirect")]
    [InlineData("chunked-oversize")]
    [InlineData("truncated")]
    [InlineData("slow-body")]
    public async Task VerifyAsync_ShouldBoundRealLoopbackTransport_AndNotFollowRedirect(string scenario)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var fixtureDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = ServeOnceAsync(listener, scenario, port, fixtureDeadline.Token);
        try
        {
            var settings = Settings();
            settings.Endpoint = $"http://127.0.0.1:{port}/synthetic/verify";
            using var handler = RecoveryIdentityVerificationClient.CreatePrimaryHandler();
            handler.UseProxy = false;
            using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var stopwatch = Stopwatch.StartNew();
            var result = await RecoveryIdentityVerificationClient.CreateForLoopbackTest(http, Options.Create(settings))
                .VerifyAsync(Request(), fixtureDeadline.Token);
            Assert.Equal(scenario == "success", result is not null);
            if (scenario == "slow-body") Assert.InRange(stopwatch.Elapsed.TotalSeconds, 0.5, 3);
            Assert.False(listener.Pending()); // A redirected second request was never issued.
            await server;
        }
        finally
        {
            fixtureDeadline.Cancel();
            listener.Stop();
            try { await server; } catch (OperationCanceledException) { }
        }
    }

    private static async Task ServeOnceAsync(TcpListener listener, string scenario, int port, CancellationToken token)
    {
        using var connection = await listener.AcceptTcpClientAsync(token);
        await using var stream = connection.GetStream();
        // Read the complete synthetic POST so closing the response does not reset unread request data.
        var header = new StringBuilder();
        var one = new byte[1];
        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(one, token) == 0) throw new IOException("Synthetic request ended early.");
            header.Append((char)one[0]);
            if (header.Length > 16384) throw new IOException("Synthetic headers exceeded fixture bound.");
        }
        var contentLength = int.Parse(header.ToString().Split("\r\n")
            .Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1]);
        await stream.ReadExactlyAsync(new byte[contentLength], token);
        var body = Verified;
        var response = scenario switch
        {
            "redirect" => $"HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:{port}/must-not-receive\r\nContent-Length: 0\r\n\r\n",
            "chunked-oversize" => $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\n\r\n1388\r\n{body.PadRight(5000)}\r\n0\r\n\r\n",
            "truncated" => $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length + 10}\r\n\r\n{body}",
            _ => $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n\r\n{(scenario == "slow-body" ? "" : body)}"
        };
        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), token);
        if (scenario == "slow-body") await Task.Delay(TimeSpan.FromSeconds(1.5), token);
    }

    [Theory]
    [InlineData("http://provider.example.invalid/path")]
    [InlineData("http://127.0.0.1/path")]
    [InlineData("/relative")]
    [InlineData("https://user:example@provider.example.invalid/path")]
    [InlineData("https://provider.example.invalid/path?secret=value")]
    [InlineData("https://provider.example.invalid/path#fragment")]
    [InlineData("")]
    public void Options_ShouldRejectUnprotectedEndpoints(string endpoint)
    {
        var settings = Settings();
        settings.Endpoint = endpoint;
        Assert.True(new RecoveryIdentityVerificationOptionsValidator().Validate(null, settings).Failed);
    }

    [Theory]
    [InlineData("secret")]
    [InlineData("header-injection")]
    [InlineData("scheme")]
    [InlineData("timeout-zero")]
    [InlineData("timeout-long")]
    [InlineData("ttl-zero")]
    [InlineData("ttl-long")]
    public void Options_ShouldFailStartupWithSafeDiagnostics(string invalid)
    {
        var settings = Settings();
        switch (invalid)
        {
            case "secret": settings.SharedSecret = ""; break;
            case "header-injection": settings.SharedSecret += "\r\nInjected: value"; break;
            case "scheme": settings.Scheme = "not-supported"; break;
            case "timeout-zero": settings.TimeoutSeconds = 0; break;
            case "timeout-long": settings.TimeoutSeconds = 11; break;
            case "ttl-zero": settings.PrecheckLifetimeMinutes = 0; break;
            case "ttl-long": settings.PrecheckLifetimeMinutes = 6; break;
        }
        var services = new ServiceCollection();
        services.AddSingleton<IValidateOptions<RecoveryIdentityVerificationOptions>, RecoveryIdentityVerificationOptionsValidator>();
        services.AddOptions<RecoveryIdentityVerificationOptions>().Configure(value =>
        {
            value.Enabled = settings.Enabled; value.Endpoint = settings.Endpoint; value.SharedSecret = settings.SharedSecret;
            value.Scheme = settings.Scheme; value.TimeoutSeconds = settings.TimeoutSeconds; value.PrecheckLifetimeMinutes = settings.PrecheckLifetimeMinutes;
        }).ValidateOnStart();
        using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        if (!string.IsNullOrEmpty(settings.SharedSecret)) Assert.DoesNotContain(settings.SharedSecret, error.Message);
        Assert.DoesNotContain(settings.Endpoint!, error.Message);
    }

    [Fact]
    public void HandlerAndTestSeam_ShouldPreserveTlsValidation_AndExcludeArbitraryHttp()
    {
        using var handler = RecoveryIdentityVerificationClient.CreatePrimaryHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.Null(handler.ServerCertificateCustomValidationCallback);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        using var http = new HttpClient(handler);
        var settings = Settings();
        settings.Endpoint = "http://provider.example.invalid/";
        Assert.Throws<OptionsValidationException>(() => RecoveryIdentityVerificationClient.CreateForLoopbackTest(http, Options.Create(settings)));
        Assert.True(new RecoveryIdentityVerificationOptionsValidator().Validate(null, new()).Succeeded);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return respond(request, cancellationToken);
        }
    }

    private class CountingStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }

    private sealed class WaitingStream(TaskCompletionSource entered) : CountingStream([])
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
