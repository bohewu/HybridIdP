using System.Text.Json;

namespace Core.Application;

/// <summary>Optional per-client restrictions stored in OpenIddict application properties.</summary>
public sealed record ClientProtocolPolicy(
    string PkceMethodPolicy, string TokenEndpointAuthMethod, string ResponseModePolicy)
{
    public const string Inherit = "inherit";
    public const string PkceProperty = "hybrididp:pkce_method_policy";
    public const string TokenAuthProperty = "hybrididp:token_endpoint_auth_method";
    public const string ResponseModeProperty = "hybrididp:response_mode_policy";

    public bool IsValid =>
        PkceMethodPolicy is Inherit or "S256" &&
        TokenEndpointAuthMethod is Inherit or "client_secret_basic" or "client_secret_post" &&
        ResponseModePolicy is Inherit or "query" or "form_post";

    public static ClientProtocolPolicy Read(IEnumerable<KeyValuePair<string, JsonElement>>? properties)
    {
        var values = properties?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
        string ReadValue(string key) => !values.TryGetValue(key, out var value) ? Inherit :
            value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
        return new(ReadValue(PkceProperty), ReadValue(TokenAuthProperty), ReadValue(ResponseModeProperty));
    }

    public static void Apply(
        IDictionary<string, JsonElement> properties, string? pkceMethodPolicy,
        string? tokenEndpointAuthMethod, string? responseModePolicy, bool isConfidential)
    {
        var current = Read(properties);
        var updated = new ClientProtocolPolicy(
            pkceMethodPolicy ?? current.PkceMethodPolicy,
            tokenEndpointAuthMethod ?? current.TokenEndpointAuthMethod,
            responseModePolicy ?? current.ResponseModePolicy);
        if (updated.PkceMethodPolicy is not (Inherit or "S256"))
            throw new ArgumentException("PkceMethodPolicy must be inherit or S256.");
        if (updated.TokenEndpointAuthMethod is not (Inherit or "client_secret_basic" or "client_secret_post"))
            throw new ArgumentException("TokenEndpointAuthMethod must be inherit, client_secret_basic or client_secret_post.");
        if (updated.ResponseModePolicy is not (Inherit or "query" or "form_post"))
            throw new ArgumentException("ResponseModePolicy must be inherit, query or form_post.");
        if (!isConfidential && updated.TokenEndpointAuthMethod != Inherit)
            throw new ArgumentException("TokenEndpointAuthMethod must be inherit for public clients.");

        void Write(string key, string value)
        {
            if (value == Inherit) properties.Remove(key);
            else properties[key] = JsonSerializer.SerializeToElement(value);
        }
        Write(PkceProperty, updated.PkceMethodPolicy);
        Write(TokenAuthProperty, updated.TokenEndpointAuthMethod);
        Write(ResponseModeProperty, updated.ResponseModePolicy);
    }
}
