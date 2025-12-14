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
        await SeedUserClaimsAndMappingsAsync(context, scopeManager);
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

    /// <summary>
    /// Seeds UserClaims and ScopeClaims mappings separately.
    /// This fixes the bug where ScopeClaims wouldn't be created if UserClaims already existed.
    /// </summary>
    private static async Task SeedUserClaimsAndMappingsAsync(ApplicationDbContext context, IOpenIddictScopeManager scopeManager)
    {
        // Define standard OIDC claims
        var standardClaims = new List<UserClaim>
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

        // Step 1: Seed UserClaims if not exist
        var hasUserClaims = await context.Set<UserClaim>().AnyAsync();
        if (!hasUserClaims)
        {
            await context.Set<UserClaim>().AddRangeAsync(standardClaims);
            await context.SaveChangesAsync();
        }

        // Step 2: Seed ScopeClaims mappings if not exist (separate check!)
        var hasScopeClaims = await context.Set<ScopeClaim>().AnyAsync();
        if (!hasScopeClaims)
        {
            // Re-fetch existing claims from DB (may have been just seeded or already existed)
            var existingClaims = await context.Set<UserClaim>().ToListAsync();
            var claimsByName = existingClaims.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

            // Define scope-to-claims mappings per OIDC specification
            var scopeMappings = new Dictionary<string, string[]>
            {
                ["openid"] = new[] { "sub" }, // OpenID scope always includes subject identifier
                ["profile"] = new[] { "name", "preferred_username" }, // Profile scope includes name-related claims
                ["email"] = new[] { "email", "email_verified" }, // Email scope includes email and verification status
                ["phone"] = new[] { "phone_number", "phone_number_verified" } // Phone scope
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
                    if (!claimsByName.TryGetValue(claimName, out var userClaim))
                        continue;

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
}
