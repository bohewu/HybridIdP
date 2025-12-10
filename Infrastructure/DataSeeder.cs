using Core.Domain;
using Core.Domain.Entities;
using Infrastructure.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Infrastructure;

public static class DataSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider, bool seedTestUsers = false)
    {
        using var scope = serviceProvider.CreateScope();
        
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        // 1. Migrate Database
        await context.Database.MigrateAsync();

        // 2. Seed Roles
        await RoleSeeder.SeedAsync(roleManager);

        // 3. Seed Scopes & Claims
        await ScopeSeeder.SeedAsync(scopeManager, context);

        // 4. Seed API Resources (Custom Entities & Scopes)
        if (seedTestUsers) 
        {
            await ResourceSeeder.SeedAsync(context, scopeManager);
            
            // Seed Localization (Consent Text)
            await LocalizationSeeder.SeedAsync(context);
        }

        // 5. Seed Users (Admin + Test Users)
        await UserSeeder.SeedAsync(userManager, roleManager, context, seedTestUsers);

        // 6. Seed Clients (M2M, Device, Public, Demo)
        await ClientSeeder.SeedAsync(applicationManager, scopeManager, seedTestUsers);
    }
}
