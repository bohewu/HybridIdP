using System.Text.Json;
using OpenIddict.Abstractions;

namespace Infrastructure.Authorization;

public static class AdministrativeClientGrant
{
    // Written only by server-controlled provisioning, never by client/scope DTOs.
    public const string PermissionsProperty = "IdpAdministrationPermissions";
    public const string ApplicationClaim = "idp_admin_application";

    public static HashSet<string> ReadPermissions(IReadOnlyDictionary<string, JsonElement>? properties)
    {
        if (properties == null || !properties.TryGetValue(PermissionsProperty, out var value) || value.ValueKind != JsonValueKind.Array)
            return new(StringComparer.Ordinal);
        var known = Core.Domain.Constants.Permissions.GetAll().ToHashSet(StringComparer.Ordinal);
        return value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString()!).Where(known.Contains).ToHashSet(StringComparer.Ordinal);
    }
}
