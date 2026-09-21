using Core.Application.Ports;
using Core.Domain.Entities;
using Core.Domain;
using Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Web.IdP.Services;

/// <summary>
/// Fails closed on uncertain or incomplete durable migration state.
/// </summary>
public sealed class MigrationIssuanceGuard : IMigrationIssuanceGuard
{
    private readonly ICredentialMigrationStateStore _stateStore;
    private readonly INativeDirectoryRecoveryBarrier _directoryRecoveryBarrier;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CredentialMigrationOptions _options;

    public MigrationIssuanceGuard(
        ICredentialMigrationStateStore stateStore,
        INativeDirectoryRecoveryBarrier directoryRecoveryBarrier,
        UserManager<ApplicationUser> userManager,
        IOptions<CredentialMigrationOptions> options)
    {
        _stateStore = stateStore;
        _directoryRecoveryBarrier = directoryRecoveryBarrier;
        _userManager = userManager;
        _options = options.Value;
    }

    public async Task<bool> CanIssueAsync(
        Guid localAccountId,
        CancellationToken cancellationToken = default)
    {
        if (localAccountId == Guid.Empty)
        {
            return false;
        }

        try
        {
            var user = await _userManager.FindByIdAsync(localAccountId.ToString());
            if (user is null || user.RequiresPasswordChange)
            {
                return false;
            }

            if (await _directoryRecoveryBarrier.HasIssuanceBarrierAsync(localAccountId, cancellationToken))
            {
                return false;
            }

            var record = await _stateStore.FindAsync(localAccountId, cancellationToken);
            if (record is not null)
            {
                return record.State == CredentialMigrationState.LocalFinalized;
            }

            return !_options.Enabled;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
