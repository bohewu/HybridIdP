using Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Infrastructure;

public static class DataSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        // Ensure database is created and migrated
        await context.Database.EnsureCreatedAsync();

        // Seed default roles
        await SeedRolesAsync(roleManager);

        // Seed default admin user
        await SeedAdminUserAsync(userManager, roleManager);

        // Seed OpenIddict scopes
        await SeedScopesAsync(scopeManager);

        // Seed OpenIddict test application (will be removed in Phase 3.2)
        await SeedTestApplicationAsync(applicationManager);
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole<Guid>> roleManager)
    {
        string[] roles = { "Admin", "User" };

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }
        }
    }

    private static async Task SeedAdminUserAsync(
        UserManager<ApplicationUser> userManager, 
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        const string adminEmail = "admin@hybridauth.local";
        const string adminPassword = "Admin@123"; // TODO: Change this in production

        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(adminUser, adminPassword);

            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }
    }

    private static async Task SeedScopesAsync(IOpenIddictScopeManager scopeManager)
    {
        var scopes = new[]
        {
            new { Name = "openid", DisplayName = "OpenID", Description = "OpenID scope" },
            new { Name = "email", DisplayName = "Email", Description = "Email scope" },
            new { Name = "profile", DisplayName = "Profile", Description = "Profile scope" },
            new { Name = "roles", DisplayName = "Roles", Description = "User roles" }
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
                        "resource_server"
                    }
                });
            }
        }
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
                    new Uri("https://localhost:7001/signin-oidc")
                },
                PostLogoutRedirectUris =
                {
                    new Uri("https://localhost:5001/signout-callback-oidc"),
                    new Uri("https://localhost:7001/signout-callback-oidc")
                }
            });
        }
    }
}
