using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TestClient.Models;
using TestClient.Options;

namespace TestClient.Services;

public sealed class OidcDemoService(HttpClient httpClient, IOptions<OidcDemoOptions> options)
{
    public async Task<ApiDemoViewModel> ProbeInvalidScopeAsync(string redirectUri, CancellationToken cancellationToken)
    {
        try
        {
            var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.AuthorityUrl("connect/par"))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = options.Value.ClientId, ["response_type"] = "code",
                    ["redirect_uri"] = redirectUri,
                    ["scope"] = string.Join(" ", options.Value.Scopes) + " api:invalid:read",
                    ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
                    ["code_challenge_method"] = "S256"
                })
            };
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode != System.Net.HttpStatusCode.BadRequest ||
                response.Content.Headers.ContentType?.MediaType != "application/json")
                return new("Invalid-scope check", false, "The IdP did not return the expected JSON protocol error.", (int)response.StatusCode);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.String || error.GetString() != "invalid_scope")
                return new("Invalid-scope check", false, "The IdP returned a different protocol result.", (int)response.StatusCode);
            return new("Invalid-scope check", true, "The IdP rejected the unsupported scope at /connect/par. No sign-in was attempted.",
                (int)response.StatusCode, ExpectedProtocolError: "invalid_scope");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new("Invalid-scope check", false, "The IdP request timed out."); }
        catch (HttpRequestException)
        { return new("Invalid-scope check", false, "The IdP could not be reached over the configured HTTPS connection."); }
        catch (JsonException)
        { return new("Invalid-scope check", false, "The IdP returned an unreadable protocol response."); }
    }

    public async Task<ApiDemoViewModel> ReadUserInfoAsync(string? accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(accessToken))
            return new("UserInfo", false, "Sign in again to obtain an access token.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, options.Value.AuthorityUrl("connect/userinfo"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new("UserInfo", false, "The IdP rejected the API call. Check the session and token status.", (int)response.StatusCode);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                return new("UserInfo", false, "The IdP returned an unexpected UserInfo response.", (int)response.StatusCode);
            return new("UserInfo", true, "The access token authenticated successfully with the IdP.",
                (int)response.StatusCode, JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new("UserInfo", false, "The IdP request timed out."); }
        catch (HttpRequestException)
        { return new("UserInfo", false, "The IdP could not be reached over the configured HTTPS connection."); }
        catch (JsonException)
        { return new("UserInfo", false, "The IdP returned an unreadable UserInfo response."); }
    }

    public async Task<RefreshDemoResult> RefreshAsync(AuthenticationProperties properties, CancellationToken cancellationToken)
    {
        var previousRefreshToken = properties.GetTokenValue("refresh_token");
        if (string.IsNullOrEmpty(previousRefreshToken))
            return new(false, "No refresh token is available. Sign in with offline_access consent.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.AuthorityUrl("connect/token"))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token", ["client_id"] = options.Value.ClientId,
                    ["refresh_token"] = previousRefreshToken
                })
            };
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(false, "The IdP rejected the refresh. Sign in again if the token expired or was revoked.", (int)response.StatusCode);
            var token = await response.Content.ReadFromJsonAsync<TokenEndpointResponse>(cancellationToken);
            if (token is null || string.IsNullOrEmpty(token.AccessToken) ||
                !string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase) ||
                token.ExpiresIn is not (> 0 and <= int.MaxValue))
                return new(false, "The IdP returned an invalid token response; the saved tokens were retained.", (int)response.StatusCode);
            var nextRefreshToken = string.IsNullOrEmpty(token.RefreshToken) ? previousRefreshToken : token.RefreshToken;
            var tokens = properties.GetTokens().Where(item => item.Name is not ("access_token" or "refresh_token" or "token_type" or "expires_at")).ToList();
            tokens.Add(new AuthenticationToken { Name = "access_token", Value = token.AccessToken });
            tokens.Add(new AuthenticationToken { Name = "refresh_token", Value = nextRefreshToken });
            tokens.Add(new AuthenticationToken { Name = "token_type", Value = "Bearer" });
            tokens.Add(new AuthenticationToken
            {
                Name = "expires_at", Value = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn.Value).ToString("o", CultureInfo.InvariantCulture)
            });
            // A refresh response does not independently validate a replacement identity.
            // Preserve the previously validated ID token and principal.
            properties.StoreTokens(tokens);
            properties.Items.TryGetValue("demo_refresh_count", out var countText);
            var count = int.TryParse(countText, out var current) && current >= 0 ? current : 0;
            properties.Items["demo_refresh_count"] = Math.Min((long)count + 1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
            return new(true, "New tokens were saved to the protected client session.", (int)response.StatusCode,
                !string.Equals(previousRefreshToken, nextRefreshToken, StringComparison.Ordinal));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(false, "The refresh request timed out. Its outcome is uncertain; sign in again before retrying."); }
        catch (HttpRequestException)
        { return new(false, "The refresh connection failed. Its outcome is uncertain; sign in again before retrying."); }
        catch (JsonException)
        { return new(false, "The IdP returned an unreadable token response; sign in again before retrying."); }
    }

    private sealed class TokenEndpointResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        [JsonPropertyName("expires_in")] public long? ExpiresIn { get; set; }
    }
}
