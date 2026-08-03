using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Infrastructure.Services;

/// <summary>
/// Service for managing Person lifecycle operations.
/// Phase 18: Personnel Lifecycle Management
/// </summary>
public partial class PersonLifecycleService : IPersonLifecycleService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IOpenIddictTokenManager _tokenManager;
    private readonly ILogger<PersonLifecycleService> _logger;

    public PersonLifecycleService(
        IApplicationDbContext dbContext,
        IOpenIddictTokenManager tokenManager,
        ILogger<PersonLifecycleService> logger)
    {
        _dbContext = dbContext;
        _tokenManager = tokenManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> TerminatePersonAsync(Guid personId, DateTime? effectiveDate, Guid terminatedBy, bool revokeTokens = true)
    {
        var person = await _dbContext.Persons.FindAsync(personId);
        if (person == null)
        {
            LogPersonNotFound(personId);
            return false;
        }

        var wasAuthenticationEligible = person.CanAuthenticate();

        // Set the end date and status
        person.EndDate = effectiveDate ?? DateTime.UtcNow;
        person.Status = PersonStatus.Resigned;
        person.ModifiedAt = DateTime.UtcNow;
        person.ModifiedBy = terminatedBy;

        await RotateLinkedUserSecurityStampsIfEligibilityChangedAsync(wasAuthenticationEligible, person, personId);

        await _dbContext.SaveChangesAsync(default);
        LogPersonTerminated(personId, person.Status, terminatedBy);

        if (revokeTokens)
        {
            var revokedCount = await RevokeAllTokensForPersonAsync(personId);
            LogTokensRevoked(personId, revokedCount);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ActivatePersonAsync(Guid personId, DateTime? startDate, Guid activatedBy)
    {
        var person = await _dbContext.Persons.FindAsync(personId);
        if (person == null)
        {
            LogPersonNotFound(personId);
            return false;
        }

        var wasAuthenticationEligible = person.CanAuthenticate();

        person.Status = PersonStatus.Active;
        person.StartDate = startDate ?? DateTime.UtcNow;
        person.ModifiedAt = DateTime.UtcNow;
        person.ModifiedBy = activatedBy;

        await RotateLinkedUserSecurityStampsIfEligibilityChangedAsync(wasAuthenticationEligible, person, personId);

        await _dbContext.SaveChangesAsync(default);
        LogPersonActivated(personId, activatedBy);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SuspendPersonAsync(Guid personId, Guid suspendedBy, bool revokeTokens = true)
    {
        var person = await _dbContext.Persons.FindAsync(personId);
        if (person == null)
        {
            LogPersonNotFound(personId);
            return false;
        }

        var wasAuthenticationEligible = person.CanAuthenticate();

        person.Status = PersonStatus.Suspended;
        person.ModifiedAt = DateTime.UtcNow;
        person.ModifiedBy = suspendedBy;

        await RotateLinkedUserSecurityStampsIfEligibilityChangedAsync(wasAuthenticationEligible, person, personId);

        await _dbContext.SaveChangesAsync(default);
        LogPersonSuspended(personId, suspendedBy);

        if (revokeTokens)
        {
            var revokedCount = await RevokeAllTokensForPersonAsync(personId);
            LogTokensRevoked(personId, revokedCount);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ChangeStatusAsync(Guid personId, PersonStatus newStatus, Guid changedBy)
    {
        var person = await _dbContext.Persons.FindAsync(personId);
        if (person == null)
        {
            LogPersonNotFound(personId);
            return false;
        }

        var oldStatus = person.Status;
        var wasAuthenticationEligible = person.CanAuthenticate();
        person.Status = newStatus;
        person.ModifiedAt = DateTime.UtcNow;
        person.ModifiedBy = changedBy;

        await RotateLinkedUserSecurityStampsIfEligibilityChangedAsync(wasAuthenticationEligible, person, personId);

        await _dbContext.SaveChangesAsync(default);
        LogPersonStatusChanged(personId, oldStatus, newStatus, changedBy);

        return true;
    }

    /// <inheritdoc />
    public async Task<int> RevokeAllTokensForPersonAsync(Guid personId)
    {
        // Get all user IDs linked to this person
        var userIds = await _dbContext.Users
            .Where(u => u.PersonId == personId)
            .Select(u => u.Id)
            .ToListAsync();

        if (userIds.Count == 0)
        {
            return 0;
        }

        int revokedCount = 0;
        foreach (var userId in userIds)
        {
            // Find all tokens for this user
            var userIdString = userId.ToString();
            await foreach (var token in _tokenManager.FindBySubjectAsync(userIdString))
            {
                await _tokenManager.TryRevokeAsync(token);
                revokedCount++;
            }
        }

        return revokedCount;
    }

    /// <inheritdoc />
    public async Task<bool> SoftDeletePersonAsync(Guid personId, Guid deletedBy, bool revokeTokens = true)
    {
        var person = await _dbContext.Persons.FindAsync(personId);
        if (person == null)
        {
            LogPersonNotFound(personId);
            return false;
        }

        var wasAuthenticationEligible = person.CanAuthenticate();

        person.IsDeleted = true;
        person.DeletedAt = DateTime.UtcNow;
        person.DeletedBy = deletedBy;
        person.ModifiedAt = DateTime.UtcNow;
        person.ModifiedBy = deletedBy;

        await RotateLinkedUserSecurityStampsIfEligibilityChangedAsync(wasAuthenticationEligible, person, personId);

        await _dbContext.SaveChangesAsync(default);
        LogPersonSoftDeleted(personId, deletedBy);

        if (revokeTokens)
        {
            var revokedCount = await RevokeAllTokensForPersonAsync(personId);
            LogTokensRevoked(personId, revokedCount);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<int> ProcessScheduledTransitionsAsync()
    {
        var now = DateTime.UtcNow.Date;
        var changedCount = 0;
        var transitionedPersonIds = new List<Guid>();
        var expiredPersonIds = new List<Guid>();

        // Auto-activate: Pending persons with StartDate <= now
        var pendingPersons = await _dbContext.Persons
            .Where(p => !p.IsDeleted 
                     && p.Status == PersonStatus.Pending 
                     && p.StartDate.HasValue 
                     && p.StartDate.Value.Date <= now)
            .ToListAsync();

        foreach (var person in pendingPersons)
        {
            person.Status = PersonStatus.Active;
            person.ModifiedAt = DateTime.UtcNow;
            LogAutoActivated(person.Id, person.StartDate!.Value);
            transitionedPersonIds.Add(person.Id);
            changedCount++;
        }

        // Auto-terminate: Active persons with EndDate < now (already passed)
        var expiredPersons = await _dbContext.Persons
            .Where(p => !p.IsDeleted 
                     && p.Status == PersonStatus.Active 
                     && p.EndDate.HasValue 
                     && p.EndDate.Value.Date < now)
            .ToListAsync();

        foreach (var person in expiredPersons)
        {
            person.Status = PersonStatus.Resigned;
            person.ModifiedAt = DateTime.UtcNow;
            LogAutoTerminated(person.Id, person.EndDate!.Value);
            transitionedPersonIds.Add(person.Id);
            expiredPersonIds.Add(person.Id);
            changedCount++;
        }

        if (changedCount > 0)
        {
            await RotateLinkedUserSecurityStampsAsync(transitionedPersonIds);
            await _dbContext.SaveChangesAsync(default);
        }

        foreach (var personId in expiredPersonIds)
        {
            var revokedTokens = await RevokeAllTokensForPersonAsync(personId);
            if (revokedTokens > 0)
            {
                LogTokensRevoked(personId, revokedTokens);
            }
        }

        return changedCount;
    }

    private async Task RotateLinkedUserSecurityStampsIfEligibilityChangedAsync(
        bool wasAuthenticationEligible,
        Person person,
        Guid personId)
    {
        if (wasAuthenticationEligible != person.CanAuthenticate())
        {
            await RotateLinkedUserSecurityStampsAsync(personId);
        }
    }

    private async Task RotateLinkedUserSecurityStampsAsync(Guid personId)
    {
        await RotateLinkedUserSecurityStampsAsync([personId]);
    }

    private async Task RotateLinkedUserSecurityStampsAsync(IEnumerable<Guid> personIds)
    {
        var ids = personIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var linkedUsers = await _dbContext.Users
            .Where(user => user.PersonId.HasValue && ids.Contains(user.PersonId.Value))
            .ToListAsync();

        foreach (var linkedUser in linkedUsers)
        {
            linkedUser.SecurityStamp = Guid.NewGuid().ToString();
        }
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Warning, Message = "Person {PersonId} not found.")]
    partial void LogPersonNotFound(Guid personId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Person {PersonId} terminated with status {Status} by {TerminatedBy}.")]
    partial void LogPersonTerminated(Guid personId, PersonStatus status, Guid terminatedBy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Person {PersonId} activated by {ActivatedBy}.")]
    partial void LogPersonActivated(Guid personId, Guid activatedBy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Person {PersonId} suspended by {SuspendedBy}.")]
    partial void LogPersonSuspended(Guid personId, Guid suspendedBy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Person {PersonId} status changed from {OldStatus} to {NewStatus} by {ChangedBy}.")]
    partial void LogPersonStatusChanged(Guid personId, PersonStatus oldStatus, PersonStatus newStatus, Guid changedBy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Person {PersonId} soft deleted by {DeletedBy}.")]
    partial void LogPersonSoftDeleted(Guid personId, Guid deletedBy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Revoked {RevokedCount} tokens for person {PersonId}.")]
    partial void LogTokensRevoked(Guid personId, int revokedCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Person {PersonId} auto-activated (start date: {StartDate}).")]
    partial void LogAutoActivated(Guid personId, DateTime startDate);

    [LoggerMessage(Level = LogLevel.Information, Message = "Person {PersonId} auto-terminated (end date: {EndDate}).")]
    partial void LogAutoTerminated(Guid personId, DateTime endDate);

    #endregion
}
