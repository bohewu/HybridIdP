using Core.Application.DTOs;
using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

public sealed class ProviderLifecycleOptions
{
    public const string Section = "ProviderLifecycle";
    public bool Enabled { get; set; }
    public string? Endpoint { get; set; }
    public string? SharedSecret { get; set; }
    public int TimeoutSeconds { get; set; } = 5;
    public int MaxAgeSeconds { get; set; } = 60;
    public List<ProviderLifecycleRequiredAccount> RequiredAccounts { get; set; } = [];
}

/// <summary>Requiredness comes from the local ID, independently of an existing binding.</summary>
public sealed class ProviderLifecycleRequiredAccount
{
    public const string DirectoryBinding = "DirectoryBinding";
    public const string ExternalLogin = "ExternalLogin";
    public Guid LocalAccountId { get; set; }
    public string BindingKind { get; set; } = string.Empty;
    public string ProviderNamespace { get; set; } = string.Empty;
    public string? ExternalLoginProvider { get; set; }
    public string SourceAuthority { get; set; } = string.Empty;
    // Producer-managed revision, never a local binding ID or security/concurrency stamp.
    public string MappingVersion { get; set; } = string.Empty;
}

public sealed class ProviderLifecycleOptionsValidator : IValidateOptions<ProviderLifecycleOptions>
{
    public ValidateOptionsResult Validate(string? name, ProviderLifecycleOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;
        var failures = new List<string>();
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(endpoint.Host) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
            failures.Add("Lifecycle requires a complete HTTPS endpoint without userinfo, query or fragment.");
        if (string.IsNullOrEmpty(options.SharedSecret) || options.SharedSecret.Any(c => c < 0x21 || c > 0x7e))
            failures.Add("Lifecycle requires a header-safe secret from protected configuration.");
        if (options.TimeoutSeconds is < 1 or > 10) failures.Add("Lifecycle timeout must be 1 to 10 seconds.");
        if (options.MaxAgeSeconds is < 1 or > 300) failures.Add("Lifecycle max age must be 1 to 300 seconds.");
        var ids = new HashSet<Guid>();
        if (options.RequiredAccounts is not { Count: > 0 })
            failures.Add("Lifecycle requires an explicit account scope.");
        else foreach (var account in options.RequiredAccounts)
        {
            if (account is null || account.LocalAccountId == Guid.Empty || !ids.Add(account.LocalAccountId) ||
                !ProviderLifecycleContract.IsOpaque(account.ProviderNamespace, 200) ||
                !ProviderLifecycleContract.IsOpaque(account.SourceAuthority, 200) ||
                !ProviderLifecycleContract.IsOpaque(account.MappingVersion, 128) ||
                (account.BindingKind != ProviderLifecycleRequiredAccount.DirectoryBinding &&
                 account.BindingKind != ProviderLifecycleRequiredAccount.ExternalLogin) ||
                (account.BindingKind == ProviderLifecycleRequiredAccount.ExternalLogin
                    ? !ProviderLifecycleContract.IsOpaque(account.ExternalLoginProvider, 200)
                    : account.ExternalLoginProvider is not null))
                failures.Add("Lifecycle account scope requires a unique local ID, explicit binding selector and approved namespace, authority and mapping.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
