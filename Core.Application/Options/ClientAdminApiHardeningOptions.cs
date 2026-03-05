namespace Core.Application.Options;

public class ClientAdminApiHardeningOptions
{
    public const string Section = "ClientAdminApiHardening";

    /// <summary>
    /// When enabled, write operations in /api/admin/clients are blocked.
    /// This is intended for deployment-level hardening where high-risk client
    /// settings must not be changed via runtime admin UI/API.
    /// </summary>
    public bool DisableClientWriteEndpoints { get; set; } = false;
}
