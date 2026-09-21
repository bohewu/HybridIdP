using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Service for managing Person lifecycle operations.
/// Phase 18: Personnel Lifecycle Management
/// </summary>
public partial class PersonLifecycleService : IPersonLifecycleService
{
    private const int ScheduledTransitionBatchSize = 100;

    private readonly IApplicationDbContext _dbContext;
    private readonly IOpenIddictSubjectTokenRevoker _tokenRevoker;
    private readonly ILogger<PersonLifecycleService> _logger;

    public PersonLifecycleService(
        IApplicationDbContext dbContext,
        IOpenIddictSubjectTokenRevoker tokenRevoker,
        ILogger<PersonLifecycleService> logger)
    {
        _dbContext = dbContext;
        _tokenRevoker = tokenRevoker;
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
        await CompletePendingScheduledTokenRevocationBeforeEligibilityRestorationAsync(
            person,
            personId);
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
        if (newStatus == PersonStatus.Active)
        {
            await CompletePendingScheduledTokenRevocationBeforeEligibilityRestorationAsync(
                person,
                personId);
        }
        person.ModifiedAt = DateTime.UtcNow;
        person.ModifiedBy = changedBy;

        await RotateLinkedUserSecurityStampsIfEligibilityChangedAsync(wasAuthenticationEligible, person, personId);

        await _dbContext.SaveChangesAsync(default);
        LogPersonStatusChanged(personId, oldStatus, newStatus, changedBy);

        return true;
    }

    /// <inheritdoc />
    public async Task<int> RevokeAllTokensForPersonAsync(
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        // Get all user IDs linked to this person
        var userIds = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.PersonId == personId)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        if (userIds.Count == 0)
        {
            return 0;
        }

        var revokedCount = 0;
        foreach (var userId in userIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var confirmedCount = await _tokenRevoker.RevokeBySubjectAsync(
                userId.ToString(),
                cancellationToken);
            revokedCount = checked(revokedCount + confirmedCount);
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
    public async Task<int> ProcessScheduledTransitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow.Date;
        await CompletePendingScheduledTokenRevocationsAsync(cancellationToken);

        var activatedCount = await ProcessScheduledActivationsAsync(now, cancellationToken);
        var terminatedCount = await ProcessScheduledTerminationsAsync(now, cancellationToken);
        return checked(activatedCount + terminatedCount);
    }

    private async Task CompletePendingScheduledTokenRevocationsAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var pendingPersons = await _dbContext.Persons
                .Where(person => person.ScheduledTokenRevocationPendingAt.HasValue)
                .OrderBy(person => person.Id)
                .Take(ScheduledTransitionBatchSize)
                .ToListAsync(cancellationToken);

            if (pendingPersons.Count == 0)
            {
                return;
            }

            foreach (var person in pendingPersons)
            {
                var revokedTokens = await RevokeAllTokensForPersonAsync(person.Id, cancellationToken);
                if (revokedTokens > 0)
                {
                    LogTokensRevoked(person.Id, revokedTokens);
                }

                person.ScheduledTokenRevocationPendingAt = null;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            DetachProcessedBatch(pendingPersons, []);
        }
    }

    private async Task<int> ProcessScheduledActivationsAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        var changedCount = 0;

        while (true)
        {
            var pendingPersons = await _dbContext.Persons
                .Where(person => !person.IsDeleted
                    && person.Status == PersonStatus.Pending
                    && person.StartDate.HasValue
                    && person.StartDate.Value.Date <= now)
                .OrderBy(person => person.Id)
                .Take(ScheduledTransitionBatchSize)
                .ToListAsync(cancellationToken);

            if (pendingPersons.Count == 0)
            {
                return changedCount;
            }

            var modifiedAt = DateTime.UtcNow;
            foreach (var person in pendingPersons)
            {
                person.Status = PersonStatus.Active;
                person.ModifiedAt = modifiedAt;
                person.ScheduledTokenRevocationPendingAt = null;
                LogAutoActivated(person.Id, person.StartDate!.Value);
            }

            var linkedUsers = await RotateLinkedUserSecurityStampsAsync(
                pendingPersons.Select(person => person.Id),
                cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            changedCount = checked(changedCount + pendingPersons.Count);
            DetachProcessedBatch(pendingPersons, linkedUsers);
        }
    }

    private async Task<int> ProcessScheduledTerminationsAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        var changedCount = 0;

        while (true)
        {
            var expiredPersons = await _dbContext.Persons
                .Where(person => !person.IsDeleted
                    && person.Status == PersonStatus.Active
                    && person.EndDate.HasValue
                    && person.EndDate.Value.Date < now)
                .OrderBy(person => person.Id)
                .Take(ScheduledTransitionBatchSize)
                .ToListAsync(cancellationToken);

            if (expiredPersons.Count == 0)
            {
                return changedCount;
            }

            var modifiedAt = DateTime.UtcNow;
            foreach (var person in expiredPersons)
            {
                person.Status = PersonStatus.Resigned;
                person.ModifiedAt = modifiedAt;
                person.ScheduledTokenRevocationPendingAt = modifiedAt;
                LogAutoTerminated(person.Id, person.EndDate!.Value);
            }

            var linkedUsers = await RotateLinkedUserSecurityStampsAsync(
                expiredPersons.Select(person => person.Id),
                cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            changedCount = checked(changedCount + expiredPersons.Count);

            foreach (var person in expiredPersons)
            {
                var revokedTokens = await RevokeAllTokensForPersonAsync(person.Id, cancellationToken);
                if (revokedTokens > 0)
                {
                    LogTokensRevoked(person.Id, revokedTokens);
                }

                person.ScheduledTokenRevocationPendingAt = null;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            DetachProcessedBatch(expiredPersons, linkedUsers);
        }
    }

    private async Task CompletePendingScheduledTokenRevocationBeforeEligibilityRestorationAsync(
        Person person,
        Guid personId)
    {
        if (!person.ScheduledTokenRevocationPendingAt.HasValue)
        {
            return;
        }

        var revokedTokens = await RevokeAllTokensForPersonAsync(personId);
        if (revokedTokens > 0)
        {
            LogTokensRevoked(personId, revokedTokens);
        }

        person.ScheduledTokenRevocationPendingAt = null;
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
        await RotateLinkedUserSecurityStampsAsync([personId], CancellationToken.None);
    }

    private async Task<IReadOnlyList<ApplicationUser>> RotateLinkedUserSecurityStampsAsync(
        IEnumerable<Guid> personIds,
        CancellationToken cancellationToken)
    {
        var ids = personIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var linkedUsers = await _dbContext.Users
            .Where(user => user.PersonId.HasValue && ids.Contains(user.PersonId.Value))
            .ToListAsync(cancellationToken);

        foreach (var linkedUser in linkedUsers)
        {
            linkedUser.SecurityStamp = Guid.NewGuid().ToString();
        }

        return linkedUsers;
    }

    private void DetachProcessedBatch(
        IEnumerable<Person> persons,
        IEnumerable<ApplicationUser> linkedUsers)
    {
        foreach (var linkedUser in linkedUsers)
        {
            _dbContext.Detach(linkedUser);
        }

        foreach (var person in persons)
        {
            _dbContext.Detach(person);
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
