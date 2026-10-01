using System.Net;
using Core.Application.DTOs;
using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

public sealed class RecoveryIdentityVerificationOptions
{
    public const string Section = "RecoveryIdentityVerification";
    public bool Enabled { get; set; }
    public bool RequireForLocalAccounts { get; set; }
    public bool RequireForDirectoryAccounts { get; set; }
    public string Scheme { get; set; } = RecoveryIdentityVerificationContract.Scheme;
    public string? Endpoint { get; set; }
    public string? SharedSecret { get; set; }
    public int TimeoutSeconds { get; set; } = 5;
    public int PrecheckLifetimeMinutes { get; set; } = 5;
    public string LabelResourceKey { get; set; } = "Recovery.Identity.Identifier.Label";
    public string HelpResourceKey { get; set; } = "Recovery.Identity.Identifier.Help";
}

public sealed class RecoveryIdentityVerificationOptionsValidator : IValidateOptions<RecoveryIdentityVerificationOptions>
{
    public ValidateOptionsResult Validate(string? name, RecoveryIdentityVerificationOptions options) =>
        ValidateCore(options, isolatedLoopbackTest: false);

    internal static ValidateOptionsResult ValidateCore(RecoveryIdentityVerificationOptions options, bool isolatedLoopbackTest)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;
        var failures = new List<string>();
        if (!options.RequireForLocalAccounts && !options.RequireForDirectoryAccounts)
            failures.Add("Recovery identity verification requires an explicit account authority cohort.");
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) ||
            string.IsNullOrEmpty(endpoint.Host) || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment) ||
            (endpoint.Scheme != Uri.UriSchemeHttps &&
             !(isolatedLoopbackTest && endpoint.Scheme == Uri.UriSchemeHttp &&
               IPAddress.TryParse(endpoint.DnsSafeHost, out var address) &&
               (address.Equals(IPAddress.Loopback) || address.Equals(IPAddress.IPv6Loopback)))))
            failures.Add("Recovery identity verification requires a complete HTTPS endpoint without credentials, query or fragment.");
        if (string.IsNullOrWhiteSpace(options.SharedSecret) ||
            options.SharedSecret.Any(character => character < 0x21 || character > 0x7e))
            failures.Add("Recovery identity verification requires a nonempty header-safe shared secret from secure configuration.");
        if (options.Scheme != RecoveryIdentityVerificationContract.Scheme)
            failures.Add("Recovery identity verification requires the identity-identifier scheme.");
        if (options.TimeoutSeconds is < 1 or > 10)
            failures.Add("Recovery identity verification timeout must be 1 to 10 seconds.");
        if (options.PrecheckLifetimeMinutes is < 1 or > 5)
            failures.Add("Recovery identity precheck lifetime must be 1 to 5 minutes.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
