using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Application.Utilities;
using Core.Domain.Entities;
using Core.Domain.Events;
using Core.Domain.Constants; // Added
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.Json;
using Infrastructure.Authorization;
using System;
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
    private readonly ApplicationDbContext _dbContext; // For accessing Roles DbSet
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly ISettingsService _settingsService;
    private readonly PiiMaskingLevel _piiMaskingLevel;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IAdministrativeAuthorizationBoundary? _administrativeBoundary;

    public AuditService(
        IApplicationDbContext db,
        ApplicationDbContext dbContext,
        IDomainEventPublisher eventPublisher,
        ISettingsService settingsService,
        IOptions<AuditOptions> auditOptions,
        IHttpContextAccessor? httpContextAccessor = null,
        IAdministrativeAuthorizationBoundary? administrativeBoundary = null)
    {
        _db = db;
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _settingsService = settingsService;
        _piiMaskingLevel = auditOptions.Value.PiiMaskingLevel;
        _httpContextAccessor = httpContextAccessor;
        _administrativeBoundary = administrativeBoundary;
    }

    public Task LogEventAsync(string eventType, string? userId, string? details, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        return PersistEventAsync(eventType, userId, AddImpersonationDetails(details, _httpContextAccessor?.HttpContext?.User), ipAddress, userAgent, cancellationToken);
    }

    public async Task LogAdministrativeEventAsync(string eventType, string targetType, string targetId, string? details, CancellationToken cancellationToken = default)
    {
        var context = _httpContextAccessor?.HttpContext;
        var authority = context?.Items[AdministrativeAuthorizationBoundary.AuthorityKey] as AdministrativeAuthority
            ?? (_administrativeBoundary == null ? null : await _administrativeBoundary.ResolveAsync());
        string? userId = null;
        var actor = new JsonObject { ["type"] = "system" };
        if (authority?.IsBearer == true)
        {
            actor = new JsonObject
            {
                ["type"] = "client",
                ["id"] = authority.Principal.FindFirst("sub")?.Value,
                ["applicationId"] = authority.Principal.FindFirst(AdministrativeClientGrant.ApplicationClaim)?.Value
            };
        }
        else if (authority != null)
        {
            var subject = authority.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? authority.Principal.FindFirst("sub")?.Value;
            var attribution = GetImpersonationAttribution(authority.Principal);
            if (attribution != null) userId = attribution.Value.ActorUserId.ToString();
            else if (Guid.TryParse(subject, out var actorId)) userId = actorId.ToString();
            actor = new JsonObject { ["type"] = "user", ["id"] = userId };
        }
        var payload = new JsonObject
        {
            ["message"] = details,
            ["actor"] = actor,
            ["target"] = new JsonObject { ["type"] = targetType, ["id"] = targetId }
        }.ToJsonString();
        await PersistEventAsync(eventType, userId, AddImpersonationDetails(payload, authority?.Principal),
            context?.Connection.RemoteIpAddress?.ToString(), context?.Request.Headers.UserAgent.ToString(), cancellationToken);
    }

    private async Task PersistEventAsync(string eventType, string? userId, string? details, string? ipAddress, string? userAgent, CancellationToken cancellationToken)
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
        await _db.SaveChangesAsync(cancellationToken);

        // Retention policy purge (config key: Audit.RetentionDays)
        var retentionDays = await _settingsService.GetValueAsync<int>(SettingKeys.Audit.RetentionDays, cancellationToken);
        if (retentionDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var oldEvents = _db.AuditEvents.Where(e => e.Timestamp < cutoff).ToList();
            if (oldEvents.Count > 0)
            {
                _db.AuditEvents.RemoveRange(oldEvents);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        // Publish domain event
        var domainEvent = new AuditEventLoggedEvent(auditEvent.Id, eventType, userId);
        await _eventPublisher.PublishAsync(domainEvent);
    }

    public Task LogImpersonationEventAsync(string eventType, ClaimsPrincipal impersonatedPrincipal, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var attribution = GetImpersonationAttribution(impersonatedPrincipal)
            ?? throw new InvalidOperationException("Original user identifier not found");
        return PersistEventAsync(eventType, attribution.ActorUserId.ToString(),
            AddImpersonationDetails(null, impersonatedPrincipal), ipAddress, userAgent, cancellationToken);
    }

    internal static string? AddImpersonationDetails(string? details, ClaimsPrincipal? principal)
    {
        var attribution = GetImpersonationAttribution(principal);
        if (attribution is null) return details;

        JsonNode? original = null;
        if (details is not null)
        {
            try { original = JsonNode.Parse(details); }
            catch (JsonException) { original = JsonValue.Create(details); }
        }

        // Preserve existing JSON fields or text; attribution is always server supplied.
        var result = original as JsonObject ?? new JsonObject { ["details"] = original };
        result["impersonation"] = new JsonObject
        {
            ["actorUserId"] = attribution.Value.ActorUserId.ToString(),
            ["subjectUserId"] = attribution.Value.SubjectUserId.ToString()
        };
        return result.ToJsonString();
    }

    private static (Guid ActorUserId, Guid SubjectUserId)? GetImpersonationAttribution(ClaimsPrincipal? principal)
    {
        var identity = principal is null ? null :
            AuthorizationRoleClaimResolver.GetApplicationPrincipal(principal).Identity as ClaimsIdentity ?? principal.Identity as ClaimsIdentity;
        if (identity?.IsAuthenticated != true) return null;
        var actorId = identity.Actor?.FindFirst(AuthConstants.Claims.ImpersonatorId)?.Value
            ?? identity.Actor?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? identity.Actor?.FindFirst("sub")?.Value;
        var preservedActorId = identity.FindFirst(AuthConstants.Claims.ImpersonatorId)?.Value;
        if (identity.Actor is null && preservedActorId is null) return null;

        var subjectId = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? identity.FindFirst("sub")?.Value;
        if (!Guid.TryParse(actorId ?? preservedActorId, out var actorUserId) ||
            !Guid.TryParse(subjectId, out var subjectUserId) ||
            (preservedActorId is not null && (!Guid.TryParse(preservedActorId, out var preservedId) || preservedId != actorUserId)))
        {
            throw new InvalidOperationException("Invalid impersonation attribution");
        }

        return (actorUserId, subjectUserId);
    }

    public async Task<(IEnumerable<AuditEventDto> items, int totalCount)> GetEventsAsync(AuditEventFilterDto filter, CancellationToken cancellationToken = default)
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

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(e => new AuditEventDto
            {
                Id = e.Id,
                EventType = e.EventType,
                UserId = e.UserId,
                Timestamp = NormalizeUtcTimestamp(e.Timestamp),
                Details = e.Details,
                IPAddress = e.IPAddress,
                UserAgent = e.UserAgent
            })
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<AuditEventExportDto?> ExportEventAsync(int eventId, CancellationToken cancellationToken = default)
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
                               Timestamp = NormalizeUtcTimestamp(e.Timestamp),
                               Details = e.Details,
                               IPAddress = e.IPAddress,
                               UserAgent = e.UserAgent,
                               Username = u.UserName
                            }).FirstOrDefaultAsync(cancellationToken);

        return result;
    }

    private static DateTime NormalizeUtcTimestamp(DateTime timestamp)
    {
        return timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
        };
    }

    // Domain Event Handlers

    public async Task HandleAsync(UserCreatedEvent @event)
    {
        var maskedUserName = PiiMasker.MaskUserName(@event.UserName, _piiMaskingLevel);
        var maskedEmail = PiiMasker.MaskEmail(@event.Email, _piiMaskingLevel);
        await LogAdministrativeEventAsync("UserCreated", "User", @event.UserId, $"User '{maskedUserName}' ({maskedEmail}) was created");
    }

    public async Task HandleAsync(UserUpdatedEvent @event)
    {
        var maskedUserName = PiiMasker.MaskUserName(@event.UserName, _piiMaskingLevel);
        await LogAdministrativeEventAsync("UserUpdated", "User", @event.UserId, $"User '{maskedUserName}' was updated: {@event.Changes}");
    }

    public async Task HandleAsync(UserDeletedEvent @event)
    {
        var maskedUserName = PiiMasker.MaskUserName(@event.UserName, _piiMaskingLevel);
        await LogAdministrativeEventAsync("UserDeleted", "User", @event.UserId, $"User '{maskedUserName}' was deleted");
    }

    public async Task HandleAsync(UserRoleAssignedEvent @event)
    {
        var action = @event.IsAssigned ? "assigned to" : "removed from";
        var maskedUserName = PiiMasker.MaskUserName(@event.UserName, _piiMaskingLevel);
        await LogAdministrativeEventAsync("UserRoleChanged", "User", @event.UserId, $"User '{maskedUserName}' was {action} role '{@event.RoleName}'");
    }

    public async Task HandleAsync(UserPasswordChangedEvent @event)
    {
        var maskedUserName = PiiMasker.MaskUserName(@event.UserName, _piiMaskingLevel);
        await LogAdministrativeEventAsync("UserPasswordChanged", "User", @event.UserId, $"Password changed for user '{maskedUserName}'");
    }

    public async Task HandleAsync(UserAccountStatusChangedEvent @event)
    {
        var maskedUserName = PiiMasker.MaskUserName(@event.UserName, _piiMaskingLevel);
        await LogAdministrativeEventAsync("UserStatusChanged", "User", @event.UserId, $"User '{maskedUserName}' status changed from '{@event.OldStatus}' to '{@event.NewStatus}'");
    }

    public async Task HandleAsync(ClientCreatedEvent @event)
    {
        await LogAdministrativeEventAsync("ClientCreated", "Client", @event.ClientId, $"Client '{@event.ClientName}' ({@event.ClientId}) was created");
    }

    public async Task HandleAsync(ClientUpdatedEvent @event)
    {
        await LogAdministrativeEventAsync("ClientUpdated", "Client", @event.ClientId, $"Client '{@event.ClientName}' ({@event.ClientId}) was updated: {@event.Changes}");
    }

    public async Task HandleAsync(ClientDeletedEvent @event)
    {
        await LogAdministrativeEventAsync("ClientDeleted", "Client", @event.ClientId, $"Client '{@event.ClientName}' ({@event.ClientId}) was deleted");
    }

    public async Task HandleAsync(ClientSecretChangedEvent @event)
    {
        await LogAdministrativeEventAsync("ClientSecretChanged", "Client", @event.ClientId, $"Secret changed for client '{@event.ClientName}' ({@event.ClientId})");
    }

    public async Task HandleAsync(ClientScopeChangedEvent @event)
    {
        await LogAdministrativeEventAsync("ClientScopeChanged", "Client", @event.ClientId, $"Scopes changed for client '{@event.ClientName}' ({@event.ClientId}): {@event.ScopeChanges}");
    }

    public async Task HandleAsync(RoleCreatedEvent @event)
    {
        await LogAdministrativeEventAsync("RoleCreated", "Role", @event.RoleId, $"Role '{@event.RoleName}' ({@event.RoleId}) was created");
    }

    public async Task HandleAsync(RoleUpdatedEvent @event)
    {
        await LogAdministrativeEventAsync("RoleUpdated", "Role", @event.RoleId, $"Role '{@event.RoleName}' ({@event.RoleId}) was updated: {@event.Changes}");
    }

    public async Task HandleAsync(RoleDeletedEvent @event)
    {
        await LogAdministrativeEventAsync("RoleDeleted", "Role", @event.RoleId, $"Role '{@event.RoleName}' ({@event.RoleId}) was deleted");
    }

    public async Task HandleAsync(RolePermissionChangedEvent @event)
    {
        await LogAdministrativeEventAsync("RolePermissionChanged", "Role", @event.RoleId, $"Permissions changed for role '{@event.RoleName}' ({@event.RoleId}): {@event.PermissionChanges}");
    }

    public async Task HandleAsync(ScopeCreatedEvent @event)
    {
        await LogAdministrativeEventAsync("ScopeCreated", "Scope", @event.ScopeId, $"Scope '{@event.ScopeName}' ({@event.ScopeId}) was created");
    }

    public async Task HandleAsync(ScopeUpdatedEvent @event)
    {
        await LogAdministrativeEventAsync("ScopeUpdated", "Scope", @event.ScopeId, $"Scope '{@event.ScopeName}' ({@event.ScopeId}) was updated: {@event.Changes}");
    }

    public async Task HandleAsync(ScopeDeletedEvent @event)
    {
        await LogAdministrativeEventAsync("ScopeDeleted", "Scope", @event.ScopeId, $"Scope '{@event.ScopeName}' ({@event.ScopeId}) was deleted");
    }

    public async Task HandleAsync(ScopeClaimChangedEvent @event)
    {
        await LogAdministrativeEventAsync("ScopeClaimChanged", "Scope", @event.ScopeId, $"Claims changed for scope '{@event.ScopeName}' ({@event.ScopeId}): {@event.ClaimChanges}");
    }

    public async Task HandleAsync(LoginAttemptEvent @event)
    {
        var status = @event.IsSuccessful ? "successful" : $"failed ({@event.FailureReason})";
        var maskedUserName = PiiMasker.MaskUserName(@event.UserName, _piiMaskingLevel);
        await LogEventAsync("LoginAttempt", @event.UserId, $"Login attempt for user '{maskedUserName}': {status}", @event.IPAddress, @event.UserAgent);
    }

    public async Task HandleAsync(LogoutEvent @event)
    {
        var maskedUserName = PiiMasker.MaskUserName(@event.UserName, _piiMaskingLevel);
        await LogEventAsync("Logout", @event.UserId, $"User '{maskedUserName}' logged out", @event.IPAddress, @event.UserAgent);
    }

    public async Task HandleAsync(SecurityPolicyUpdatedEvent @event)
    {
        var maskedUserName = PiiMasker.MaskUserName(@event.UpdatedByUserName, _piiMaskingLevel);
        await LogEventAsync("SecurityPolicyUpdated", @event.UpdatedByUserId, $"Security policies updated by '{maskedUserName}': {@event.PolicyChanges}", null, null);
    }

    public async Task LogRoleSwitchAsync(Guid userId, Guid oldRoleId, Guid newRoleId, string sessionAuthorizationId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
    {
        // Get role names for better audit readability
        var oldRole = await _dbContext.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == oldRoleId, cancellationToken);
        var newRole = await _dbContext.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == newRoleId, cancellationToken);

        var details = $"User switched from role '{oldRole?.Name ?? oldRoleId.ToString()}' to '{newRole?.Name ?? newRoleId.ToString()}'. Session: {sessionAuthorizationId}";
        
        await LogEventAsync("RoleSwitch", userId.ToString(), details, ipAddress, userAgent, cancellationToken);
    }

    public async Task LogAccountSwitchAsync(Guid currentUserId, Guid targetAccountId, string reason, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
    {
        // Get user information for better audit readability
        var currentUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
        var targetUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == targetAccountId, cancellationToken);

        var maskedCurrentUserName = PiiMasker.MaskUserName(currentUser?.UserName ?? currentUserId.ToString(), _piiMaskingLevel);
        var maskedTargetUserName = PiiMasker.MaskUserName(targetUser?.UserName ?? targetAccountId.ToString(), _piiMaskingLevel);
        var details = $"User '{maskedCurrentUserName}' switched to account '{maskedTargetUserName}'. Reason: {reason}";
        
        await LogEventAsync("AccountSwitch", currentUserId.ToString(), details, ipAddress, userAgent, cancellationToken);
    }
}
