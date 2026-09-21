using Core.Application.Ports;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

public sealed class NativeDirectoryRecoveryBarrier(ApplicationDbContext dbContext)
    : INativeDirectoryRecoveryBarrier
{
    public Task<bool> HasIssuanceBarrierAsync(
        Guid localAccountId,
        CancellationToken cancellationToken = default) =>
        dbContext.NativeDirectoryRecoveryAttempts.AsNoTracking().AnyAsync(
            attempt => attempt.LocalAccountId == localAccountId &&
                (attempt.Status == NativeDirectoryRecoveryStatus.Reserved ||
                 attempt.Status == NativeDirectoryRecoveryStatus.ReconciliationRequired),
            cancellationToken);
}
