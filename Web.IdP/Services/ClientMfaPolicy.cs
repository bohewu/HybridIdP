using System.Collections.Immutable;
using System.Text.Json;
using Core.Domain.Constants;

namespace Web.IdP.Services;

internal static class ClientMfaPolicy
{
    internal static bool RequiresMfa(ImmutableDictionary<string, JsonElement>? properties)
    {
        if (properties is null ||
            !properties.TryGetValue(AuthConstants.Properties.RequireMfa, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => false
        };
    }
}
