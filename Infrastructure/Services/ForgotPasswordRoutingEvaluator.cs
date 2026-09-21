using Core.Application.Ports;
using Core.Domain.Enums;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class ForgotPasswordRoutingEvaluator : IForgotPasswordRoutingEvaluator
{
    private readonly ForgotPasswordMode _deploymentCeiling;
    private readonly bool _nativeRecoveryEnabled;

    public ForgotPasswordRoutingEvaluator(IOptions<ForgotPasswordRecoveryOptions> options)
    {
        _deploymentCeiling = options.Value.DeploymentCeiling;
        _nativeRecoveryEnabled = options.Value.NativeRecoveryEnabled;
    }

    public ForgotPasswordRoutingDecision Evaluate(
        ForgotPasswordMode runtimeMode,
        string? customExternalUrl)
    {
        var ceiling = Enum.IsDefined(_deploymentCeiling)
            ? _deploymentCeiling
            : ForgotPasswordMode.Disabled;
        var requestedMode = Enum.IsDefined(runtimeMode)
            ? runtimeMode
            : ForgotPasswordMode.Disabled;
        var permittedMode = IsPermitted(requestedMode, ceiling)
            ? requestedMode
            : ForgotPasswordMode.Disabled;

        if (permittedMode == ForgotPasswordMode.External &&
            TryGetSafeExternalUrl(customExternalUrl, out var externalUrl))
        {
            return new ForgotPasswordRoutingDecision(
                requestedMode,
                ceiling,
                permittedMode,
                ForgotPasswordMode.External,
                externalUrl);
        }

        if (permittedMode == ForgotPasswordMode.Native && _nativeRecoveryEnabled)
        {
            return new ForgotPasswordRoutingDecision(
                requestedMode,
                ceiling,
                permittedMode,
                ForgotPasswordMode.Native,
                null);
        }

        return new ForgotPasswordRoutingDecision(
            requestedMode,
            ceiling,
            permittedMode,
            ForgotPasswordMode.Disabled,
            null);
    }

    private static bool IsPermitted(ForgotPasswordMode runtimeMode, ForgotPasswordMode ceiling) =>
        runtimeMode switch
        {
            ForgotPasswordMode.Disabled => true,
            ForgotPasswordMode.External => ceiling is ForgotPasswordMode.External or ForgotPasswordMode.Native,
            ForgotPasswordMode.Native => ceiling == ForgotPasswordMode.Native,
            _ => false
        };

    private static bool TryGetSafeExternalUrl(string? value, out string? externalUrl)
    {
        externalUrl = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        externalUrl = candidate;
        return true;
    }
}
