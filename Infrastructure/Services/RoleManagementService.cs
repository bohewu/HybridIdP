using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

public class RoleManagementService : IRoleManagementService
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public RoleManagementService(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager)
    {
        _roleManager = roleManager;
        _userManager = userManager;
    }

    public async Task<List<RoleSummaryDto>> GetRolesAsync()
    {
        var roles = await _roleManager.Roles.ToListAsync();
        var roleSummaries = new List<RoleSummaryDto>();

        foreach (var role in roles)
        {
            var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
            
            roleSummaries.Add(new RoleSummaryDto
            {
                Id = role.Id,
                Name = role.Name ?? string.Empty,
                Description = role.Description,
                UserCount = usersInRole.Count,
                Permissions = ParsePermissions(role.Permissions),
                IsSystem = role.IsSystem
            });
        }

        return roleSummaries;
    }

    public async Task<RoleDetailDto?> GetRoleByIdAsync(Guid roleId)
    {
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        if (role == null)
            return null;

        var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
        
        var userSummaries = usersInRole.Select(u => new UserSummaryDto
        {
            Id = u.Id,
            Email = u.Email ?? string.Empty,
            UserName = u.UserName,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Department = u.Department,
            JobTitle = u.JobTitle,
            IsActive = u.IsActive,
            EmailConfirmed = u.EmailConfirmed,
            LastLoginDate = u.LastLoginDate,
            CreatedAt = u.CreatedAt,
            Roles = new List<string> { role.Name! }
        }).ToList();

        return new RoleDetailDto
        {
            Id = role.Id,
            Name = role.Name ?? string.Empty,
            NormalizedName = role.NormalizedName,
            Description = role.Description,
            Permissions = ParsePermissions(role.Permissions),
            IsSystem = role.IsSystem,
            UserCount = usersInRole.Count,
            Users = userSummaries
        };
    }

    public async Task<(bool Success, Guid? RoleId, IEnumerable<string> Errors)> CreateRoleAsync(CreateRoleDto createDto)
    {
        var role = new ApplicationRole
        {
            Name = createDto.Name,
            Description = createDto.Description,
            Permissions = SerializePermissions(createDto.Permissions),
            IsSystem = false,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _roleManager.CreateAsync(role);

        if (!result.Succeeded)
        {
            return (false, null, result.Errors.Select(e => e.Description));
        }

        return (true, role.Id, Array.Empty<string>());
    }

    public async Task<(bool Success, IEnumerable<string> Errors)> UpdateRoleAsync(Guid roleId, UpdateRoleDto updateDto)
    {
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        if (role == null)
        {
            return (false, new[] { "Role not found" });
        }

        // Prevent modification of system role names (but allow description/permissions updates)
        if (role.IsSystem && role.Name != updateDto.Name)
        {
            return (false, new[] { "Cannot rename system roles" });
        }

        role.Name = updateDto.Name;
        role.Description = updateDto.Description;
        role.Permissions = SerializePermissions(updateDto.Permissions);
        role.ModifiedAt = DateTime.UtcNow;

        var result = await _roleManager.UpdateAsync(role);

        return result.Succeeded
            ? (true, Array.Empty<string>())
            : (false, result.Errors.Select(e => e.Description));
    }

    public async Task<(bool Success, IEnumerable<string> Errors)> DeleteRoleAsync(Guid roleId)
    {
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        if (role == null)
        {
            return (false, new[] { "Role not found" });
        }

        if (role.IsSystem)
        {
            return (false, new[] { "Cannot delete system roles" });
        }

        // Check if role has users
        var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
        if (usersInRole.Count > 0)
        {
            return (false, new[] { $"Cannot delete role with {usersInRole.Count} assigned user(s). Remove users from role first." });
        }

        var result = await _roleManager.DeleteAsync(role);

        return result.Succeeded
            ? (true, Array.Empty<string>())
            : (false, result.Errors.Select(e => e.Description));
    }

    public Task<List<string>> GetAvailablePermissionsAsync()
    {
        return Task.FromResult(Permissions.GetAll());
    }

    private List<string> ParsePermissions(string? permissionsString)
    {
        if (string.IsNullOrWhiteSpace(permissionsString))
            return new List<string>();

        return permissionsString.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .ToList();
    }

    private string? SerializePermissions(List<string> permissions)
    {
        if (permissions == null || !permissions.Any())
            return null;

        return string.Join(",", permissions);
    }
}
