using System.Security.Claims;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Identity;

namespace Web.IdP.Infrastructure.Identity;

internal static class ExternalEmailAssurance
{
    public static bool IsVerified(ExternalLoginInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        var isSupportedProvider =
            string.Equals(info.LoginProvider, AuthConstants.Providers.Google, StringComparison.Ordinal) ||
            string.Equals(info.LoginProvider, AuthConstants.Providers.Microsoft, StringComparison.Ordinal);

        return isSupportedProvider &&
               bool.TryParse(
                   info.Principal.FindFirstValue(AuthConstants.Claims.ExternalEmailVerified),
                   out var verified) &&
               verified;
    }
}
