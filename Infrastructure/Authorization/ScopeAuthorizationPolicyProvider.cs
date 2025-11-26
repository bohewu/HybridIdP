using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Infrastructure.Authorization;

/// <summary>
/// Custom authorization policy provider that dynamically creates scope-based policies
/// Recognizes policy name pattern: "RequireScope:{scopeName}"
/// Falls back to default provider for other policy names
/// </summary>
public class ScopeAuthorizationPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackPolicyProvider;
    private const string PolicyPrefix = "RequireScope:";

    /// <summary>
    /// Initializes a new instance of <see cref="ScopeAuthorizationPolicyProvider"/>
    /// </summary>
    public ScopeAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallbackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);
    }

    /// <summary>
    /// Gets the default authorization policy
    /// </summary>
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
    {
        return _fallbackPolicyProvider.GetDefaultPolicyAsync();
    }

    /// <summary>
    /// Gets the fallback authorization policy when no policy is specified
    /// </summary>
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
    {
        return _fallbackPolicyProvider.GetFallbackPolicyAsync();
    }

    /// <summary>
    /// Gets or creates an authorization policy by name
    /// Dynamically creates policies for "RequireScope:{scopeName}" pattern
    /// </summary>
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Extract scope name from policy name
            var scopeName = policyName.Substring(PolicyPrefix.Length);
            
            if (string.IsNullOrWhiteSpace(scopeName))
            {
                // Invalid policy name, fall back
                return _fallbackPolicyProvider.GetPolicyAsync(policyName);
            }

            // Build policy with ScopeRequirement
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new ScopeRequirement(scopeName))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        // Not a scope policy, use fallback provider
        return _fallbackPolicyProvider.GetPolicyAsync(policyName);
    }
}
