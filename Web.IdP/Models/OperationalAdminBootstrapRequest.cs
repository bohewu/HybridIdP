namespace Web.IdP.Models;

public sealed class OperationalAdminBootstrapRequest
{
    public string? Email { get; init; }
    public string? Name { get; init; }
    public string? Password { get; init; }

    public override string ToString() => "Operational administrator bootstrap request (redacted)";
}

public sealed record OperationalAdminBootstrapResponse(
    string Code,
    string CorrelationId);
