namespace Web.IdP.Services;

public interface ICurrentUserLifecycleEligibility
{
    Task<bool> IsEligibleAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
