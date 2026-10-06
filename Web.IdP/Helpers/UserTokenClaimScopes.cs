using System.Security.Claims;
using OpenIddict.Abstractions;

namespace Web.IdP.Helpers;

public static class UserTokenClaimScopes
{
    public static string? RequiredScope(string type) => type switch
    {
        "name" or "given_name" or "family_name" or "middle_name" or "nickname" or "preferred_username" or
            "profile" or "picture" or "website" or "gender" or "birthdate" or "zoneinfo" or "locale" or "updated_at" or
            ClaimTypes.Name or ClaimTypes.GivenName or ClaimTypes.Surname or ClaimTypes.DateOfBirth => "profile",
        "email" or "email_verified" or ClaimTypes.Email => "email",
        "phone_number" or "phone_number_verified" or ClaimTypes.MobilePhone or ClaimTypes.HomePhone => "phone",
        "address" or ClaimTypes.StreetAddress => "address",
        _ => null
    };

    public static void Apply(ClaimsIdentity identity)
    {
        foreach (var claim in identity.Claims.ToArray())
            if (RequiredScope(claim.Type) is string scope && !identity.HasScope(scope))
                identity.RemoveClaim(claim);
    }
}
