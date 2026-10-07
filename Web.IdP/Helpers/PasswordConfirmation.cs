using Core.Domain;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Web.IdP.Helpers;

public static class PasswordConfirmation
{
    public static async Task<bool> CheckAsync(UserManager<ApplicationUser> users, ApplicationUser user,
        string password, SecurityPolicy policy)
    {
        if (await users.IsLockedOutAsync(user)) return false;
        if (await users.CheckPasswordAsync(user, password)) return true;
        await RecordFailureAsync(users, user, policy);
        return false;
    }

    public static async Task RecordFailureAsync(UserManager<ApplicationUser> users, ApplicationUser user,
        SecurityPolicy policy)
    {
        if (policy.MaxFailedAccessAttempts <= 0) return;
        var originalOptions = users.Options;
        // Identity applies lockout and resets the counter in the same user update.
        // Replace only this scoped manager's options; never mutate shared options.
        users.Options = new IdentityOptions
        {
            ClaimsIdentity = originalOptions.ClaimsIdentity,
            Password = originalOptions.Password,
            User = originalOptions.User,
            SignIn = originalOptions.SignIn,
            Stores = originalOptions.Stores,
            Tokens = originalOptions.Tokens,
            Lockout = new LockoutOptions
            {
                AllowedForNewUsers = originalOptions.Lockout.AllowedForNewUsers,
                MaxFailedAccessAttempts = policy.MaxFailedAccessAttempts,
                DefaultLockoutTimeSpan = TimeSpan.FromMinutes(policy.LockoutDurationMinutes)
            }
        };
        try
        {
            if (!(await users.AccessFailedAsync(user)).Succeeded)
                throw new InvalidOperationException("Password failure could not be persisted.");
        }
        finally { users.Options = originalOptions; }
    }
}
