using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Core.Domain.Events;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Services;

public class AuditService : IAuditService,
    IDomainEventHandler<UserCreatedEvent>,
    IDomainEventHandler<UserUpdatedEvent>,
    IDomainEventHandler<UserDeletedEvent>,
    IDomainEventHandler<UserRoleAssignedEvent>,
    IDomainEventHandler<UserPasswordChangedEvent>,
    IDomainEventHandler<UserAccountStatusChangedEvent>,
    IDomainEventHandler<ClientCreatedEvent>,
    IDomainEventHandler<ClientUpdatedEvent>,
    IDomainEventHandler<ClientDeletedEvent>,
    IDomainEventHandler<ClientSecretChangedEvent>,
    IDomainEventHandler<ClientScopeChangedEvent>,
    IDomainEventHandler<RoleCreatedEvent>,
    IDomainEventHandler<RoleUpdatedEvent>,
    IDomainEventHandler<RoleDeletedEvent>,
    IDomainEventHandler<RolePermissionChangedEvent>,
    IDomainEventHandler<ScopeCreatedEvent>,
    IDomainEventHandler<ScopeUpdatedEvent>,
    IDomainEventHandler<ScopeDeletedEvent>,
    IDomainEventHandler<ScopeClaimChangedEvent>,
    IDomainEventHandler<LoginAttemptEvent>,
    IDomainEventHandler<LogoutEvent>,
    IDomainEventHandler<SecurityPolicyUpdatedEvent>
{
    private readonly IApplicationDbContext _db;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly ISettingsService _settingsService;

    public AuditService(IApplicationDbContext db, IDomainEventPublisher eventPublisher, ISettingsService settingsService)
    {
        _db = db;
        _eventPublisher = eventPublisher;
        _settingsService = settingsService;
    }

    public async Task LogEventAsync(string eventType, string? userId, string? details, string? ipAddress, string? userAgent)
    {
        var auditEvent = new AuditEvent
        {
            EventType = eventType,
            UserId = userId,
            Details = details,
            IPAddress = ipAddress,
            UserAgent = userAgent,
            Timestamp = DateTime.UtcNow
        };

        _db.AuditEvents.Add(auditEvent);
        await _db.SaveChangesAsync(CancellationToken.None);

        // Retention policy purge (config key: Audit.RetentionDays)
        var retentionDays = await _settingsService.GetValueAsync<int>("Audit.RetentionDays", CancellationToken.None);
        if (retentionDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var oldEvents = _db.AuditEvents.Where(e => e.Timestamp < cutoff).ToList();
            if (oldEvents.Count > 0)
            {
                _db.AuditEvents.RemoveRange(oldEvents);
                await _db.SaveChangesAsync(CancellationToken.None);
            }
        }

        // Publish domain event
        var domainEvent = new AuditEventLoggedEvent(auditEvent.Id, eventType, userId);
        await _eventPublisher.PublishAsync(domainEvent);
    }

    public async Task<(IEnumerable<AuditEventDto> items, int totalCount)> GetEventsAsync(AuditEventFilterDto filter)
    {
        var query = _db.AuditEvents.AsQueryable();

        if (!string.IsNullOrEmpty(filter.EventType))
        {
            query = query.Where(e => e.EventType == filter.EventType);
        }

        if (!string.IsNullOrEmpty(filter.UserId))
        {
            query = query.Where(e => e.UserId == filter.UserId);
        }

        if (filter.StartDate.HasValue)
        {
            query = query.Where(e => e.Timestamp >= filter.StartDate.Value);
        }

        if (filter.EndDate.HasValue)
        {
            query = query.Where(e => e.Timestamp <= filter.EndDate.Value);
        }

        if (!string.IsNullOrEmpty(filter.IPAddress))
        {
            query = query.Where(e => e.IPAddress == filter.IPAddress);
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(e => new AuditEventDto
            {
                Id = e.Id,
                EventType = e.EventType,
                UserId = e.UserId,
                Timestamp = e.Timestamp,
                Details = e.Details,
                IPAddress = e.IPAddress,
                UserAgent = e.UserAgent
            })
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<AuditEventExportDto?> ExportEventAsync(int eventId)
    {
        var result = await (from e in _db.AuditEvents
                           where e.Id == eventId
                           join u in _db.Users on e.UserId equals u.Id.ToString() into userGroup
                           from u in userGroup.DefaultIfEmpty()
                           select new AuditEventExportDto
                           {
                               Id = e.Id,
                               EventType = e.EventType,
                               UserId = e.UserId,
                               Timestamp = e.Timestamp,
                               Details = e.Details,
                               IPAddress = e.IPAddress,
                               UserAgent = e.UserAgent,
                               Username = u.UserName
                           }).FirstOrDefaultAsync();

        return result;
    }

    // Domain Event Handlers

    public async Task HandleAsync(UserCreatedEvent @event)
    {
        await LogEventAsync("UserCreated", @event.UserId, $"User '{@event.UserName}' ({@event.Email}) was created", null, null);
    }

    public async Task HandleAsync(UserUpdatedEvent @event)
    {
        await LogEventAsync("UserUpdated", @event.UserId, $"User '{@event.UserName}' was updated: {@event.Changes}", null, null);
    }

    public async Task HandleAsync(UserDeletedEvent @event)
    {
        await LogEventAsync("UserDeleted", @event.UserId, $"User '{@event.UserName}' was deleted", null, null);
    }

    public async Task HandleAsync(UserRoleAssignedEvent @event)
    {
        var action = @event.IsAssigned ? "assigned to" : "removed from";
        await LogEventAsync("UserRoleChanged", @event.UserId, $"User '{@event.UserName}' was {action} role '{@event.RoleName}'", null, null);
    }

    public async Task HandleAsync(UserPasswordChangedEvent @event)
    {
        await LogEventAsync("UserPasswordChanged", @event.UserId, $"Password changed for user '{@event.UserName}'", null, null);
    }

    public async Task HandleAsync(UserAccountStatusChangedEvent @event)
    {
        await LogEventAsync("UserStatusChanged", @event.UserId, $"User '{@event.UserName}' status changed from '{@event.OldStatus}' to '{@event.NewStatus}'", null, null);
    }

    public async Task HandleAsync(ClientCreatedEvent @event)
    {
        await LogEventAsync("ClientCreated", null, $"Client '{@event.ClientName}' ({@event.ClientId}) was created", null, null);
    }

    public async Task HandleAsync(ClientUpdatedEvent @event)
    {
        await LogEventAsync("ClientUpdated", null, $"Client '{@event.ClientName}' ({@event.ClientId}) was updated: {@event.Changes}", null, null);
    }

    public async Task HandleAsync(ClientDeletedEvent @event)
    {
        await LogEventAsync("ClientDeleted", null, $"Client '{@event.ClientName}' ({@event.ClientId}) was deleted", null, null);
    }

    public async Task HandleAsync(ClientSecretChangedEvent @event)
    {
        await LogEventAsync("ClientSecretChanged", null, $"Secret changed for client '{@event.ClientName}' ({@event.ClientId})", null, null);
    }

    public async Task HandleAsync(ClientScopeChangedEvent @event)
    {
        await LogEventAsync("ClientScopeChanged", null, $"Scopes changed for client '{@event.ClientName}' ({@event.ClientId}): {@event.ScopeChanges}", null, null);
    }

    public async Task HandleAsync(RoleCreatedEvent @event)
    {
        await LogEventAsync("RoleCreated", null, $"Role '{@event.RoleName}' ({@event.RoleId}) was created", null, null);
    }

    public async Task HandleAsync(RoleUpdatedEvent @event)
    {
        await LogEventAsync("RoleUpdated", null, $"Role '{@event.RoleName}' ({@event.RoleId}) was updated: {@event.Changes}", null, null);
    }

    public async Task HandleAsync(RoleDeletedEvent @event)
    {
        await LogEventAsync("RoleDeleted", null, $"Role '{@event.RoleName}' ({@event.RoleId}) was deleted", null, null);
    }

    public async Task HandleAsync(RolePermissionChangedEvent @event)
    {
        await LogEventAsync("RolePermissionChanged", null, $"Permissions changed for role '{@event.RoleName}' ({@event.RoleId}): {@event.PermissionChanges}", null, null);
    }

    public async Task HandleAsync(ScopeCreatedEvent @event)
    {
        await LogEventAsync("ScopeCreated", null, $"Scope '{@event.ScopeName}' ({@event.ScopeId}) was created", null, null);
    }

    public async Task HandleAsync(ScopeUpdatedEvent @event)
    {
        await LogEventAsync("ScopeUpdated", null, $"Scope '{@event.ScopeName}' ({@event.ScopeId}) was updated: {@event.Changes}", null, null);
    }

    public async Task HandleAsync(ScopeDeletedEvent @event)
    {
        await LogEventAsync("ScopeDeleted", null, $"Scope '{@event.ScopeName}' ({@event.ScopeId}) was deleted", null, null);
    }

    public async Task HandleAsync(ScopeClaimChangedEvent @event)
    {
        await LogEventAsync("ScopeClaimChanged", null, $"Claims changed for scope '{@event.ScopeName}' ({@event.ScopeId}): {@event.ClaimChanges}", null, null);
    }

    public async Task HandleAsync(LoginAttemptEvent @event)
    {
        var status = @event.IsSuccessful ? "successful" : $"failed ({@event.FailureReason})";
        await LogEventAsync("LoginAttempt", @event.UserId, $"Login attempt for user '{@event.UserName}': {status}", @event.IPAddress, @event.UserAgent);
    }

    public async Task HandleAsync(LogoutEvent @event)
    {
        await LogEventAsync("Logout", @event.UserId, $"User '{@event.UserName}' logged out", @event.IPAddress, @event.UserAgent);
    }

    public async Task HandleAsync(SecurityPolicyUpdatedEvent @event)
    {
        await LogEventAsync("SecurityPolicyUpdated", @event.UpdatedByUserId, $"Security policies updated by '{@event.UpdatedByUserName}': {@event.PolicyChanges}", null, null);
    }
}