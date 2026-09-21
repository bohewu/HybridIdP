using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

public sealed class RecoveryVerificationPolicyOptions
{
    public const string Section = "RecoveryVerificationPolicy";

    public bool Enabled { get; set; }
    public string? CurrentPeriodId { get; set; }
    public DateTimeOffset? EffectiveAtUtc { get; set; }
    public DateTimeOffset? GraceEndsAtUtc { get; set; }
    public bool BootstrapEnabled { get; set; }
    public DateTimeOffset? BootstrapUntilUtc { get; set; }
    public bool AcceptSourceVerifiedEmails { get; set; }
    public bool AcceptPolicyTrustedEmails { get; set; }
}

public sealed class RecoveryVerificationPolicyOptionsValidator : IValidateOptions<RecoveryVerificationPolicyOptions>
{
    public ValidateOptionsResult Validate(string? name, RecoveryVerificationPolicyOptions options)
    {
        var failures = new List<string>();

        if (options.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.CurrentPeriodId) || options.CurrentPeriodId.Length > 128)
            {
                failures.Add("Enabled recovery verification policy requires a current period identifier of at most 128 characters.");
            }

            if (options.EffectiveAtUtc is null || options.EffectiveAtUtc.Value.Offset != TimeSpan.Zero)
            {
                failures.Add("Enabled recovery verification policy requires a UTC effective time.");
            }

            if (options.GraceEndsAtUtc is null || options.GraceEndsAtUtc.Value.Offset != TimeSpan.Zero)
            {
                failures.Add("Enabled recovery verification policy requires a UTC grace deadline.");
            }

            if (options.EffectiveAtUtc is { } effectiveAt &&
                options.GraceEndsAtUtc is { } graceEndsAt &&
                graceEndsAt < effectiveAt)
            {
                failures.Add("Recovery verification grace deadline must not precede the period effective time.");
            }
        }

        if (options.BootstrapEnabled &&
            (options.BootstrapUntilUtc is null || options.BootstrapUntilUtc.Value.Offset != TimeSpan.Zero))
        {
            failures.Add("Enabled recovery verification bootstrap requires a fixed UTC cutoff.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
