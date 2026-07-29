namespace Core.Application;

public interface IOperationalAdminBootstrapService
{
    Task<OperationalAdminBootstrapResult> BootstrapAsync(
        OperationalAdminBootstrapCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record OperationalAdminBootstrapCommand(
    string Email,
    string Name,
    string Password,
    string CorrelationId);

public sealed record OperationalAdminBootstrapResult(bool Succeeded)
{
    public static OperationalAdminBootstrapResult Completed { get; } = new(true);
    public static OperationalAdminBootstrapResult Unavailable { get; } = new(false);
}
