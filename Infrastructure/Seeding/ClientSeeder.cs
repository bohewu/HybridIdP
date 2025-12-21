using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Infrastructure.Seeding;

public static class ClientSeeder
{
    public static async Task SeedAsync(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        bool seedTestClients)
    {
        if (seedTestClients)
        {
            // Seed Device Flow test client for manual verification
            await SeedTestDeviceClientAsync(applicationManager);
            
            // Seed M2M test client for system tests
            await SeedTestM2mClientAsync(applicationManager, scopeManager);
            
            // Seed Public Client (new)
            await SeedTestClientPublicAsync(applicationManager);
            
            // Seed Demo Client (new)
            await SeedDemoClientAsync(applicationManager);

            // Seed Admin Client for System Tests
            await SeedTestAdminClientAsync(applicationManager, scopeManager);
        }

        // Note: SeedTestApplicationAsync removed in Phase 3.2
    }

    private static async Task SeedTestDeviceClientAsync(IOpenIddictApplicationManager applicationManager)
    {
        const string clientId = "testclient-device";

        var client = await applicationManager.FindByClientIdAsync(clientId);
        if (client != null)
        {
            await applicationManager.DeleteAsync(client);
        }

        await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "Device Test Client",
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.DeviceAuthorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.DeviceCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
                $"{Permissions.Prefixes.Scope}openid",
                $"{Permissions.Prefixes.Scope}offline_access"
            }
        });
    }

    /// <summary>
    /// Seeds M2M test client for system tests.
    /// ClientId: testclient-m2m
    /// ClientSecret: m2m-test-secret-2024
    /// Supports: client_credentials grant with api:company:read, api:company:write scopes
    /// </summary>
    private static async Task SeedTestM2mClientAsync(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager)
    {
        const string clientId = "testclient-m2m";
        const string clientSecret = "m2m-test-secret-2024";

        // Seed required API scopes first (scopes are now handled by ScopeSeeder/ResourceSeeder, 
        // but checking here ensures they exist if run independently)
        var apiScopes = new[]
        {
            new { Name = "api:company:read", DisplayName = "Read Company Data", Description = "Allows reading company information" },
            new { Name = "api:company:write", DisplayName = "Write Company Data", Description = "Allows modifying company information" }
        };

        foreach (var scope in apiScopes)
        {
            if (await scopeManager.FindByNameAsync(scope.Name) == null)
            {
                await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
                {
                    Name = scope.Name,
                    DisplayName = scope.DisplayName,
                    Description = scope.Description
                });
            }
        }

        // Seed M2M client
        var client = await applicationManager.FindByClientIdAsync(clientId);
        if (client != null)
        {
            await applicationManager.DeleteAsync(client);
        }

        await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            DisplayName = "M2M Test Client",
            ClientType = ClientTypes.Confidential,
            ConsentType = ConsentTypes.Implicit, // No consent for M2M
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Introspection,
                Permissions.Endpoints.Revocation,
                Permissions.Endpoints.PushedAuthorization, // Added PAR support
                Permissions.GrantTypes.ClientCredentials,
                $"{Permissions.Prefixes.Scope}api:company:read",
                $"{Permissions.Prefixes.Scope}api:company:write"
            }
        });
    }

    /// <summary>
    /// Seeds Admin M2M test client for CRUD tests.
    /// ClientId: testclient-admin
    /// ClientSecret: admin-test-secret-2024
    /// Supports: client_credentials grant with full admin API access
    /// </summary>
    private static async Task SeedTestAdminClientAsync(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager)
    {
        const string clientId = "testclient-admin";
        const string clientSecret = "admin-test-secret-2024";

        // Get all application permissions (matching Admin Role)
        var allPermissions = Core.Domain.Constants.Permissions.GetAll();

        // Ensure all scopes exist
        foreach (var permission in allPermissions)
        {
            if (await scopeManager.FindByNameAsync(permission) == null)
            {
                await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
                {
                    Name = permission,
                    DisplayName = permission, // Description can be refined if needed
                    Description = $"Scope for {permission}"
                });
            }
        }

        // Seed Admin M2M client
        var client = await applicationManager.FindByClientIdAsync(clientId);
        if (client != null)
        {
            await applicationManager.DeleteAsync(client);
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            DisplayName = "Admin API Test Client",
            ClientType = ClientTypes.Confidential,
            ConsentType = ConsentTypes.Implicit,
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Introspection,
                Permissions.Endpoints.Revocation,
                Permissions.Endpoints.PushedAuthorization, // Added PAR support
                Permissions.GrantTypes.ClientCredentials,
                // Standard OIDC scopes for UserInfo endpoint testing
                $"{Permissions.Prefixes.Scope}openid",
                Permissions.Scopes.Profile,
                Permissions.Scopes.Email,
                Permissions.Scopes.Roles
            }
        };

        // Add all permission scopes to the client permissions
        foreach (var permission in allPermissions)
        {
            descriptor.Permissions.Add($"{Permissions.Prefixes.Scope}{permission}");
        }

        await applicationManager.CreateAsync(descriptor);
    }


    private static async Task SeedTestClientPublicAsync(IOpenIddictApplicationManager applicationManager)
    {
        const string clientId = "testclient-public";

        var client = await applicationManager.FindByClientIdAsync(clientId);
        if (client != null)
        {
            await applicationManager.DeleteAsync(client);
        }

        await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = "Test Client (Public)",
            RedirectUris = 
            {
                new Uri("https://localhost:7001/signin-oidc"),
                new Uri("https://localhost:7035/signin-oidc")
            },
            PostLogoutRedirectUris = 
            {
                new Uri("https://localhost:7001/signout-callback-oidc")
            },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Introspection,
                Permissions.Endpoints.Revocation,
                Permissions.Endpoints.EndSession,
                Permissions.Endpoints.PushedAuthorization,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.GrantTypes.Password, // For ROPC testing
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
                $"{Permissions.Prefixes.Scope}openid",
                $"{Permissions.Prefixes.Scope}api:company:read",
                $"{Permissions.Prefixes.Scope}api:company:write",
                $"{Permissions.Prefixes.Scope}api:inventory:read"
            }
        });
    }

    private static async Task SeedDemoClientAsync(IOpenIddictApplicationManager applicationManager)
    {
        const string clientId = "demo-client-1";

        var client = await applicationManager.FindByClientIdAsync(clientId);
        if (client != null)
        {
            await applicationManager.DeleteAsync(client);
        }

        await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = "Demo Client 1",
            RedirectUris = 
            {
                new Uri("https://localhost:7001/signin-oidc")
            },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Introspection,
                Permissions.Endpoints.Revocation,
                Permissions.Endpoints.EndSession,
                Permissions.Endpoints.PushedAuthorization,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                $"{Permissions.Prefixes.Scope}openid",
            }
        });
    }
}
