using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Infrastructure.Seeding;

public static class ResourceSeeder
{
    public static async Task SeedAsync(ApplicationDbContext context, IOpenIddictScopeManager scopeManager)
    {
        await SeedApiResourcesAsync(context, scopeManager);
    }

    private static async Task SeedApiResourcesAsync(ApplicationDbContext context, IOpenIddictScopeManager scopeManager)
    {
        // 1. Ensure Scopes exist
        var scopes = new[]
        {
            new { Name = "api:company:read", DisplayName = "Read Company Data", Description = "Allows reading company information" },
            new { Name = "api:company:write", DisplayName = "Write Company Data", Description = "Allows modifying company information" },
            new { Name = "api:inventory:read", DisplayName = "Read Inventory Data", Description = "Allows reading inventory information" }
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
                    Resources = { "resource_server" } // Default resource
                });
            }
        }

        // 2. Ensure ApiResource entities exist (Custom Entity)
        var resources = new[]
        {
            new { Name = "company_api", DisplayName = "Company API", Description = "Company management and data API", BaseUrl = "https://api.company.com", Scopes = new[] { "api:company:read", "api:company:write" } },
            new { Name = "inventory_api", DisplayName = "Inventory API", Description = "Inventory management and tracking API", BaseUrl = "https://api.inventory.com", Scopes = new[] { "api:inventory:read" } }
        };

        foreach (var res in resources)
        {
            var existing = await context.ApiResources.FirstOrDefaultAsync(r => r.Name == res.Name);
            if (existing == null)
            {
                var newResource = new ApiResource
                {
                    // Id is auto-increment int, do not assign Guid
                    Name = res.Name,
                    DisplayName = res.DisplayName,
                    Description = res.Description,
                    BaseUrl = res.BaseUrl,
                    CreatedAt = DateTime.UtcNow
                };
                context.ApiResources.Add(newResource);
                await context.SaveChangesAsync();

                // Link scopes
                foreach (var scopeName in res.Scopes)
                {
                    var scope = await scopeManager.FindByNameAsync(scopeName);
                    if (scope != null)
                    {
                        var scopeId = await scopeManager.GetIdAsync(scope);
                        if(scopeId != null) 
                        {
                            context.ApiResourceScopes.Add(new ApiResourceScope
                            {
                                // Id is auto-increment int
                                ApiResourceId = newResource.Id,
                                ScopeId = scopeId
                            });
                        }
                    }
                }
                await context.SaveChangesAsync();
            }
        }
    }
}
