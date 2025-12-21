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
        await SeedScopeExtensionsAsync(scopeManager, context);
        await SeedUserClaimsAndMappingsAsync(context, scopeManager);
    }

    private static async Task SeedScopeExtensionsAsync(IOpenIddictScopeManager scopeManager, ApplicationDbContext context)
    {
        var extensions = new[]
        {
            new { Name = Scopes.OpenId, DisplayNameKey = "scope.openid.display", DescriptionKey = "scope.openid.description", Icon = "bi bi-person-badge", IsRequired = true, Order = 1, Category = "Identity" },
            new { Name = Scopes.Profile, DisplayNameKey = "scope.profile.display", DescriptionKey = "scope.profile.description", Icon = "bi bi-person", IsRequired = false, Order = 2, Category = "Identity" },
            new { Name = Scopes.Email, DisplayNameKey = "scope.email.display", DescriptionKey = "scope.email.description", Icon = "bi bi-envelope", IsRequired = false, Order = 3, Category = "Identity" },
            new { Name = Scopes.Phone, DisplayNameKey = "scope.phone.display", DescriptionKey = "scope.phone.description", Icon = "bi bi-phone", IsRequired = false, Order = 4, Category = "Identity" },
            new { Name = Scopes.Address, DisplayNameKey = "scope.address.display", DescriptionKey = "scope.address.description", Icon = "bi bi-geo-alt", IsRequired = false, Order = 5, Category = "Identity" },
            new { Name = AuthConstants.Scopes.Roles, DisplayNameKey = "scope.roles.display", DescriptionKey = "scope.roles.description", Icon = "bi bi-shield-check", IsRequired = false, Order = 6, Category = "Identity" }
        };

        foreach (var ext in extensions)
        {
            var scope = await scopeManager.FindByNameAsync(ext.Name);
            if (scope != null)
            {
                var scopeId = await scopeManager.GetIdAsync(scope);
                if (scopeId != null && !await context.ScopeExtensions.AnyAsync(e => e.ScopeId == scopeId))
                {
                    context.ScopeExtensions.Add(new ScopeExtension
                    {
                        ScopeId = scopeId,
                        ConsentDisplayNameKey = ext.DisplayNameKey,
                        ConsentDescriptionKey = ext.DescriptionKey,
                        IconUrl = ext.Icon,
                        IsRequired = ext.IsRequired,
                        DisplayOrder = ext.Order,
                        Category = ext.Category,
                        IsPublic = true
                    });
                }
            }
        }
        await context.SaveChangesAsync();
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
            new() { Name = "address", DisplayName = "Address", Description = "User's mailing address as JSON", ClaimType = Claims.Address, UserPropertyPath = "Address", DataType = "Json", IsStandard = true, IsRequired = false },
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
