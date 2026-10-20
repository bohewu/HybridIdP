using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using System.Text.Json;
using System.Threading;
using Infrastructure.Authorization;
using Infrastructure.Identity;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class UserManagementService : IUserManagementService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly IApplicationDbContext _context;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly PrivilegedRoleProtectionOptions _roleProtection;
    private readonly string? _trustedProviderNamespace;
    private readonly INativeFieldPolicyService _nativeFields;

    public UserManagementService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IDomainEventPublisher eventPublisher,
        IApplicationDbContext context,
        IOpenIddictApplicationManager applicationManager,
        INativeFieldPolicyService nativeFields,
        IOptions<PrivilegedRoleProtectionOptions>? roleProtection = null,
        IOptions<ProviderProofOptions>? providerProofOptions = null)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _eventPublisher = eventPublisher;
        _context = context;
        _applicationManager = applicationManager;
        _nativeFields = nativeFields;
        _roleProtection = roleProtection?.Value ?? new PrivilegedRoleProtectionOptions();
        _trustedProviderNamespace = providerProofOptions?.Value.TrustedProviderNamespace;
    }

    public async Task<PagedUsersDto> GetUsersAsync(
        int skip = 0,
        int take = 25,
        string? search = null,
        string? role = null,
        bool? isActive = null,
        string? sortBy = "createdat",
        string? sortDirection = "desc",
        CancellationToken cancellationToken = default)
    {
        var query = _userManager.Users.Include(u => u.Person).AsQueryable();

        // Get all user IDs that have passkeys (for HasPasskey flag)
        var usersWithPasskeys = await _context.UserCredentials
            .Where(c => c.DisabledAtUtc == null)
            .Select(c => c.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Filter out soft-deleted users
        query = query.Where(u => !u.IsDeleted);

        query = QueryNativeNames(query, search, isActive, sortBy, sortDirection);

        // Use synchronous enumeration to support both EF Core and in-memory LINQ providers in tests.
        // (Async 'CountAsync/ToListAsync' requires an EF Core IAsyncQueryProvider; mocked IQueryable lacks it.)
        var totalCount = query.Count();
        var users = query
            .Skip(skip)
            .Take(take)
            .ToList();

        var userSummaries = new List<UserSummaryDto>();
        // Fetch only this page's managed display metadata in one query; never query per user.
        var userIds = users.Select(user => user.Id).ToList();
        var managedLogins = string.IsNullOrWhiteSpace(_trustedProviderNamespace) || userIds.Count == 0
            ? new List<IdentityUserLogin<Guid>>()
            : await _context.UserLogins.AsNoTracking()
                .Where(login => userIds.Contains(login.UserId) && login.LoginProvider == _trustedProviderNamespace)
                .ToListAsync(cancellationToken);
        var managedNames = managedLogins.GroupBy(login => login.UserId).ToDictionary(group => group.Key,
            group => ManagedCredentialDisplay.GetAssuredName(group.Select(login =>
                new UserLoginInfo(login.LoginProvider, login.ProviderKey, login.ProviderDisplayName)), _trustedProviderNamespace));

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
                DisplayName = ManagedCredentialDisplay.GetDisplayName(user, user.Person,
                    managedNames.GetValueOrDefault(user.Id), user.UserName ?? string.Empty),
                FirstName = user.Person?.FirstName ?? user.FirstName,
                LastName = user.Person?.LastName ?? user.LastName,
                Department = user.Person?.Department ?? user.Department,
                JobTitle = user.Person?.JobTitle ?? user.JobTitle,
                IsActive = user.IsActive,
                EmailConfirmed = user.EmailConfirmed,
                LastLoginDate = user.LastLoginDate,
                CreatedAt = user.CreatedAt,
                TwoFactorEnabled = user.TwoFactorEnabled,
                EmailMfaEnabled = user.EmailMfaEnabled,
                HasPasskey = usersWithPasskeys.Contains(user.Id),
                IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow,
                LockoutEnd = user.LockoutEnd,
                AccessFailedCount = user.AccessFailedCount,
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

    // Keep name filtering and ordering in SQL; raw provider display metadata is not native-field authority.
    internal static IQueryable<ApplicationUser> QueryNativeNames(
        IQueryable<ApplicationUser> users, string? search, bool? isActive,
        string? sortBy, string? sortDirection)
    {
        if (isActive.HasValue)
            users = users.Where(user => user.IsActive == isActive.Value);

        var query = users.Select(user => new
        {
            User = user,
            UserName = ((user.FirstName ?? "").Trim()
                + (user.MiddleName == null || user.MiddleName.Trim() == "" ? "" : " " + user.MiddleName.Trim())
                + (user.LastName == null || user.LastName.Trim() == "" ? "" : " " + user.LastName.Trim())).Trim(),
            PersonName = user.Person == null ? "" : ((user.Person.FirstName ?? "").Trim()
                + (user.Person.MiddleName == null || user.Person.MiddleName.Trim() == "" ? "" : " " + user.Person.MiddleName.Trim())
                + (user.Person.LastName == null || user.Person.LastName.Trim() == "" ? "" : " " + user.Person.LastName.Trim())).Trim()
        }).Select(row => new
        {
            row.User,
            Name = row.UserName != "" ? row.UserName
                : row.PersonName != "" ? row.PersonName
                : row.User.Nickname != null && row.User.Nickname.Trim() != "" ? row.User.Nickname.Trim()
                : row.User.Person != null && row.User.Person.Nickname != null && row.User.Person.Nickname.Trim() != "" ? row.User.Person.Nickname.Trim()
                : row.User.UserName ?? ""
        });

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(row => row.Name.Contains(search)
                || (row.User.FirstName != null && row.User.FirstName.Contains(search))
                || (row.User.LastName != null && row.User.LastName.Contains(search))
                || (row.User.Nickname != null && row.User.Nickname.Contains(search))
                || (row.User.Person != null && ((row.User.Person.FirstName != null && row.User.Person.FirstName.Contains(search))
                    || (row.User.Person.LastName != null && row.User.Person.LastName.Contains(search))
                    || (row.User.Person.Nickname != null && row.User.Person.Nickname.Contains(search))))
                || (row.User.Email != null && row.User.Email.Contains(search))
                || (row.User.UserName != null && row.User.UserName.Contains(search)));
        }

        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        var ordered = sortBy?.ToLowerInvariant() switch
        {
            "displayname" or "firstname" => descending ? query.OrderByDescending(row => row.Name) : query.OrderBy(row => row.Name),
            "username" => descending ? query.OrderByDescending(row => row.User.UserName) : query.OrderBy(row => row.User.UserName),
            "lastname" => descending ? query.OrderByDescending(row => row.User.LastName) : query.OrderBy(row => row.User.LastName),
            "department" => descending
                ? query.OrderByDescending(row => (row.User.Person != null ? row.User.Person.Department : row.User.Department) ?? "")
                : query.OrderBy(row => (row.User.Person != null ? row.User.Person.Department : row.User.Department) ?? ""),
            "createdat" => descending ? query.OrderByDescending(row => row.User.CreatedAt) : query.OrderBy(row => row.User.CreatedAt),
            _ => descending ? query.OrderByDescending(row => row.User.Email) : query.OrderBy(row => row.User.Email)
        };
        return ordered.ThenBy(row => row.User.Id).Select(row => row.User);
    }

    public async Task<UserDetailDto?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.Users
            .Include(u => u.Person)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
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
            IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow,
            LockoutEnd = user.LockoutEnd,
            AccessFailedCount = user.AccessFailedCount,
            CreatedAt = user.CreatedAt,
            ModifiedAt = user.ModifiedAt,
            Roles = roles.ToList()
        };
    }

    public async Task<(bool Success, Guid? UserId, IEnumerable<string> Errors)> CreateUserAsync(
        CreateUserDto createDto,
        Guid? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        // Phase 10.4: Create Person first, then ApplicationUser
        if (_roleProtection.RequireTargetMfaForPrivilegedRoleAssignment &&
            PrivilegedRoleAssignmentPolicy.ContainsProtectedRole(createDto.Roles ?? [], _roleProtection))
            return (false, null, new[] { PrivilegedRoleAssignmentPolicy.TargetMfaError });

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
        await _context.SaveChangesAsync(cancellationToken);

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
        if (createDto.Roles != null && createDto.Roles.Count > 0)
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
        Guid? modifiedBy = null,
        CancellationToken cancellationToken = default)
    {
        return await UpdateUserCoreAsync(
            userId,
            updateDto,
            modifiedBy,
            updateRoles: true,
            cancellationToken);
    }

    public async Task<(bool Success, IEnumerable<string> Errors)> UpdateUserWithoutRolesAsync(
        Guid userId,
        UpdateUserDto updateDto,
        Guid? modifiedBy = null,
        CancellationToken cancellationToken = default)
    {
        return await UpdateUserCoreAsync(
            userId,
            updateDto,
            modifiedBy,
            updateRoles: false,
            cancellationToken);
    }

    private async Task<(bool Success, IEnumerable<string> Errors)> UpdateUserCoreAsync(
        Guid userId,
        UpdateUserDto updateDto,
        Guid? modifiedBy,
        bool updateRoles,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.Users
            .Include(u => u.Person)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            return (false, new[] { "User not found" });
        }

        // Profile editing is not authority to replace an enrolled authentication factor.
        if (user.EmailMfaEnabled &&
            (!string.Equals(user.Email, updateDto.Email, StringComparison.Ordinal) ||
             user.EmailConfirmed != updateDto.EmailConfirmed))
        {
            return (false, new[] { "EmailMfaAddressChangeRequiresRemoval" });
        }

        if (updateRoles && !await PrivilegedRoleAssignmentPolicy.CanReceiveAsync(user,
                updateDto.Roles.Except(await _userManager.GetRolesAsync(user), StringComparer.OrdinalIgnoreCase),
                _roleProtection, _context, cancellationToken))
            return (false, new[] { PrivilegedRoleAssignmentPolicy.TargetMfaError });

        var dbContext = _context as DbContext;
        await using var transaction = dbContext?.Database.IsRelational() == true
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var committed = false;
        try
        {
            var isActiveChanged = user.IsActive != updateDto.IsActive;

            // Legacy full-profile DTOs have no field-presence flags. Compare with the
            // displayed Person-preferred value; null is omission, empty is explicit clear.
            var manualFields = new Dictionary<NativeField, string?>();
            void AddEdit(NativeField field, string? incoming, string? current)
            {
                if (incoming is not null && (incoming.Length == 0 || incoming != current))
                    manualFields[field] = incoming;
            }
            AddEdit(NativeField.FirstName, updateDto.FirstName, user.Person?.FirstName ?? user.FirstName);
            AddEdit(NativeField.LastName, updateDto.LastName, user.Person?.LastName ?? user.LastName);
            AddEdit(NativeField.Nickname, updateDto.Nickname, user.Person?.Nickname ?? user.Nickname);
            AddEdit(NativeField.Department, updateDto.Department, user.Person?.Department ?? user.Department);
            AddEdit(NativeField.JobTitle, updateDto.JobTitle, user.Person?.JobTitle ?? user.JobTitle);
            AddEdit(NativeField.EmployeeId, updateDto.EmployeeId, user.Person?.EmployeeId ?? user.EmployeeId);
            _nativeFields.ApplyManual(user, manualFields);
            if (user.Person is not null) _nativeFields.ApplyManual(user.Person, manualFields);

            // Phase 10.4: Update Person first (if exists), then ApplicationUser
            if (user.Person != null)
            {
                user.Person.MiddleName = updateDto.MiddleName;
                user.Person.ProfileUrl = updateDto.ProfileUrl;
                user.Person.PictureUrl = updateDto.PictureUrl;
                user.Person.Website = updateDto.Website;
                user.Person.Address = updateDto.Address;
                user.Person.Birthdate = updateDto.Birthdate;
                user.Person.Gender = updateDto.Gender;
                user.Person.TimeZone = updateDto.TimeZone;
                user.Person.Locale = updateDto.Locale;
                user.Person.ModifiedBy = modifiedBy;
                user.Person.ModifiedAt = DateTime.UtcNow;

                // Identity persists the tracked Person and user together below.
            }

            // Update ApplicationUser properties (keep for backward compatibility)
            if (!string.Equals(user.Email, updateDto.Email, StringComparison.Ordinal))
            {
                // A pending enrollment code proves ownership of the previous address only.
                user.EmailMfaCode = null;
                user.EmailMfaCodeExpiry = null;
                user.EmailMfaVerificationAttempts = 0;
            }
            user.Email = updateDto.Email;
            if (updateDto.UserName != null)
            {
                user.UserName = updateDto.UserName;
            }
            user.MiddleName = updateDto.MiddleName;
            user.PhoneNumber = updateDto.PhoneNumber;
            user.ProfileUrl = updateDto.ProfileUrl;
            user.PictureUrl = updateDto.PictureUrl;
            user.Website = updateDto.Website;
            user.Address = updateDto.Address;
            user.Birthdate = updateDto.Birthdate;
            user.Gender = updateDto.Gender;
            user.TimeZone = updateDto.TimeZone;
            user.Locale = updateDto.Locale;
            user.IsActive = updateDto.IsActive;
            user.EmailConfirmed = updateDto.EmailConfirmed;
            user.PhoneNumberConfirmed = updateDto.PhoneNumberConfirmed;
            user.ModifiedBy = modifiedBy;
            user.ModifiedAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;

            // Keep the lifecycle change and its cookie invalidation in the same
            // UserManager persistence operation.
            if (isActiveChanged)
            {
                user.SecurityStamp = Guid.NewGuid().ToString();
            }

            var result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded)
            {
                return (false, result.Errors.Select(e => e.Description));
            }

            if (updateRoles)
            {
                var currentRoles = await _userManager.GetRolesAsync(user);
                var rolesToRemove = currentRoles.Except(updateDto.Roles).ToList();
                var rolesToAdd = updateDto.Roles.Except(currentRoles).ToList();

                if (rolesToRemove.Count > 0)
                {
                    var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
                    if (!removeResult.Succeeded)
                    {
                        return (false, removeResult.Errors.Select(e => e.Description));
                    }
                }

                if (rolesToAdd.Count > 0)
                {
                    var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
                    if (!addResult.Succeeded)
                    {
                        return (false, addResult.Errors.Select(e => e.Description));
                    }
                }
            }

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                // A later save in this request must not flush an unsuccessful update.
                if (user.Person != null) _context.Detach(user.Person);
                _context.Detach(user);
                if (dbContext != null)
                {
                    foreach (var role in dbContext.ChangeTracker.Entries<IdentityUserRole<Guid>>()
                                 .Where(entry => entry.Entity.UserId == user.Id).ToArray())
                        role.State = EntityState.Detached;
                }
            }
        }

        // Publish domain event only after persistence succeeds.
        var changes = $"Updated user details and roles";
        await _eventPublisher.PublishAsync(new UserUpdatedEvent(user.Id.ToString(), user.UserName!, changes));

        return (true, Array.Empty<string>());
    }

    public async Task<(bool Success, IEnumerable<string> Errors)> DeactivateUserAsync(
        Guid userId,
        Guid? modifiedBy = null,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, new[] { "User not found" });
        }

        user.IsActive = false;
        user.ModifiedBy = modifiedBy;
        user.ModifiedAt = DateTime.UtcNow;
        user.SecurityStamp = Guid.NewGuid().ToString();

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
        Guid? modifiedBy = null,
        CancellationToken cancellationToken = default)
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

    public async Task UpdateLastLoginAsync(Guid userId, CancellationToken cancellationToken = default)
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
        IEnumerable<string> roles,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.Users
            .Include(u => u.Person)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            
        if (user == null)
        {
            return (false, new[] { "User not found" });
        }

        var allowedRolesList = roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var currentRoles = await _userManager.GetRolesAsync(user);
        var rolesToRemove = currentRoles.Except(allowedRolesList).ToList();
        var rolesToAdd = allowedRolesList.Except(currentRoles).ToList();

        // Get all sibling accounts (same PersonId) for role synchronization
        List<ApplicationUser> siblingAccounts = new();
        if (user.PersonId.HasValue)
        {
            siblingAccounts = await _userManager.Users
                .Where(u => u.PersonId == user.PersonId.Value && u.Id != userId)
                .ToListAsync(cancellationToken);
        }

        var changes = new List<(ApplicationUser User, List<string> Add, List<string> Remove)>
        {
            (user, rolesToAdd, rolesToRemove)
        };
        foreach (var sibling in siblingAccounts)
        {
            var siblingRoles = await _userManager.GetRolesAsync(sibling);
            changes.Add((sibling,
                allowedRolesList.Except(siblingRoles, StringComparer.OrdinalIgnoreCase).ToList(),
                siblingRoles.Intersect(rolesToRemove, StringComparer.OrdinalIgnoreCase).ToList()));
        }

        // Validate every actual recipient before any removal or addition, even if the selected
        // account already has a requested role that is missing from one of its siblings.
        foreach (var change in changes)
        {
            if (!await PrivilegedRoleAssignmentPolicy.CanReceiveAsync(change.User, change.Add,
                    _roleProtection, _context, cancellationToken))
                return (false, new[] { PrivilegedRoleAssignmentPolicy.TargetMfaError });
        }

        foreach (var change in changes)
        {
            if (change.Remove.Count > 0)
            {
                var result = await _userManager.RemoveFromRolesAsync(change.User, change.Remove);
                if (!result.Succeeded) return (false, result.Errors.Select(error => error.Description));
            }
            if (change.Add.Count > 0)
            {
                var result = await _userManager.AddToRolesAsync(change.User, change.Add);
                if (!result.Succeeded) return (false, result.Errors.Select(error => error.Description));
            }
        }

        foreach (var role in rolesToAdd)
            await _eventPublisher.PublishAsync(new UserRoleAssignedEvent(user.Id.ToString(), user.UserName!, role, true));

        // Publish events for removed roles
        foreach (var role in rolesToRemove)
        {
            await _eventPublisher.PublishAsync(new UserRoleAssignedEvent(user.Id.ToString(), user.UserName!, role, false));
        }

        return (true, Array.Empty<string>());
    }

    public async Task<(bool Success, IEnumerable<string> Errors)> AssignRolesByIdAsync(
        Guid userId,
        IEnumerable<Guid> roleIds,
        CancellationToken cancellationToken = default)
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
        if (errors.Count > 0)
        {
            return (false, errors);
        }

        // Delegate to existing AssignRolesAsync method
        return await AssignRolesAsync(userId, roleNames, cancellationToken);
    }
    public async Task<List<string>> GetUserAppRolesAsync(Guid userId, string clientId, CancellationToken cancellationToken = default)
    {
        return await _context.UserAppRoles
            .Where(uar => uar.UserId == userId && uar.ClientId == clientId)
            .Select(uar => uar.RoleName)
            .ToListAsync(cancellationToken);
    }

    public async Task<(bool Success, IEnumerable<string> Errors)> AssignUserAppRolesAsync(
        Guid userId,
        string clientId,
        IEnumerable<string> roleNames,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate User exists
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, new[] { "User not found" });
        }

        // 2. Validate Client exists
        var application = await _applicationManager.FindByClientIdAsync(clientId, cancellationToken);
        if (application == null)
        {
            return (false, new[] { "Client not found" });
        }

        // 3. Get SupportedRoles from Client Properties
        var properties = await _applicationManager.GetPropertiesAsync(application, cancellationToken);
        var supportedRoles = new List<string>();
        
        if (properties.TryGetValue(Core.Domain.Constants.AuthConstants.Properties.SupportedRoles, out var element) &&
            element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            supportedRoles = element.Deserialize<List<string>>() ?? new();
        }

        // 4. Validate requested roles are supported
        var requestedRoles = roleNames.Distinct().ToList();
        var unsupportedRoles = requestedRoles.Except(supportedRoles).ToList();
        if (unsupportedRoles.Any())
        {
            return (false, new[] { $"Roles not supported by this client: {string.Join(", ", unsupportedRoles)}" });
        }

        // 5. Update UserAppRole table (Transaction would be ideal strictly speaking, but EF ApplyChanges is atomic per SaveChanges)
        
        // Fetch existing roles
        var existingRoles = await _context.UserAppRoles
            .Where(uar => uar.UserId == userId && uar.ClientId == clientId)
            .ToListAsync(cancellationToken);

        _context.UserAppRoles.RemoveRange(existingRoles);

        var newRoles = requestedRoles.Select(role => new UserAppRole
        {
            UserId = userId,
            ClientId = clientId,
            RoleName = role,
            CreatedAt = DateTime.UtcNow
        });

        _context.UserAppRoles.AddRange(newRoles);

        await _context.SaveChangesAsync(cancellationToken);

        // Publish event for audit/sync if needed (Optional for now)
        // await _eventPublisher.PublishAsync(new UserAppRolesUpdatedEvent(...));

        return (true, Array.Empty<string>());
    }
}
