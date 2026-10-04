using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Application.Utilities;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>One correlated, bounded lifecycle exchange; no retries, cache, persistence or logging.</summary>
public sealed class ProviderLifecycleClient : IProviderLifecycleClient
{
    private readonly HttpClient _httpClient;
    private readonly bool _enabled;
    private readonly Uri? _endpoint;
    private readonly string? _secret;
    private readonly TimeSpan _timeout;

    public ProviderLifecycleClient(HttpClient httpClient, IOptions<ProviderLifecycleOptions> options)
    {
        var value = options.Value;
        var validation = new ProviderLifecycleOptionsValidator().Validate(null, value);
        if (validation.Failed) throw new OptionsValidationException(ProviderLifecycleOptions.Section,
            typeof(ProviderLifecycleOptions), validation.Failures);
        _httpClient = httpClient;
        _enabled = value.Enabled;
        _endpoint = _enabled ? new Uri(value.Endpoint!, UriKind.Absolute) : null;
        _secret = value.SharedSecret;
        _timeout = TimeSpan.FromSeconds(value.TimeoutSeconds);
    }

    public static HttpClientHandler CreatePrimaryHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None
        // Default platform certificate and hostname verification remain enabled.
    };

    public async Task<ProviderLifecycleLookupResult> LookupAsync(ProviderLifecycleBinding binding,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_enabled) return Failed(ProviderLifecycleFailure.Disabled);
        var request = new ProviderLifecycleRequest { RequestId = Guid.NewGuid().ToString("D"), Binding = binding };
        if (!ProviderLifecycleJson.TrySerializeRequest(request, out var body)) return Failed(ProviderLifecycleFailure.Malformed);
        var started = Stopwatch.GetTimestamp();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        void CheckDeadline()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested || Stopwatch.GetElapsedTime(started) >= _timeout)
                throw new OperationCanceledException(deadline.Token);
        }
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = new ByteArrayContent(body) };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            message.Headers.Add("X-Internal-Secret", _secret);
            message.Headers.Add("X-Provider-Contract", ProviderLifecycleContract.Discriminator);
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            CheckDeadline();
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Failed(ProviderLifecycleFailure.AuthenticationFailed);
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.BadRequest or HttpStatusCode.ServiceUnavailable))
                return Failed(ProviderLifecycleFailure.Unavailable);
            if ((response.Content.Headers.TryGetValues("Content-Encoding", out var encodings) && encodings.Any(v => !string.IsNullOrEmpty(v))) ||
                !ProviderLifecycleJson.IsMediaType(response.Content.Headers.ContentType?.ToString()))
                return Failed(ProviderLifecycleFailure.Malformed);
            var length = response.Content.Headers.ContentLength;
            if (length is > ProviderLifecycleContract.MaximumResponseBytes or < 0) return Failed(ProviderLifecycleFailure.Malformed);
            var buffer = new byte[ProviderLifecycleContract.MaximumResponseBytes + 1];
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                var count = 0;
                while (count < buffer.Length)
                {
                    CheckDeadline();
                    var read = await stream.ReadAsync(buffer.AsMemory(count), deadline.Token);
                    CheckDeadline();
                    if (read == 0) break;
                    count += read;
                }
                if (count > ProviderLifecycleContract.MaximumResponseBytes || (length.HasValue && length.Value != count))
                    return Failed(ProviderLifecycleFailure.Malformed);
                var valid = ProviderLifecycleJson.TryReadResponse((int)response.StatusCode,
                    response.Content.Headers.ContentType?.ToString(), buffer.AsMemory(0, count), request, out var result);
                CheckDeadline();
                return valid ? new() { Response = result } : Failed(ProviderLifecycleFailure.Malformed);
            }
            finally { Array.Clear(buffer); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Failed(ProviderLifecycleFailure.Timeout); }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failed(deadline.IsCancellationRequested || Stopwatch.GetElapsedTime(started) >= _timeout
                ? ProviderLifecycleFailure.Timeout : ProviderLifecycleFailure.Unavailable);
        }
        catch (FormatException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failed(ProviderLifecycleFailure.Malformed);
        }
        finally { Array.Clear(body); }
    }

    private static ProviderLifecycleLookupResult Failed(ProviderLifecycleFailure failure) => new() { Failure = failure };
}
