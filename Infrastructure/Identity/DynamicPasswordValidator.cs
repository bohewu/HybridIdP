using Microsoft.AspNetCore.Identity;
using Core.Domain;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Core.Application;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json; // For JSON serialization/deserialization
using System;

namespace HybridIdP.Infrastructure.Identity
{
    public partial class DynamicPasswordValidator : PasswordValidator<ApplicationUser>
    {
        private readonly ILogger<DynamicPasswordValidator> _logger;
        private readonly ISecurityPolicyService _securityPolicyService;

        public DynamicPasswordValidator(ISecurityPolicyService securityPolicyService, ILogger<DynamicPasswordValidator> logger)
        {
            _securityPolicyService = securityPolicyService;
            _logger = logger;
        }

        public override async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
        {
            var errors = new List<IdentityError>();
            if (string.IsNullOrWhiteSpace(password))
            {
                errors.Add(new IdentityError { Code = "PasswordEmpty", Description = "Password cannot be empty." });
                return IdentityResult.Failed(errors.ToArray());
            }

            var policy = await _securityPolicyService.GetCurrentPolicyAsync();

            // Minimum length
            if (password.Length < policy.MinPasswordLength)
            {
                errors.Add(new IdentityError { Code = nameof(IdentityErrorDescriber.PasswordTooShort), Description = $"Passwords must be at least {policy.MinPasswordLength} characters." });
            }

            // Uppercase
            if (policy.RequireUppercase && !password.Any(char.IsUpper))
            {
                errors.Add(new IdentityError { Code = nameof(IdentityErrorDescriber.PasswordRequiresUpper), Description = "Passwords must have at least one uppercase ('A'-'Z')." });
            }

            // Lowercase
            if (policy.RequireLowercase && !password.Any(char.IsLower))
            {
                errors.Add(new IdentityError { Code = nameof(IdentityErrorDescriber.PasswordRequiresLower), Description = "Passwords must have at least one lowercase ('a'-'z')." });
            }

            // Digit
            if (policy.RequireDigit && !password.Any(char.IsDigit))
            {
                errors.Add(new IdentityError { Code = nameof(IdentityErrorDescriber.PasswordRequiresDigit), Description = "Passwords must have at least one digit ('0'-'9')." });
            }

            // Non-alphanumeric
            if (policy.RequireNonAlphanumeric && !password.Any(c => !char.IsLetterOrDigit(c)))
            {
                errors.Add(new IdentityError { Code = nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric), Description = "Passwords must have at least one non alphanumeric character." });
            }

            // Minimum Character Types
            if (policy.MinCharacterTypes > 0)
            {
                int matchCount = 0;
                if (password.Any(char.IsUpper)) matchCount++;
                if (password.Any(char.IsLower)) matchCount++;
                if (password.Any(char.IsDigit)) matchCount++;
                if (password.Any(c => !char.IsLetterOrDigit(c))) matchCount++;

                if (matchCount < policy.MinCharacterTypes)
                {
                    errors.Add(new IdentityError { Code = "PasswordTooSimple", Description = $"Passwords must contain at least {policy.MinCharacterTypes} different character types (Uppercase, Lowercase, Digit, Non-alphanumeric)." });
                }
            }

            // Password History Check
            if (policy.PasswordHistoryCount > 0 && user.PasswordHash != null)
            {
                var passwordHasher = new PasswordHasher<ApplicationUser>();
                List<string> passwordHistory = new List<string>();
                if (!string.IsNullOrEmpty(user.PasswordHistory))
                {
                    try
                    {
                        passwordHistory = JsonSerializer.Deserialize<List<string>>(user.PasswordHistory) ?? new List<string>();
                    }
                    catch (JsonException ex)
                    {
                        LogPasswordHistoryDeserializationFailed(_logger, ex, user.Id.ToString());
                    }
                }

                // Include current password hash in history check
                passwordHistory.Insert(0, user.PasswordHash); // Add current hash to the beginning for checking

                foreach (var historicalHash in passwordHistory.Take(policy.PasswordHistoryCount))
                {
                    if (passwordHasher.VerifyHashedPassword(user, historicalHash, password) == PasswordVerificationResult.Success)
                    {
                        errors.Add(new IdentityError { Code = "PasswordReuse", Description = $"You cannot reuse a password from your last {policy.PasswordHistoryCount} passwords." });
                        break;
                    }
                }
            }

            // Password Expiration Check (only if user has a last password change date)
            if (policy.PasswordExpirationDays > 0 && user.LastPasswordChangeDate.HasValue)
            {
                if (user.LastPasswordChangeDate.Value.AddDays(policy.PasswordExpirationDays) < DateTime.UtcNow)
                {
                    errors.Add(new IdentityError { Code = "PasswordExpired", Description = $"Your password has expired. It must be changed every {policy.PasswordExpirationDays} days." });
                }
            }

            // Minimum Password Age Check
            if (policy.MinPasswordAgeDays > 0 && user.LastPasswordChangeDate.HasValue)
            {
                if (user.LastPasswordChangeDate.Value.AddDays(policy.MinPasswordAgeDays) > DateTime.UtcNow)
                {
                    errors.Add(new IdentityError { Code = "PasswordChangeTooSoon", Description = $"You cannot change your password again so soon. Please wait at least {policy.MinPasswordAgeDays} days." });
                }
            }

            // TODO: Implement common password blacklist (Phase 5.3) - This might require a separate service or configuration.

            return errors.Count > 0 ? IdentityResult.Failed(errors.ToArray()) : IdentityResult.Success;
        }
        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to deserialize password history for user {UserId}")]
        static partial void LogPasswordHistoryDeserializationFailed(ILogger logger, Exception ex, string userId);
    }
}
