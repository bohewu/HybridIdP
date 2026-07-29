namespace Core.Application.Options;

public sealed class PrivilegedTestAdminBootstrapOptions
{
    public const string Section = "SeedData:PrivilegedTestAdminBootstrap";

    /// <summary>
    /// Enables the privileged test fixture only when the host is also running
    /// in an environment allowed by the privileged test bootstrap policy.
    /// </summary>
    public bool Enabled { get; set; }
}
