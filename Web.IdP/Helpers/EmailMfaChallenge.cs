using Core.Domain;

namespace Web.IdP.Helpers;

public static class EmailMfaChallenge
{
    public static bool HasPendingCode(ApplicationUser user) =>
        !string.IsNullOrEmpty(user.EmailMfaCode) && user.EmailMfaCodeExpiry > DateTime.UtcNow &&
        user.EmailMfaVerificationAttempts < 5;
}
