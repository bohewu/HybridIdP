using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

public sealed class DirectoryIntegrationOptions
{
    public const string Section = "DirectoryIntegration";

    public bool Enabled { get; set; }
    public bool AuthenticationEnabled { get; set; }
    public bool TemporaryCredentialCapabilityEnabled { get; set; }
    public DirectoryTransport Transport { get; set; } = DirectoryTransport.Ldaps;
}

public enum DirectoryTransport
{
    Ldaps,
    StartTls,
    WindowsNegotiate,
    Anonymous,
    SimpleBind
}

public sealed class DirectoryIntegrationOptionsValidator : IValidateOptions<DirectoryIntegrationOptions>
{
    public ValidateOptionsResult Validate(string? name, DirectoryIntegrationOptions options)
    {
        var failures = new List<string>();

        if (options.AuthenticationEnabled && !options.Enabled)
        {
            failures.Add("Directory authentication requires directory integration to be enabled.");
        }


        if (options.TemporaryCredentialCapabilityEnabled && !options.Enabled)
        {
            failures.Add("Directory temporary credentials require directory integration to be enabled.");
        }

        if (options.Transport is not (DirectoryTransport.Ldaps or DirectoryTransport.StartTls or DirectoryTransport.WindowsNegotiate))
        {
            failures.Add("Directory transport must use LDAPS, StartTLS, or Windows Negotiate.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
