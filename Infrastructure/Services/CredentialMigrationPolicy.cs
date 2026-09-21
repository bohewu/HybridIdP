using Core.Application.Ports;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// Applies the deployment-owned migration and OTP floors without exposing account details.
/// </summary>
public sealed class CredentialMigrationPolicy : ICredentialMigrationPolicy
{
    private readonly CredentialMigrationOptions _options;

    public CredentialMigrationPolicy(IOptions<CredentialMigrationOptions> options)
    {
        _options = options.Value;
    }

    public MigrationPolicyDecision Evaluate(MigrationPolicyRequest request, DateTimeOffset now) =>
        new(
            _options.IsLegacyProofAllowed(now) && !request.IsCompleted,
            _options.GetEffectiveEmailOtpPolicy(request.RequestedEmailOtpPolicy));
}
