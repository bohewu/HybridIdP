using Core.Domain.Entities;

namespace Core.Application.Ports;

/// <summary>
/// Records only correlation data, a typed category, and lifecycle state.
/// </summary>
public interface ISanitizedMigrationAudit
{
    Task RecordAsync(SanitizedMigrationAuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public sealed record SanitizedMigrationAuditEvent(
    Guid CorrelationId,
    SanitizedMigrationAuditCategory Category,
    CredentialMigrationState? State = null);

public enum SanitizedMigrationAuditCategory
{
    PolicyDenied,
    ProofDenied,
    DirectoryDenied,
    DirectoryUnavailable,
    ContinuationDenied,
    RecoveryRequired,
    ProofValidated,
    DirectoryCredentialCommitted,
    Completed
}
