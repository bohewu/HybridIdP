using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

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
        // Standard OIDC scopes per specification
        var scopes = new[]
        {
            new { Name = Scopes.OpenId, DisplayName = "OpenID", Description = "OpenID Connect identifier" },
            new { Name = Scopes.Email, DisplayName = "Email", Description = "Email address and verification status" },
            new { Name = Scopes.Profile, DisplayName = "Profile", Description = "User profile information" },
            new { Name = Scopes.Phone, DisplayName = "Phone", Description = "Phone number and verification status" },
            new { Name = Scopes.Address, DisplayName = "Address", Description = "Mailing address" },
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
                    Resources = { AuthConstants.Resources.ResourceServer }
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
        // Define standard OIDC claims per specification
        var standardClaims = new List<UserClaim>
        {
            // REQUIRED: Subject identifier (always included)
            new() { Name = "sub", DisplayName = "Subject Identifier", Description = "Unique identifier for the user", ClaimType = Claims.Subject, UserPropertyPath = "Id", DataType = "String", IsStandard = true, IsRequired = true },
            
            // PROFILE SCOPE claims
            new() { Name = "name", DisplayName = "Full Name", Description = "Full name of the user", ClaimType = Claims.Name, UserPropertyPath = "UserName", DataType = "String", IsStandard = true, IsRequired = false },
            new() { Name = "preferred_username", DisplayName = "Preferred Username", Description = "Shorthand name the user prefers to be called", ClaimType = Claims.PreferredUsername, UserPropertyPath = "UserName", DataType = "String", IsStandard = true, IsRequired = false },
            
            // EMAIL SCOPE claims
            new() { Name = "email", DisplayName = "Email Address", Description = "Email address of the user", ClaimType = Claims.Email, UserPropertyPath = "Email", DataType = "String", IsStandard = true, IsRequired = false },
            new() { Name = "email_verified", DisplayName = "Email Verified", Description = "Whether the email address has been verified", ClaimType = Claims.EmailVerified, UserPropertyPath = "EmailConfirmed", DataType = "Boolean", IsStandard = true, IsRequired = false },
            
            // PHONE SCOPE claims
            new() { Name = "phone_number", DisplayName = "Phone Number", Description = "Phone number of the user", ClaimType = Claims.PhoneNumber, UserPropertyPath = "PhoneNumber", DataType = "String", IsStandard = true, IsRequired = false },
            new() { Name = "phone_number_verified", DisplayName = "Phone Verified", Description = "Whether the phone number has been verified", ClaimType = Claims.PhoneNumberVerified, UserPropertyPath = "PhoneNumberConfirmed", DataType = "Boolean", IsStandard = true, IsRequired = false },
            
            // ADDRESS SCOPE claims (single formatted address claim)
            new() { Name = "address", DisplayName = "Address", Description = "User's mailing address as JSON", ClaimType = Claims.Address, UserPropertyPath = null, DataType = "Json", IsStandard = true, IsRequired = false },
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
                [Scopes.OpenId] = new[] { "sub" },
                [Scopes.Profile] = new[] { "name", "preferred_username" },
                [Scopes.Email] = new[] { "email", "email_verified" },
                [Scopes.Phone] = new[] { "phone_number", "phone_number_verified" },
                [Scopes.Address] = new[] { "address" }
            };

            foreach (var (scopeName, claimNames) in scopeMappings)
            {
                var scope = await scopeManager.FindByNameAsync(scopeName);
                if (scope == null) continue;

                var scopeId = await scopeManager.GetIdAsync(scope);
                if (scopeId == null) continue;

                foreach (var claimName in claimNames)
                {
                    if (!claimsByName.TryGetValue(claimName, out var userClaim))
                        continue;

                    var scopeClaim = new ScopeClaim
                    {
                        ScopeId = scopeId.ToString()!,
                        ScopeName = scopeName,
                        UserClaimId = userClaim.Id,
                        AlwaysInclude = claimName == "sub"
                    };

                    await context.Set<ScopeClaim>().AddAsync(scopeClaim);
                }
            }

            await context.SaveChangesAsync();
        }
    }
}
