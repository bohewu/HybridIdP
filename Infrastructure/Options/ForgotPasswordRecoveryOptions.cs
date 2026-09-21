using Core.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

public sealed class ForgotPasswordRecoveryOptions
{
    public const string Section = "ForgotPasswordRecovery";

    // Optional plain text or @ResourceKey guidance; empty values render nothing.
    public string TopNotice { get; set; } = string.Empty;
    public string VerificationTip { get; set; } = string.Empty;
    public string ResetTip { get; set; } = string.Empty;
    public string SuccessReminder { get; set; } = string.Empty;
    public string SupportText { get; set; } = string.Empty;
    public string SupportLabel { get; set; } = string.Empty;
    public string SupportUrl { get; set; } = string.Empty;

    // This is the maximum runtime mode a deployment permits, not native-backend readiness.
    public ForgotPasswordMode DeploymentCeiling { get; set; } = ForgotPasswordMode.External;
    public bool NativeRecoveryEnabled { get; set; }
    public bool NativeDirectoryRecoveryEnabled { get; set; }
    public bool AdminTemporaryCredentialsEnabled { get; set; }
    public bool OrdinaryRecoveryAssistanceEnabled { get; set; }
    public bool PendingDirectorySettlementEnabled { get; set; }
    public int NativeOtpLifetimeMinutes { get; set; } = 10;
    public int NativeOtpMaxAttempts { get; set; } = 5;
    public int NativeOtpResendCooldownSeconds { get; set; } = 60;
    public int OrdinaryRecoveryApprovalLifetimeMinutes { get; set; } = 10;
    public int PendingDirectorySettlementLifetimeMinutes { get; set; } = 10;
    public int PendingDirectorySettlementAuthorizationMinutes { get; set; } = 5;
}

public sealed class ForgotPasswordRecoveryOptionsValidator : IValidateOptions<ForgotPasswordRecoveryOptions>
{
    public ValidateOptionsResult Validate(string? name, ForgotPasswordRecoveryOptions options)
    {
        if (!Enum.IsDefined(options.DeploymentCeiling))
        {
            return ValidateOptionsResult.Fail("Forgot-password recovery deployment ceiling is invalid.");
        }

        if (options.NativeRecoveryEnabled && options.DeploymentCeiling != ForgotPasswordMode.Native)
        {
            return ValidateOptionsResult.Fail("Native recovery requires a Native deployment ceiling.");
        }

        if (options.NativeDirectoryRecoveryEnabled && !options.NativeRecoveryEnabled)
        {
            return ValidateOptionsResult.Fail("Native directory recovery requires native recovery to be enabled.");
        }

        return options.NativeOtpLifetimeMinutes is >= 1 and <= 30 &&
               options.NativeOtpMaxAttempts is >= 1 and <= 10 &&
               options.NativeOtpResendCooldownSeconds is >= 30 and <= 3600 &&
               options.OrdinaryRecoveryApprovalLifetimeMinutes is >= 1 and <= 10 &&
               options.PendingDirectorySettlementLifetimeMinutes is >= 1 and <= 10 &&
               options.PendingDirectorySettlementAuthorizationMinutes is >= 1 and <= 10
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Native recovery OTP limits are invalid.");
    }
}
