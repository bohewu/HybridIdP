using System.Net;
using System.Net.Http.Headers;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Application.Utilities;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>One bounded RIV exchange. No persistence, logging, retry or recovery authority.</summary>
public sealed class RecoveryIdentityVerificationClient : IRecoveryIdentityVerificationClient
{
    private readonly HttpClient _httpClient;
    private readonly bool _enabled;
    private readonly Uri? _endpoint;
    private readonly string? _sharedSecret;
    private readonly TimeSpan _timeout;

    public RecoveryIdentityVerificationClient(HttpClient httpClient, IOptions<RecoveryIdentityVerificationOptions> options)
        : this(httpClient, options, isolatedLoopbackTest: false) { }

    private RecoveryIdentityVerificationClient(HttpClient httpClient,
        IOptions<RecoveryIdentityVerificationOptions> options, bool isolatedLoopbackTest)
    {
        var value = options.Value;
        var validation = RecoveryIdentityVerificationOptionsValidator.ValidateCore(value, isolatedLoopbackTest);
        if (validation.Failed)
            throw new OptionsValidationException(RecoveryIdentityVerificationOptions.Section,
                typeof(RecoveryIdentityVerificationOptions), validation.Failures);
        _httpClient = httpClient;
        _enabled = value.Enabled;
        _endpoint = _enabled && !string.IsNullOrWhiteSpace(value.Endpoint) ? new Uri(value.Endpoint, UriKind.Absolute) : null;
        _sharedSecret = value.SharedSecret;
        _timeout = TimeSpan.FromSeconds(value.TimeoutSeconds);
    }

    // Accessible only to the existing friend test assembly. Never bound from deployment settings.
    internal static RecoveryIdentityVerificationClient CreateForLoopbackTest(HttpClient httpClient,
        IOptions<RecoveryIdentityVerificationOptions> options) => new(httpClient, options, isolatedLoopbackTest: true);

    public static HttpClientHandler CreatePrimaryHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None
        // Normal platform certificate validation remains in force.
    };

    public async Task<RecoveryIdentityVerificationResponse?> VerifyAsync(
        RecoveryIdentityVerificationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_enabled || _endpoint is null) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        if (!RecoveryIdentityVerificationJson.TrySerializeRequest(request, out var body)) return null;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new ByteArrayContent(body)
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            message.Headers.Add("X-Internal-Secret", _sharedSecret);
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.BadRequest or HttpStatusCode.ServiceUnavailable) ||
                response.Content.Headers.ContentEncoding.Count != 0)
                return null;

            var length = response.Content.Headers.ContentLength;
            if (length is > RecoveryIdentityVerificationContract.MaximumResponseBytes) return null;
            // One extra byte detects overflow even without Content-Length (including chunked bodies).
            var buffer = new byte[RecoveryIdentityVerificationContract.MaximumResponseBytes + 1];
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), deadline.Token);
                if (read == 0) break;
                count += read;
            }
            deadline.Token.ThrowIfCancellationRequested();
            if (count > RecoveryIdentityVerificationContract.MaximumResponseBytes ||
                (length.HasValue && length.Value != count)) return null;
            var valid = RecoveryIdentityVerificationJson.TryReadResponse((int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(), buffer.AsMemory(0, count), request, out var result);
            deadline.Token.ThrowIfCancellationRequested();
            return valid ? result : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or IOException)
        {
            // Even exception messages may contain provider data. Do not log them.
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        finally
        {
            Array.Clear(body);
        }
    }

    public async Task<NeutralQueryResponse?> VerifyAsync(NeutralQueryContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Transport is null || context.DirectoryTransport is not null || !_enabled || context.Request.ContractType != "recovery-identity-verification" ||
            !NeutralQueryJson.TrySerializeRequest(context.Request, out var body)) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(context.Transport.Timeout);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, context.Transport.Endpoint)
            {
                Content = new ByteArrayContent(body)
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            message.Headers.Add("X-Internal-Secret", context.Transport.SharedSecret);
            message.Headers.Add("X-Provider-Contract", "recovery-identity-verification/2.0");
            message.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.Content.Headers.ContentEncoding.Count != 0 ||
                response.Content.Headers.ContentLength > RecoveryIdentityVerificationContract.MaximumResponseBytes) return null;
            var buffer = new byte[RecoveryIdentityVerificationContract.MaximumResponseBytes + 1];
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), deadline.Token);
                if (read == 0) break;
                count += read;
            }
            deadline.Token.ThrowIfCancellationRequested();
            if (count > RecoveryIdentityVerificationContract.MaximumResponseBytes ||
                response.Content.Headers.ContentLength is { } length && length != count) return null;
            return NeutralQueryJson.TryReadResponse((int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(), buffer.AsMemory(0, count), context.Request, out var result)
                ? result : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        finally { Array.Clear(body); }
    }
}
