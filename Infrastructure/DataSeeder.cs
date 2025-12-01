using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Infrastructure;

public static class DataSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        // Ensure database schema is applied using migrations for both providers
        // Using MigrateAsync ensures EF migrations are executed and the schema matches
        // the migrations assemblies configured for the selected provider.
        await context.Database.MigrateAsync();

        // Seed default roles
        await SeedRolesAsync(roleManager);

        // Seed default admin user (Phase 10.6.2: with Person entity)
        await SeedAdminUserAsync(userManager, roleManager, context);

        // Seed OpenIddict scopes
        await SeedScopesAsync(scopeManager);

        // Seed user claims and scope-to-claims mappings (Phase 3.9A)
        await SeedUserClaimsAsync(context, scopeManager);

        // Note: Test client seeding removed in Phase 3.2 - use Admin API to manage clients dynamically
        // await SeedTestApplicationAsync(applicationManager);
    }

    private static async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        string[] roles = { AuthConstants.Roles.Admin, AuthConstants.Roles.User };

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ApplicationRole 
                { 
                    Name = role,
                    IsSystem = true,
                    Description = role == AuthConstants.Roles.Admin 
                        ? "Administrator with full system access" 
                        : "Standard user role"
                });
            }
        }
    }

    private static async Task SeedAdminUserAsync(
        UserManager<ApplicationUser> userManager, 
        RoleManager<ApplicationRole> roleManager,
        ApplicationDbContext context)
    {
        var adminUser = await userManager.FindByEmailAsync(AuthConstants.DefaultAdmin.Email);
        
        if (adminUser == null)
        {
            // Phase 10.6.2: Create Person entity first with default NationalId
            var adminPerson = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = "System",
                LastName = "Administrator",
                NationalId = "A123456789", // Default admin National ID (Taiwan format)
                IdentityDocumentType = IdentityDocumentTypes.NationalId,
                IdentityVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            
            context.Persons.Add(adminPerson);
            await context.SaveChangesAsync();

            adminUser = new ApplicationUser
            {
                UserName = AuthConstants.DefaultAdmin.Email,
                Email = AuthConstants.DefaultAdmin.Email,
                EmailConfirmed = true,
                PersonId = adminPerson.Id // Link admin user to Person entity
            };

            var result = await userManager.CreateAsync(adminUser, AuthConstants.DefaultAdmin.Password);

            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, AuthConstants.Roles.Admin);
                
                // Update Person.CreatedBy after user is created
                adminPerson.CreatedBy = adminUser.Id;
                adminPerson.IdentityVerifiedBy = adminUser.Id;
                await context.SaveChangesAsync();
            }
        }
        else
        {
            // Phase 10.6.2: Update existing admin user's Person if it doesn't exist
            if (adminUser.PersonId == null)
            {
                var existingPerson = await context.Persons
                    .FirstOrDefaultAsync(p => p.NationalId == "A123456789");
                
                if (existingPerson == null)
                {
                    // Create Person for existing admin
                    var adminPerson = new Person
                    {
                        Id = Guid.NewGuid(),
                        FirstName = "System",
                        LastName = "Administrator",
                        NationalId = "A123456789",
                        IdentityDocumentType = IdentityDocumentTypes.NationalId,
                        IdentityVerifiedAt = DateTime.UtcNow,
                        CreatedBy = adminUser.Id,
                        IdentityVerifiedBy = adminUser.Id,
                        CreatedAt = DateTime.UtcNow
                    };
                    
                    context.Persons.Add(adminPerson);
                    adminUser.PersonId = adminPerson.Id;
                    await context.SaveChangesAsync();
                }
                else
                {
                    // Link existing Person to admin user
                    adminUser.PersonId = existingPerson.Id;
                    await context.SaveChangesAsync();
                }
            }
        }
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
    /// Seeds standard OIDC user claims and creates default scope-to-claims mappings.
    /// Phase 3.9A: Claims & Scope-to-Claims Mapping
    /// </summary>
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

    private static async Task SeedTestApplicationAsync(IOpenIddictApplicationManager applicationManager)
    {
        const string clientId = "test_client";

        if (await applicationManager.FindByClientIdAsync(clientId) == null)
        {
            await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientSecret = "test_secret",
                DisplayName = "Test Client Application",
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Authorization,
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.Permissions.ResponseTypes.Code,
                    OpenIddictConstants.Permissions.Scopes.Email,
                    OpenIddictConstants.Permissions.Scopes.Profile,
                    OpenIddictConstants.Permissions.Scopes.Roles,
                    $"{OpenIddictConstants.Permissions.Prefixes.Scope}openid"
                },
                RedirectUris =
                {
                    new Uri("https://localhost:5001/signin-oidc"),
                        new Uri("https://localhost:7001/signin-oidc"),
                        new Uri("https://localhost:7170/signin-oidc")
                },
                PostLogoutRedirectUris =
                {
                    new Uri("https://localhost:5001/signout-callback-oidc"),
                        new Uri("https://localhost:7001/signout-callback-oidc"),
                        new Uri("https://localhost:7170/signout-callback-oidc")
                }
            });
        }
    }
}
