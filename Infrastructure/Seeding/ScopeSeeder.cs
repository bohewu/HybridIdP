using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Infrastructure.Seeding;

public static class ScopeSeeder
{
    public static async Task SeedAsync(IOpenIddictScopeManager scopeManager, ApplicationDbContext context)
    {
        await SeedScopesAsync(scopeManager);
        await SeedUserClaimsAsync(context, scopeManager);
    }

    private static async Task SeedScopesAsync(IOpenIddictScopeManager scopeManager)
    {
        var scopes = new[]
        {
            new { Name = AuthConstants.Scopes.OpenId, DisplayName = "OpenID", Description = "OpenID scope" },
            new { Name = AuthConstants.Scopes.Email, DisplayName = "Email", Description = "Email scope" },
            new { Name = AuthConstants.Scopes.Profile, DisplayName = "Profile", Description = "Profile scope" },
            new { Name = AuthConstants.Scopes.Roles, DisplayName = "Roles", Description = "User roles" }
        };

        foreach (var scope in scopes)
        {
            if (await scopeManager.FindByNameAsync(scope.Name) == null)
            {
                await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
                {
                    Name = scope.Name,
                    DisplayName = scope.DisplayName,
                    Description = scope.Description,
                    Resources =
                    {
                        AuthConstants.Resources.ResourceServer
                    }
                });
            }
        }
    }

    private static async Task SeedUserClaimsAsync(ApplicationDbContext context, IOpenIddictScopeManager scopeManager)
    {
        // Check if claims already seeded
        if (await context.Set<UserClaim>().AnyAsync())
        {
            return; // Claims already seeded
        }

        // Define standard OIDC claims
        var claims = new List<UserClaim>
        {
            // REQUIRED: Subject identifier (always included)
            new() { Name = "sub", DisplayName = "Subject Identifier", Description = "Unique identifier for the user", ClaimType = "sub", UserPropertyPath = "Id", DataType = "String", IsStandard = true, IsRequired = true },
            
            // PROFILE SCOPE claims
            new() { Name = "name", DisplayName = "Full Name", Description = "Full name of the user", ClaimType = "name", UserPropertyPath = "UserName", DataType = "String", IsStandard = true, IsRequired = false },
            new() { Name = "preferred_username", DisplayName = "Preferred Username", Description = "Shorthand name the user prefers to be called", ClaimType = "preferred_username", UserPropertyPath = "UserName", DataType = "String", IsStandard = true, IsRequired = false },
            
            // EMAIL SCOPE claims
            new() { Name = "email", DisplayName = "Email Address", Description = "Email address of the user", ClaimType = "email", UserPropertyPath = "Email", DataType = "String", IsStandard = true, IsRequired = false },
            new() { Name = "email_verified", DisplayName = "Email Verified", Description = "Whether the email address has been verified", ClaimType = "email_verified", UserPropertyPath = "EmailConfirmed", DataType = "Boolean", IsStandard = true, IsRequired = false },
            
            // PHONE SCOPE claims
            new() { Name = "phone_number", DisplayName = "Phone Number", Description = "Phone number of the user", ClaimType = "phone_number", UserPropertyPath = "PhoneNumber", DataType = "String", IsStandard = true, IsRequired = false },
            new() { Name = "phone_number_verified", DisplayName = "Phone Verified", Description = "Whether the phone number has been verified", ClaimType = "phone_number_verified", UserPropertyPath = "PhoneNumberConfirmed", DataType = "Boolean", IsStandard = true, IsRequired = false },
        };

        // Add claims to database
        await context.Set<UserClaim>().AddRangeAsync(claims);
        await context.SaveChangesAsync();

        // Create scope-to-claims mappings
        var scopeMappings = new Dictionary<string, string[]>
        {
            ["openid"] = new[] { "sub" }, // OpenID scope always includes subject identifier
            ["profile"] = new[] { "name", "preferred_username" }, // Profile scope includes name-related claims
            ["email"] = new[] { "email", "email_verified" }, // Email scope includes email and verification status
            ["phone"] = new[] { "phone_number", "phone_number_verified" } // Phone scope (if exists)
        };

        foreach (var (scopeName, claimNames) in scopeMappings)
        {
            // Get the scope ID from OpenIddict
            var scope = await scopeManager.FindByNameAsync(scopeName);
            if (scope == null) continue; // Skip if scope doesn't exist

            var scopeId = await scopeManager.GetIdAsync(scope);
            if (scopeId == null) continue;

            // Create ScopeClaim mappings
            foreach (var claimName in claimNames)
            {
                var userClaim = claims.FirstOrDefault(c => c.Name == claimName);
                if (userClaim == null) continue;

                var scopeClaim = new ScopeClaim
                {
                    ScopeId = scopeId.ToString()!,
                    ScopeName = scopeName,
                    UserClaimId = userClaim.Id,
                    AlwaysInclude = claimName == "sub" // Always include "sub" claim
                };

                await context.Set<ScopeClaim>().AddAsync(scopeClaim);
            }
        }

        await context.SaveChangesAsync();
    }
}
