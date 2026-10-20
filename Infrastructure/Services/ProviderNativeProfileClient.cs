using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Ports;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class ProviderNativeProfileClient(HttpClient httpClient, IOptions<ProviderNativeProfileOptions> options)
    : IProviderNativeProfileClient
{
    public static HttpClientHandler CreatePrimaryHandler() => new() { AllowAutoRedirect = false, UseCookies = false };

    public async Task<ProviderNativeProfileResponse> FetchAsync(ProviderNativeProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var configuration = options.Value;
        if (!configuration.Enabled || !configuration.TryGetEndpoint(out var endpoint) ||
            request.ContractVersion != ProviderNativeProfileContract.Version ||
            request.ProviderNamespace != configuration.TrustedProviderNamespace ||
            !ProviderNativeProfileContract.IsValidIdentity(request.ProviderNamespace, request.StableSubject))
            throw new ProfileSyncException("configuration_invalid");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(configuration.Timeout);
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(request) };
            message.Headers.Add("X-Internal-Secret", configuration.SharedSecret);
            message.Headers.CacheControl = new() { NoStore = true };
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) throw new ProfileSyncException("source_not_found");
            if (response.StatusCode != HttpStatusCode.OK) throw new ProfileSyncException("source_unavailable");
            var media = response.Content.Headers.ContentType;
            if (!string.Equals(media?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
                media.CharSet is { } charset && !string.Equals(charset.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase))
                throw new ProfileSyncException("source_invalid_response");
            using var bounded = await BoundedUpstreamResponse.ReadAsync(response.Content,
                ProviderNativeProfileContract.MaximumResponseBytes, timeout.Token);
            var bytes = await bounded.ReadAsByteArrayAsync(timeout.Token);
            return ProviderNativeProfileContract.Parse(bytes, request);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new ProfileSyncException("source_unavailable"); }
        catch (HttpRequestException) { throw new ProfileSyncException("source_unavailable"); }
        catch (JsonException) { throw new ProfileSyncException("source_invalid_response"); }
        catch (DecoderFallbackException) { throw new ProfileSyncException("source_invalid_response"); }
        catch (IOException) { throw new ProfileSyncException("source_unavailable"); }
    }
}
