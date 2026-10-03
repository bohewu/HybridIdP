namespace Core.Application.Interfaces;

public interface IAccountLifecycleEligibility
{
    Task<bool> IsEligibleAsync(Guid userId, CancellationToken cancellationToken = default);
}
