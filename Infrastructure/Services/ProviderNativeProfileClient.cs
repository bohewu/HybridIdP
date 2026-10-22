using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Application.Utilities;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class ProviderNativeProfileClient(HttpClient httpClient, IOptions<ProviderNativeProfileOptions> options)
    : IProviderNativeProfileClient
{
    public static HttpClientHandler CreatePrimaryHandler() => new() { AllowAutoRedirect = false, UseCookies = false };

    public async Task<NeutralNativeProfileResult> FetchNeutralAsync(NeutralQueryContext context,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled || context.Request.ContractType != "native-profile" ||
            !NeutralQueryJson.TrySerializeRequest(context.Request, out var body)) throw new ProfileSyncException("configuration_invalid");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(context.Transport.Timeout);
            using var message = new HttpRequestMessage(HttpMethod.Post, context.Transport.Endpoint) { Content = new ByteArrayContent(body) };
            message.Content.Headers.ContentType = new("application/json");
            message.Headers.Add("X-Internal-Secret", context.Transport.SharedSecret);
            message.Headers.Add("X-Provider-Contract", "native-profile/2.0");
            message.Headers.CacheControl = new() { NoStore = true };
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.Content.Headers.ContentEncoding.Count != 0) throw new ProfileSyncException("source_invalid_response");
            using var bounded = await BoundedUpstreamResponse.ReadAsync(response.Content, 65536, timeout.Token);
            var bytes = await bounded.ReadAsByteArrayAsync(timeout.Token);
            if (!NeutralQueryJson.TryReadResponse((int)response.StatusCode, response.Content.Headers.ContentType?.ToString(),
                bytes, context.Request, out var result)) throw new ProfileSyncException("source_invalid_response");
            if (result!.QueryOutcome != "Found") throw new ProfileSyncException(result.QueryOutcome == "NotFound" ? "source_not_found" : "source_unavailable");
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            json.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            return result.Result!.Value.Deserialize<NeutralNativeProfileResult>(json)!;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new ProfileSyncException("source_unavailable"); }
        catch (Exception ex) when (ex is HttpRequestException or IOException) { throw new ProfileSyncException("source_unavailable"); }
        catch (JsonException) { throw new ProfileSyncException("source_invalid_response"); }
        finally { Array.Clear(body); }
    }

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
