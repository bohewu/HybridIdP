using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Application.Utilities;
using Infrastructure.Options;

namespace Infrastructure.Services;

public sealed class ProviderProfileClient(HttpClient httpClient)
{
    public static HttpClientHandler CreatePrimaryHandler() => new() { AllowAutoRedirect = false, UseCookies = false };

    public async Task<NeutralProfileResult?> FetchNeutralAsync(NeutralQueryContext context, CancellationToken cancellationToken)
    {
        if (context.Request.ContractType != "profile" || !NeutralQueryJson.TrySerializeRequest(context.Request, out var body)) return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(context.Transport.Timeout);
            using var message = new HttpRequestMessage(HttpMethod.Post, context.Transport.Endpoint) { Content = new ByteArrayContent(body) };
            message.Content.Headers.ContentType = new("application/json");
            message.Headers.Add("X-Internal-Secret", context.Transport.SharedSecret);
            message.Headers.Add("X-Provider-Contract", "profile/2.0");
            message.Headers.CacheControl = new() { NoStore = true };
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.Content.Headers.ContentEncoding.Count != 0) return null;
            using var bounded = await BoundedUpstreamResponse.ReadAsync(response.Content, 65536, timeout.Token);
            var bytes = await bounded.ReadAsByteArrayAsync(timeout.Token);
            if (!NeutralQueryJson.TryReadResponse((int)response.StatusCode, response.Content.Headers.ContentType?.ToString(),
                bytes, context.Request, out var result) || result?.QueryOutcome != "Found") return null;
            return new(result.Result!.Value.GetProperty("extraProperties").EnumerateObject().ToDictionary(
                x => x.Name, x => ProviderProfileContract.NormalizeValue(x.Value), StringComparer.Ordinal));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or IOException) { return null; }
        finally { Array.Clear(body); }
    }

    public async Task<ProviderProfileResult?> FetchAsync(ProviderProfileRequest request,
        ProviderProfileSourceOptions source, CancellationToken cancellationToken)
    {
        if (!request.IsValid()) return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(source.Timeout);
            using var message = new HttpRequestMessage(HttpMethod.Post, source.Endpoint)
            {
                Content = JsonContent.Create(request)
            };
            message.Headers.TryAddWithoutValidation("X-Internal-Secret", source.SharedSecret);
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK ||
                response.Content.Headers.ContentType?.MediaType != "application/json") return null;
            using var content = await BoundedUpstreamResponse.ReadAsync(response.Content, 64 * 1024, timeout.Token);
            using var document = await JsonDocument.ParseAsync(await content.ReadAsStreamAsync(timeout.Token),
                new JsonDocumentOptions { MaxDepth = 8 }, timeout.Token);
            if (!HasUniqueMembers(document.RootElement)) return null;
            var result = document.RootElement.Deserialize<ProviderProfileResult>(JsonSerializerOptions.Web);
            return result?.IsValidFor(request) == true ? result with
            {
                ExtraProperties = result.ExtraProperties.ToDictionary(pair => pair.Key,
                    pair => ProviderProfileContract.NormalizeValue(pair.Value), StringComparer.Ordinal)
            } : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            return null;
        }
    }

    private static bool HasUniqueMembers(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in element.EnumerateObject())
        {
            if (!names.Add(member.Name)) return false;
            if (member.Value.ValueKind == JsonValueKind.Object && !HasUniqueMembers(member.Value)) return false;
        }
        return true;
    }
}
