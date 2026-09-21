using System.Text.Json;
using Core.Application;
using Core.Application.Ports;

namespace Infrastructure.Services;

public sealed class RecoveryProofAudit : IRecoveryProofAudit
{
    private readonly IAuditService _auditService;

    public RecoveryProofAudit(IAuditService auditService)
    {
        _auditService = auditService;
    }

    public Task RecordAsync(RecoveryProofAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
        _auditService.LogEventAsync(
            auditEvent.Category.ToString(),
            auditEvent.ActorAccountId?.ToString(),
            JsonSerializer.Serialize(new
            {
                auditEvent.CorrelationId,
                auditEvent.TargetAccountId
            }),
            null,
            null,
            cancellationToken);
}
