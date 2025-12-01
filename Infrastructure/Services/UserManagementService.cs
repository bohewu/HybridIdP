using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

public class UserManagementService : IUserManagementService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly IApplicationDbContext _context;

    public UserManagementService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IDomainEventPublisher eventPublisher,
        IApplicationDbContext context)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _eventPublisher = eventPublisher;
        _context = context;
    }

    public async Task<PagedUsersDto> GetUsersAsync(
        int skip = 0,
        int take = 25,
        string? search = null,
        string? role = null,
        bool? isActive = null,
        string? sortBy = "email",
        string? sortDirection = "asc")
    {
        var query = _userManager.Users.Include(u => u.Person).AsQueryable();

        // Filter out soft-deleted users
        query = query.Where(u => !u.IsDeleted);

        // Apply filters
        if (isActive.HasValue)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(searchLower)) ||
                (u.UserName != null && u.UserName.ToLower().Contains(searchLower)) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(searchLower)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(searchLower)));
        }

        // Apply sorting
        query = sortBy?.ToLower() switch
        {
            "username" => sortDirection?.ToLower() == "desc"
                ? query.OrderByDescending(u => u.UserName)
                : query.OrderBy(u => u.UserName),
            "firstname" => sortDirection?.ToLower() == "desc"
                ? query.OrderByDescending(u => u.FirstName)
                : query.OrderBy(u => u.FirstName),
            "lastname" => sortDirection?.ToLower() == "desc"
                ? query.OrderByDescending(u => u.LastName)
                : query.OrderBy(u => u.LastName),
            "createdat" => sortDirection?.ToLower() == "desc"
                ? query.OrderByDescending(u => u.CreatedAt)
                : query.OrderBy(u => u.CreatedAt),
            _ => sortDirection?.ToLower() == "desc"
                ? query.OrderByDescending(u => u.Email)
                : query.OrderBy(u => u.Email)
        };

        // Use synchronous enumeration to support both EF Core and in-memory LINQ providers in tests.
        // (Async 'CountAsync/ToListAsync' requires an EF Core IAsyncQueryProvider; mocked IQueryable lacks it.)
        var totalCount = query.Count();
        var users = query
            .Skip(skip)
            .Take(take)
            .ToList();

        var userSummaries = new List<UserSummaryDto>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);

            // Apply role filter if specified
            if (!string.IsNullOrWhiteSpace(role) && !roles.Contains(role))
            {
                totalCount--; // Adjust count since we're filtering after query
                continue;
            }

            userSummaries.Add(new UserSummaryDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                UserName = user.UserName,
                FirstName = user.Person?.FirstName ?? user.FirstName,
                LastName = user.Person?.LastName ?? user.LastName,
                Department = user.Person?.Department ?? user.Department,
                JobTitle = user.Person?.JobTitle ?? user.JobTitle,
                IsActive = user.IsActive,
                EmailConfirmed = user.EmailConfirmed,
                LastLoginDate = user.LastLoginDate,
                CreatedAt = user.CreatedAt,
                Roles = roles.ToList()
            });
        }

        return new PagedUsersDto
        {
            Items = userSummaries,
            TotalCount = totalCount,
            Skip = skip,
            Take = take
        };
    }

    public async Task<UserDetailDto?> GetUserByIdAsync(Guid userId)
    {
        var user = await _userManager.Users
            .Include(u => u.Person)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return null;

        var roles = await _userManager.GetRolesAsync(user);

        return new UserDetailDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            UserName = user.UserName,
            FirstName = user.Person?.FirstName ?? user.FirstName,
            LastName = user.Person?.LastName ?? user.LastName,
            MiddleName = user.Person?.MiddleName ?? user.MiddleName,
            Nickname = user.Person?.Nickname ?? user.Nickname,
            PhoneNumber = user.PhoneNumber,
            Department = user.Person?.Department ?? user.Department,
            JobTitle = user.Person?.JobTitle ?? user.JobTitle,
            ProfileUrl = user.Person?.ProfileUrl ?? user.ProfileUrl,
            PictureUrl = user.Person?.PictureUrl ?? user.PictureUrl,
            Website = user.Person?.Website ?? user.Website,
            Address = user.Person?.Address ?? user.Address,
            Birthdate = user.Person?.Birthdate ?? user.Birthdate,
            Gender = user.Person?.Gender ?? user.Gender,
            TimeZone = user.Person?.TimeZone ?? user.TimeZone,
            Locale = user.Person?.Locale ?? user.Locale,
            EmployeeId = user.Person?.EmployeeId ?? user.EmployeeId,
            PersonId = user.PersonId,  // Phase 10: Expose Person link
            IsActive = user.IsActive,
            EmailConfirmed = user.EmailConfirmed,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed,
            TwoFactorEnabled = user.TwoFactorEnabled,
            LastLoginDate = user.LastLoginDate,
            CreatedAt = user.CreatedAt,
            ModifiedAt = user.ModifiedAt,
            Roles = roles.ToList()
        };
    }

    public async Task<(bool Success, Guid? UserId, IEnumerable<string> Errors)> CreateUserAsync(
        CreateUserDto createDto,
        Guid? createdBy = null)
    {
        // Phase 10.4: Create Person first, then ApplicationUser
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = createDto.FirstName,
            LastName = createDto.LastName,
            Department = createDto.Department,
            JobTitle = createDto.JobTitle,
            EmployeeId = createDto.EmployeeId,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        _context.Persons.Add(person);
        await _context.SaveChangesAsync(CancellationToken.None);

        var user = new ApplicationUser
        {
            Email = createDto.Email,
            UserName = createDto.UserName,
            FirstName = createDto.FirstName,  // Keep for backward compatibility
            LastName = createDto.LastName,
            PhoneNumber = createDto.PhoneNumber,
            Department = createDto.Department,
            JobTitle = createDto.JobTitle,
            EmployeeId = createDto.EmployeeId,
            PersonId = person.Id,  // Link to Person
            IsActive = true, // Default to active for new users
            EmailConfirmed = true, // Admin-created users are pre-confirmed and can login immediately
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, createDto.Password);

        if (!result.Succeeded)
        {
            return (false, null, result.Errors.Select(e => e.Description));
        }

        // Assign roles
        if (createDto.Roles.Any())
        {
            var roleResult = await _userManager.AddToRolesAsync(user, createDto.Roles);
            if (!roleResult.Succeeded)
            {
                // User created but role assignment failed
                return (false, user.Id, roleResult.Errors.Select(e => e.Description));
            }
        }

        // Publish domain event
        await _eventPublisher.PublishAsync(new UserCreatedEvent(user.Id.ToString(), user.UserName!, user.Email!));

        return (true, user.Id, Array.Empty<string>());
    }

    public async Task<(bool Success, IEnumerable<string> Errors)> UpdateUserAsync(
        Guid userId,
        UpdateUserDto updateDto,
        Guid? modifiedBy = null)
    {
        var user = await _userManager.Users
            .Include(u => u.Person)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return (false, new[] { "User not found" });
        }

        // Phase 10.4: Update Person first (if exists), then ApplicationUser
        if (user.Person != null)
        {
            user.Person.FirstName = updateDto.FirstName;
            user.Person.LastName = updateDto.LastName;
            user.Person.MiddleName = updateDto.MiddleName;
            user.Person.Nickname = updateDto.Nickname;
            user.Person.Department = updateDto.Department;
            user.Person.JobTitle = updateDto.JobTitle;
            user.Person.ProfileUrl = updateDto.ProfileUrl;
            user.Person.PictureUrl = updateDto.PictureUrl;
            user.Person.Website = updateDto.Website;
            user.Person.Address = updateDto.Address;
            user.Person.Birthdate = updateDto.Birthdate;
            user.Person.Gender = updateDto.Gender;
            user.Person.TimeZone = updateDto.TimeZone;
            user.Person.Locale = updateDto.Locale;
            user.Person.EmployeeId = updateDto.EmployeeId;
            user.Person.ModifiedBy = modifiedBy;
            user.Person.ModifiedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(CancellationToken.None);
        }

        // Update ApplicationUser properties (keep for backward compatibility)
        user.Email = updateDto.Email;
        if (updateDto.UserName != null)
        {
            user.UserName = updateDto.UserName;
        }
        user.FirstName = updateDto.FirstName;
        user.LastName = updateDto.LastName;
        user.MiddleName = updateDto.MiddleName;
        user.Nickname = updateDto.Nickname;
        user.PhoneNumber = updateDto.PhoneNumber;
        user.Department = updateDto.Department;
        user.JobTitle = updateDto.JobTitle;
        user.ProfileUrl = updateDto.ProfileUrl;
        user.PictureUrl = updateDto.PictureUrl;
        user.Website = updateDto.Website;
        user.Address = updateDto.Address;
        user.Birthdate = updateDto.Birthdate;
        user.Gender = updateDto.Gender;
        user.TimeZone = updateDto.TimeZone;
        user.Locale = updateDto.Locale;
        user.EmployeeId = updateDto.EmployeeId;
        user.IsActive = updateDto.IsActive;
        user.EmailConfirmed = updateDto.EmailConfirmed;
        user.PhoneNumberConfirmed = updateDto.PhoneNumberConfirmed;
        user.ModifiedBy = modifiedBy;
        user.ModifiedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return (false, result.Errors.Select(e => e.Description));
        }

        // Update roles
        var currentRoles = await _userManager.GetRolesAsync(user);
        var rolesToRemove = currentRoles.Except(updateDto.Roles).ToList();
        var rolesToAdd = updateDto.Roles.Except(currentRoles).ToList();

        if (rolesToRemove.Any())
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                return (false, removeResult.Errors.Select(e => e.Description));
            }
        }

        if (rolesToAdd.Any())
        {
            var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                return (false, addResult.Errors.Select(e => e.Description));
            }
        }

        // Publish domain event
        var changes = $"Updated user details and roles";
        await _eventPublisher.PublishAsync(new UserUpdatedEvent(user.Id.ToString(), user.UserName!, changes));

        return (true, Array.Empty<string>());
    }

    public async Task<(bool Success, IEnumerable<string> Errors)> DeactivateUserAsync(
        Guid userId,
        Guid? modifiedBy = null)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, new[] { "User not found" });
        }

        user.IsActive = false;
        user.ModifiedBy = modifiedBy;
        user.ModifiedAt = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);

        if (result.Succeeded)
        {
            // Publish domain event
            await _eventPublisher.PublishAsync(new UserAccountStatusChangedEvent(user.Id.ToString(), user.UserName!, "Active", "Inactive"));
        }

        return result.Succeeded
            ? (true, Array.Empty<string>())
            : (false, result.Errors.Select(e => e.Description));
    }

    public async Task<(bool Success, IEnumerable<string> Errors)> ReactivateUserAsync(
        Guid userId,
        Guid? modifiedBy = null)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, new[] { "User not found" });
        }

        user.IsActive = true;
        user.ModifiedBy = modifiedBy;
        user.ModifiedAt = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);

        if (result.Succeeded)
        {
            // Publish domain event
            await _eventPublisher.PublishAsync(new UserAccountStatusChangedEvent(user.Id.ToString(), user.UserName!, "Inactive", "Active"));
        }

        return result.Succeeded
            ? (true, Array.Empty<string>())
            : (false, result.Errors.Select(e => e.Description));
    }

    public async Task UpdateLastLoginAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user != null)
        {
            user.LastLoginDate = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
        }
    }

    public async Task<(bool Success, IEnumerable<string> Errors)> AssignRolesAsync(
        Guid userId,
        IEnumerable<string> roles)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, new[] { "User not found" });
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        var rolesToRemove = currentRoles.Except(roles).ToList();
        var rolesToAdd = roles.Except(currentRoles).ToList();

        if (rolesToRemove.Any())
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                return (false, removeResult.Errors.Select(e => e.Description));
            }
        }

        if (rolesToAdd.Any())
        {
            var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                return (false, addResult.Errors.Select(e => e.Description));
            }

            // Publish events for added roles
            foreach (var role in rolesToAdd)
            {
                await _eventPublisher.PublishAsync(new UserRoleAssignedEvent(user.Id.ToString(), user.UserName!, role, true));
            }
        }

        // Publish events for removed roles
        foreach (var role in rolesToRemove)
        {
            await _eventPublisher.PublishAsync(new UserRoleAssignedEvent(user.Id.ToString(), user.UserName!, role, false));
        }

        return (true, Array.Empty<string>());
    }

    public async Task<(bool Success, IEnumerable<string> Errors)> AssignRolesByIdAsync(
        Guid userId,
        IEnumerable<Guid> roleIds)
    {
        var roleNames = new List<string>();
        var errors = new List<string>();

        // Resolve role IDs to role names
        foreach (var roleId in roleIds)
        {
            var role = await _roleManager.FindByIdAsync(roleId.ToString());
            if (role == null)
            {
                errors.Add($"Role with ID '{roleId}' not found");
            }
            else
            {
                roleNames.Add(role.Name!);
            }
        }

        // If any role ID was invalid, return error
        if (errors.Any())
        {
            return (false, errors);
        }

        // Delegate to existing AssignRolesAsync method
        return await AssignRolesAsync(userId, roleNames);
    }
}
