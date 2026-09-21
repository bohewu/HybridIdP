using Core.Domain;
using Core.Domain.Entities;

namespace Core.Application;

public static class LocalPasswordSignInPolicy
{
    public static bool RequiresChange(
        ApplicationUser user,
        SecurityPolicy policy,
        DateTime utcNow) =>
        user.RequiresPasswordChange ||
        (policy.PasswordExpirationDays > 0 &&
         user.LastPasswordChangeDate.HasValue &&
         user.LastPasswordChangeDate.Value.AddDays(policy.PasswordExpirationDays) < utcNow);
}
