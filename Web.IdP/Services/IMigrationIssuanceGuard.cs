namespace Web.IdP.Services;

/// <summary>
/// Confirms that an account may cross an issuance boundary while credential migration is enabled.
/// Callers remain responsible for their existing local lifecycle and MFA checks.
/// </summary>
public interface IMigrationIssuanceGuard
{
    Task<bool> CanIssueAsync(Guid localAccountId, CancellationToken cancellationToken = default);
}
