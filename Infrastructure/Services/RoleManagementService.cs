using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Infrastructure.Services;

public class RoleManagementService : IRoleManagementService
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDomainEventPublisher _eventPublisher;

    public RoleManagementService(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        IDomainEventPublisher eventPublisher)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _eventPublisher = eventPublisher;
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

    public async Task<PagedRolesDto> GetRolesAsync(int skip, int take, string? search = null, string? sortBy = "name", string? sortDirection = "asc")
    {
        if (skip < 0) skip = 0;
        if (take <= 0) take = 25;

        var rolesQuery = _roleManager.Roles.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            rolesQuery = rolesQuery.Where(r =>
                (r.Name != null && r.Name.ToLower().Contains(s)) ||
                (r.Description != null && r.Description.ToLower().Contains(s))
            );
        }

        // Sorting
        var sortField = (sortBy ?? "name").ToLower();
        var asc = (sortDirection ?? "asc").ToLower() != "desc";
        rolesQuery = (sortField) switch
        {
            "createdat" => asc ? rolesQuery.OrderBy(r => r.CreatedAt) : rolesQuery.OrderByDescending(r => r.CreatedAt),
            _ => asc ? rolesQuery.OrderBy(r => r.Name) : rolesQuery.OrderByDescending(r => r.Name)
        };

        List<ApplicationRole> page;
        int total;
        var providerIsAsync = rolesQuery.Provider is IAsyncQueryProvider;
        if (providerIsAsync)
        {
            total = await rolesQuery.CountAsync();
            page = await rolesQuery.Skip(skip).Take(take).ToListAsync();
        }
        else
        {
            total = rolesQuery.Count();
            page = rolesQuery.Skip(skip).Take(take).ToList();
        }

        var summaries = new List<RoleSummaryDto>(page.Count);
        foreach (var role in page)
        {
            var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
            summaries.Add(new RoleSummaryDto
            {
                Id = role.Id,
                Name = role.Name ?? string.Empty,
                Description = role.Description,
                UserCount = usersInRole.Count,
                Permissions = ParsePermissions(role.Permissions),
                IsSystem = role.IsSystem
            });
        }

        return new PagedRolesDto
        {
            Items = summaries,
            TotalCount = total,
            Skip = skip,
            Take = take
        };
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
        // Validate name uniqueness
        if (string.IsNullOrWhiteSpace(createDto.Name))
        {
            return (false, null, new[] { "Role name is required" });
        }

        var existingByName = await _roleManager.FindByNameAsync(createDto.Name);
        if (existingByName != null)
        {
            return (false, null, new[] { "Role name already exists" });
        }

        // Validate permissions
        var allPermissions = Permissions.GetAll();
        var invalidPermissions = (createDto.Permissions ?? new List<string>())
            .Where(p => !allPermissions.Contains(p))
            .ToList();
        if (invalidPermissions.Any())
        {
            return (false, null, new[] { $"Invalid permissions: {string.Join(", ", invalidPermissions)}" });
        }

        var role = new ApplicationRole
        {
            Name = createDto.Name,
            Description = createDto.Description,
            Permissions = SerializePermissions(createDto.Permissions ?? new List<string>()),
            IsSystem = false,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _roleManager.CreateAsync(role);

        if (!result.Succeeded)
        {
            return (false, null, result.Errors.Select(e => e.Description));
        }

        await _eventPublisher.PublishAsync(new RoleCreatedEvent(role.Id.ToString(), role.Name!));

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

        // Validate name uniqueness if name changed
        if (!string.Equals(role.Name, updateDto.Name, StringComparison.OrdinalIgnoreCase))
        {
            var existingByName = await _roleManager.FindByNameAsync(updateDto.Name);
            if (existingByName != null && existingByName.Id != role.Id)
            {
                return (false, new[] { "Role name already exists" });
            }
        }

        // Validate permissions
        var allPermissions = Permissions.GetAll();
        var invalidPermissions = (updateDto.Permissions ?? new List<string>())
            .Where(p => !allPermissions.Contains(p))
            .ToList();
        if (invalidPermissions.Any())
        {
            return (false, new[] { $"Invalid permissions: {string.Join(", ", invalidPermissions)}" });
        }

        var oldPermissions = ParsePermissions(role.Permissions);
        var newPermissions = updateDto.Permissions ?? new List<string>();

        role.Name = updateDto.Name;
        role.Description = updateDto.Description;
        role.Permissions = SerializePermissions(newPermissions);
        role.ModifiedAt = DateTime.UtcNow;

        var result = await _roleManager.UpdateAsync(role);

        if (result.Succeeded)
        {
            await _eventPublisher.PublishAsync(new RoleUpdatedEvent(role.Id.ToString(), role.Name!, "Role updated"));

            // Check if permissions changed
            if (!oldPermissions.SequenceEqual(newPermissions))
            {
                var changes = $"Permissions changed from [{string.Join(", ", oldPermissions)}] to [{string.Join(", ", newPermissions)}]";
                await _eventPublisher.PublishAsync(new RolePermissionChangedEvent(role.Id.ToString(), role.Name!, changes));
            }
        }

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

        if (result.Succeeded)
        {
            await _eventPublisher.PublishAsync(new RoleDeletedEvent(role.Id.ToString(), role.Name!));
        }

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
