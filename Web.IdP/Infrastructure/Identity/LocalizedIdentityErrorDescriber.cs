using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using Web.IdP;

namespace Web.IdP.Infrastructure.Identity
{
    public class LocalizedIdentityErrorDescriber : IdentityErrorDescriber
    {
        private readonly IStringLocalizer<SharedResource> _localizer;

        public LocalizedIdentityErrorDescriber(IStringLocalizer<SharedResource> localizer)
        {
            _localizer = localizer;
        }

        public override IdentityError DuplicateEmail(string email)
        {
            return new IdentityError
            {
                Code = nameof(DuplicateEmail),
                Description = string.Format(_localizer["DuplicateEmail"], email)
            };
        }

        public override IdentityError PasswordTooShort(int length)
        {
            return new IdentityError
            {
                Code = nameof(PasswordTooShort),
                Description = string.Format(_localizer["PasswordTooShort"], length)
            };
        }

        public override IdentityError InvalidUserName(string? userName)
        {
            return new IdentityError
            {
                Code = nameof(InvalidUserName),
                Description = string.Format(_localizer["InvalidUserName"], userName)
            };
        }

        public override IdentityError DefaultError()
        {
            return new IdentityError
            {
                Code = nameof(DefaultError),
                Description = _localizer["DefaultError"]
            };
        }

        public override IdentityError PasswordRequiresNonAlphanumeric()
        {
            return new IdentityError
            {
                Code = nameof(PasswordRequiresNonAlphanumeric),
                Description = _localizer["PasswordRequiresNonAlphanumeric"]
            };
        }

        public override IdentityError PasswordRequiresDigit()
        {
            return new IdentityError
            {
                Code = nameof(PasswordRequiresDigit),
                Description = _localizer["PasswordRequiresDigit"]
            };
        }

        public override IdentityError PasswordRequiresLower()
        {
            return new IdentityError
            {
                Code = nameof(PasswordRequiresLower),
                Description = _localizer["PasswordRequiresLower"]
            };
        }

        public override IdentityError PasswordRequiresUpper()
        {
            return new IdentityError
            {
                Code = nameof(PasswordRequiresUpper),
                Description = _localizer["PasswordRequiresUpper"]
            };
        }
        
        // Extended Items from IdentityErrorDescriber usually needed
        // Since we copied specific overrides only, we assume the base class handles others or they were not customized in the original file.
        // However, I see more keys in RESX than overrides in the class:
        // UserAccountLockedOut, PasswordExpired, PasswordChangeTooSoon, InvalidLoginAttempt...
        // Wait, IdentityErrorDescriber DOES NOT have "InvalidLoginAttempt" methods. That might be used by App logic, not Identity itself?
        // Let's check the original class again.
        // Step 845 showed 86 lines. It implemented: DuplicateEmail, PasswordTooShort, InvalidUserName, DefaultError, PasswordRequiresNonAlphanumeric, PasswordRequiresDigit, PasswordRequiresLower, PasswordRequiresUpper.
        // Use custom logic usage for others?
        // It seems `LocalizedIdentityErrorDescriber` only overrides passwords/email/user validators.
        // Login logic might use `SharedResource` directly if I update it?
        // But `LocalIdentityErrorDescriber` is OK as is.
    }
}
