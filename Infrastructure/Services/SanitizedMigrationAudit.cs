using Core.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Emits only approved migration categories, states, and correlation identifiers.
/// </summary>
public sealed partial class SanitizedMigrationAudit : ISanitizedMigrationAudit
{
    private readonly ILogger<SanitizedMigrationAudit> _logger;

    public SanitizedMigrationAudit(ILogger<SanitizedMigrationAudit> logger)
    {
        _logger = logger;
    }

    public Task RecordAsync(SanitizedMigrationAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LogMigrationAudit(auditEvent.Category, auditEvent.State, auditEvent.CorrelationId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Credential migration event {Category}, state {State}, correlation {CorrelationId}.")]
    private partial void LogMigrationAudit(
        SanitizedMigrationAuditCategory category,
        Core.Domain.Entities.CredentialMigrationState? state,
        Guid correlationId);
}
