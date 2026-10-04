using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

public sealed class RecoveryEmailSelectionOptions
{
    public const string Section = "RecoveryEmailSelection";
    public bool Enabled { get; set; }
    public bool SelfServiceEnabled { get; set; }
    public bool TrustedDefaultFallbackEnabled { get; set; }
    public int RecentStepUpMinutes { get; set; } = 5;
}

public sealed class RecoveryEmailSelectionOptionsValidator : IValidateOptions<RecoveryEmailSelectionOptions>
{
    public ValidateOptionsResult Validate(string? name, RecoveryEmailSelectionOptions options) =>
        options.Enabled && options.RecentStepUpMinutes is < 1 or > 5
            ? ValidateOptionsResult.Fail("Recovery email selection requires a recent step-up window of 1 to 5 minutes.")
            : ValidateOptionsResult.Success;
}
