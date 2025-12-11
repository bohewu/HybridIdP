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

        // 7. Seed Default Settings
        await SeedDefaultSettingsAsync(context);
    }

    private static async Task SeedDefaultSettingsAsync(ApplicationDbContext context)
    {
        var key = Core.Domain.Constants.SettingKeys.Security.RegistrationEnabled;
        if (!await context.Settings.AnyAsync(s => s.Key == key))
        {
            context.Settings.Add(new Setting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = "false"  // Default: registration disabled
            });
            await context.SaveChangesAsync();
        }

        // Seed Default Security Policy if not exists
        if (!await context.SecurityPolicies.AnyAsync())
        {
            context.SecurityPolicies.Add(new Core.Domain.Entities.SecurityPolicy
            {
                Id = Guid.NewGuid(),
                MinPasswordLength = 8,
                RequireUppercase = true,
                RequireLowercase = true,
                RequireDigit = true,
                RequireNonAlphanumeric = true,
                PasswordHistoryCount = 5,
                PasswordExpirationDays = 180,
                MinPasswordAgeDays = 1,
                MaxFailedAccessAttempts = 5,
                LockoutDurationMinutes = 15,
                AbnormalLoginHistoryCount = 10,
                BlockAbnormalLogin = false,
                UpdatedUtc = DateTime.UtcNow,
                UpdatedBy = "System"
            });
            await context.SaveChangesAsync();
        }
    }
}
