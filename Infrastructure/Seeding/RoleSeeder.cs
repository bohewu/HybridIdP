using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Seeding;

public static class RoleSeeder
{
    public static async Task SeedAsync(RoleManager<ApplicationRole> roleManager)
    {
        var roles = new[]
        {
            new { Name = AuthConstants.Roles.Admin, Description = "Administrator with full system access", Permissions = string.Empty },
            new { Name = AuthConstants.Roles.User, Description = "Standard user role", Permissions = string.Empty },
            new { Name = AuthConstants.Roles.ApplicationManager, Description = "Application Manager - can manage OAuth clients and scopes they own", Permissions = "clients.read,clients.create,clients.update,clients.delete,scopes.read,scopes.create,scopes.update,scopes.delete" }
        };

        foreach (var role in roles)
        {
            var existingRole = await roleManager.FindByNameAsync(role.Name);
            if (existingRole == null)
            {
                await roleManager.CreateAsync(new ApplicationRole 
                { 
                    Name = role.Name,
                    IsSystem = true,
                    Description = role.Description,
                    Permissions = role.Permissions
                });
            }
            else if (!string.IsNullOrEmpty(role.Permissions) && existingRole.Permissions != role.Permissions)
            {
                // Update existing role's permissions if they differ (e.g., ApplicationManager role)
                existingRole.Permissions = role.Permissions;
                existingRole.Description = role.Description;
                await roleManager.UpdateAsync(existingRole);
            }
        }
    }
}
