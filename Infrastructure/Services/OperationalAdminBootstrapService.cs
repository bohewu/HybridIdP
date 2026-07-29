using System.Data;
using System.Text.Json;
using Core.Application;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public sealed partial class OperationalAdminBootstrapService
    : IOperationalAdminBootstrapService
{
    private const string CompletedEventType = "OperationalAdminBootstrapCompleted";
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ILogger<OperationalAdminBootstrapService> _logger;

    public OperationalAdminBootstrapService(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ILogger<OperationalAdminBootstrapService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task<OperationalAdminBootstrapResult> BootstrapAsync(
        OperationalAdminBootstrapCommand command,
        CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            if (!await IsFreshAsync(cancellationToken))
            {
                return await DenyAsync(
                    transaction,
                    command.CorrelationId,
                    "identity_residue",
                    cancellationToken);
            }

            var adminRole = await GetValidAdminRoleAsync(cancellationToken);
            if (adminRole is null)
            {
                return await DenyAsync(
                    transaction,
                    command.CorrelationId,
                    "admin_role_invalid",
                    cancellationToken);
            }

            var now = DateTime.UtcNow;
            var person = new Person
            {
                Id = Guid.NewGuid(),
                Email = command.Email,
                FirstName = command.Name,
                CreatedAt = now
            };
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = command.Email,
                UserName = command.Email,
                FirstName = command.Name,
                PersonId = person.Id,
                IsActive = true,
                EmailConfirmed = true,
                LockoutEnabled = true,
                CreatedAt = now
            };

            if (!await ValidateIdentityAsync(user, command.Password))
            {
                return await DenyAsync(
                    transaction,
                    command.CorrelationId,
                    "identity_validation_failed",
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _dbContext.Persons.Add(person);
            var createResult = await _userManager.CreateAsync(user, command.Password);
            if (!createResult.Succeeded)
            {
                return await DenyAsync(
                    transaction,
                    command.CorrelationId,
                    "identity_creation_failed",
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var roleResult = await _userManager.AddToRoleAsync(
                user,
                AuthConstants.Roles.Admin);
            if (!roleResult.Succeeded)
            {
                return await DenyAsync(
                    transaction,
                    command.CorrelationId,
                    "role_assignment_failed",
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _dbContext.Settings.Add(new Setting
            {
                Id = Guid.NewGuid(),
                Key = SettingKeys.OperationalAdminBootstrapCompleted,
                Value = "completed",
                UpdatedUtc = now,
                UpdatedBy = "system"
            });
            _dbContext.AuditEvents.Add(new AuditEvent
            {
                EventType = CompletedEventType,
                Timestamp = now,
                CreatedAt = now,
                Details = JsonSerializer.Serialize(new
                {
                    correlationId = command.CorrelationId,
                    outcome = "completed",
                    reason = "fresh_install",
                    timestampUtc = now
                })
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken);

            LogOutcome(
                command.CorrelationId,
                "completed",
                "fresh_install",
                DateTimeOffset.UtcNow);
            return OperationalAdminBootstrapResult.Completed;
        }
        catch (OperationCanceledException)
        {
            await RollbackAsync(transaction);
            LogOutcome(
                command.CorrelationId,
                "unavailable",
                "cancelled",
                DateTimeOffset.UtcNow);
            return OperationalAdminBootstrapResult.Unavailable;
        }
        catch (Exception)
        {
            await RollbackAsync(transaction);
            LogOutcome(
                command.CorrelationId,
                "unavailable",
                "operation_failed",
                DateTimeOffset.UtcNow);
            return OperationalAdminBootstrapResult.Unavailable;
        }
        finally
        {
            if (transaction is not null)
            {
                try
                {
                    await transaction.DisposeAsync();
                }
                catch (Exception)
                {
                    LogOutcome(
                        command.CorrelationId,
                        "unavailable",
                        "transaction_dispose_failed",
                        DateTimeOffset.UtcNow);
                }
            }
        }
    }

    private async Task<bool> IsFreshAsync(CancellationToken cancellationToken)
    {
        if (await _dbContext.Settings.AnyAsync(
                setting => setting.Key == SettingKeys.OperationalAdminBootstrapCompleted,
                cancellationToken) ||
            await _dbContext.Users.AnyAsync(cancellationToken) ||
            await _dbContext.Persons.AnyAsync(cancellationToken) ||
            await _dbContext.UserRoles.AnyAsync(cancellationToken) ||
            await _dbContext.UserLogins.AnyAsync(cancellationToken) ||
            await _dbContext.UserClaims.AnyAsync(cancellationToken) ||
            await _dbContext.UserTokens.AnyAsync(cancellationToken) ||
            await _dbContext.RoleClaims.AnyAsync(cancellationToken) ||
            await _dbContext.UserSessions.AnyAsync(cancellationToken) ||
            await _dbContext.UserCredentials.AnyAsync(cancellationToken) ||
            await _dbContext.LoginHistories.AnyAsync(cancellationToken) ||
            await _dbContext.UserAppRoles.AnyAsync(cancellationToken) ||
            await _dbContext.ClientOwnerships.AnyAsync(cancellationToken) ||
            await _dbContext.ScopeOwnerships.AnyAsync(cancellationToken))
        {
            return false;
        }

        return true;
    }

    private async Task<ApplicationRole?> GetValidAdminRoleAsync(
        CancellationToken cancellationToken)
    {
        var normalizedAdminName = _roleManager.NormalizeKey(AuthConstants.Roles.Admin);
        var candidates = await _dbContext.Roles
            .Where(role =>
                role.Name == AuthConstants.Roles.Admin ||
                role.NormalizedName == normalizedAdminName)
            .ToListAsync(cancellationToken);

        if (candidates.Count != 1)
        {
            return null;
        }

        var role = candidates[0];
        if (!role.IsSystem ||
            !string.Equals(role.Name, AuthConstants.Roles.Admin, StringComparison.Ordinal) ||
            !string.Equals(role.NormalizedName, normalizedAdminName, StringComparison.Ordinal))
        {
            return null;
        }

        var expectedPermissions = Permissions.GetAll();
        var actualPermissions = (role.Permissions ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (actualPermissions.Length != expectedPermissions.Count ||
            !actualPermissions.ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedPermissions))
        {
            return null;
        }

        return role;
    }

    private async Task<bool> ValidateIdentityAsync(
        ApplicationUser user,
        string password)
    {
        foreach (var validator in _userManager.UserValidators)
        {
            var result = await validator.ValidateAsync(_userManager, user);
            if (!result.Succeeded)
            {
                return false;
            }
        }

        foreach (var validator in _userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(_userManager, user, password);
            if (!result.Succeeded)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<OperationalAdminBootstrapResult> DenyAsync(
        IDbContextTransaction transaction,
        string correlationId,
        string reason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RollbackAsync(transaction);
        LogOutcome(
            correlationId,
            "unavailable",
            reason,
            DateTimeOffset.UtcNow);
        return OperationalAdminBootstrapResult.Unavailable;
    }

    private async Task RollbackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                // The public outcome stays generic even when rollback itself is uncertain.
            }
        }

        _dbContext.ChangeTracker.Clear();
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Operational administrator bootstrap outcome. CorrelationId={CorrelationId} Outcome={Outcome} Reason={Reason} Utc={Utc}")]
    private partial void LogOutcome(
        string correlationId,
        string outcome,
        string reason,
        DateTimeOffset utc);
}
