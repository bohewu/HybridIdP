using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Core.Application.DTOs;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class ProviderProofProviderTests
{
    private const string AuthenticatedJson =
        "{\"contractVersion\":\"1.0\",\"outcome\":\"Authenticated\",\"providerNamespace\":\"example.provider\",\"stableSubject\":\"subject-1\",\"canonicalAccount\":\"account\",\"assurance\":{\"stableSubjectAssured\":true,\"canonicalAccountAssured\":true},\"requiredActions\":[]}";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProveAsync_ShouldAcceptInLimitJsonWithoutMediaTypeEnforcement(bool knownLength)
    {
        var content = new UpstreamTestContent(AuthenticatedJson, knownLength)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("text/plain") }
        };
        var handler = RespondingHandler(content, HttpStatusCode.Created);

        var result = await CreateService(handler).ProveAsync(new ProofRequest { AccountName = "account" }, "synthetic-password");

        Assert.Equal(ProofOutcome.Authenticated, result.Outcome);
        Assert.True(result.TryValidate(out _));
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProveAsync_ShouldRejectOversizedAuthenticatedResponse(bool knownLength)
    {
        var content = new UpstreamTestContent(
            AuthenticatedJson + new string(' ', ProviderProofProvider.MaximumResponseBytes), knownLength);
        var handler = RespondingHandler(content, chunked: !knownLength);

        var result = await CreateService(handler).ProveAsync(new ProofRequest { AccountName = "account" }, "synthetic-password");

        Assert.Equal(ProofOutcome.Unavailable, result.Outcome);
        Assert.Null(result.StableSubject);
        Assert.Equal(knownLength ? 0 : ProviderProofProvider.MaximumResponseBytes + 1, content.BytesRead);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task ProveAsync_ShouldRejectRedirectWithoutRetry(HttpStatusCode status)
    {
        var handler = RespondingHandler(new StringContent(AuthenticatedJson), status);

        var result = await CreateService(handler).ProveAsync(new ProofRequest { AccountName = "account" }, "synthetic-password");

        Assert.Equal(ProofOutcome.Unavailable, result.Outcome);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProveAsync_ShouldPreserveBodyTimeoutAndReadFailureOutcomes(bool timeout)
    {
        var content = new UpstreamTestContent("", knownLength: false, failRead: !timeout, waitForCancellation: timeout);
        var handler = RespondingHandler(content);

        var result = await CreateService(handler, TimeSpan.FromMilliseconds(100))
            .ProveAsync(new ProofRequest { AccountName = "account" }, "synthetic-password").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(timeout ? ProofOutcome.Timeout : ProofOutcome.Unavailable, result.Outcome);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ProveAsync_ShouldPropagateBodyCallerCancellationWithoutRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var content = new UpstreamTestContent(AuthenticatedJson, knownLength: false, onRead: cancellation.Cancel);
        var handler = new UpstreamStubHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(handler).ProveAsync(
            new ProofRequest { AccountName = "account" }, "synthetic-password", cancellation.Token));
        Assert.True(content.ReadStarted);
        Assert.Equal(1, handler.CallCount);
    }

    private static ProviderProofProvider CreateService(HttpMessageHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler), Options.Create(new ProviderProofOptions
        {
            Endpoint = "https://provider.example.test/login", SharedSecret = Guid.NewGuid().ToString("N"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5)
        }));

    private static UpstreamStubHandler RespondingHandler(HttpContent content, HttpStatusCode status = HttpStatusCode.OK,
        bool chunked = false) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = content,
            Headers = { Location = new Uri("https://other.example.test/credentials"), TransferEncodingChunked = chunked }
        }));
}

// In-process content that refuses whole-body buffering and records the actual read budget.
internal sealed class UpstreamTestContent(string body, bool knownLength = true,
    bool failRead = false, bool waitForCancellation = false, Action? onRead = null) : HttpContent
{
    private readonly TrackingStream _stream = new(Encoding.UTF8.GetBytes(body), failRead, waitForCancellation, onRead);
    public int BytesRead => _stream.BytesRead;
    public bool ReadCancelled => _stream.ReadCancelled;
    public bool ReadStarted => _stream.ReadStarted;

    protected override bool TryComputeLength(out long length)
    {
        length = _stream.Length;
        return knownLength;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        throw new InvalidOperationException("The response must be read as a bounded stream.");

    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(_stream);
    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(_stream);

    private sealed class TrackingStream(byte[] bytes, bool failRead, bool waitForCancellation, Action? onRead) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public bool ReadCancelled { get; private set; }
        public bool ReadStarted { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted = true;
            onRead?.Invoke();
            if (failRead) throw new IOException("Synthetic body failure.");
            if (waitForCancellation)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) { ReadCancelled = true; throw; }
            }
            var read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }
    }
}

internal sealed class UpstreamStubHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
{
    public int CallCount { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return responseFactory(request, cancellationToken);
    }
}
