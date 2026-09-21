using Core.Application.DTOs;
using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

public sealed class CredentialMigrationOptions
{
    public const string Section = "CredentialMigration";

    public bool Enabled { get; set; }
    public EmailOtpPolicy EmailOtpPolicyFloor { get; set; } = EmailOtpPolicy.Disabled;
    public DateTimeOffset? MigrationWindowStartsAtUtc { get; set; }
    public DateTimeOffset? MigrationWindowEndsAtUtc { get; set; }
    public DateTimeOffset? SunsetAtUtc { get; set; }
    public bool OperatorCutoff { get; set; }
    public bool RecoveryEmailEnabled { get; set; }
    public bool MigrationEmailOtpEnabled { get; set; }
    public bool RecoveryAdminAssistanceEnabled { get; set; }
    public int RecoveryOtpLifetimeMinutes { get; set; } = 10;
    public int RecoveryOtpMaxAttempts { get; set; } = 5;
    public int RecoveryOtpResendCooldownSeconds { get; set; } = 60;
    public int RecoveryApprovalLifetimeMinutes { get; set; } = 10;

    public EmailOtpPolicy GetEffectiveEmailOtpPolicy(EmailOtpPolicy requestedPolicy) =>
        EmailOtpPolicies.Effective(requestedPolicy, EmailOtpPolicyFloor);

    public bool IsLegacyProofAllowed(DateTimeOffset now)
    {
        if (!Enabled || OperatorCutoff ||
            (MigrationWindowStartsAtUtc is { } startsAt && now < startsAt) ||
            (MigrationWindowEndsAtUtc is { } endsAt && now >= endsAt) ||
            (SunsetAtUtc is { } sunsetAt && now >= sunsetAt))
        {
            return false;
        }

        return true;
    }
}

public sealed class CredentialMigrationOptionsValidator : IValidateOptions<CredentialMigrationOptions>
{
    private readonly IOptions<DirectoryIntegrationOptions> _directoryOptions;
    private readonly IOptions<LegacyPasswordSyncOptions> _legacyOptions;

    public CredentialMigrationOptionsValidator(
        IOptions<DirectoryIntegrationOptions> directoryOptions,
        IOptions<LegacyPasswordSyncOptions> legacyOptions)
    {
        _directoryOptions = directoryOptions;
        _legacyOptions = legacyOptions;
    }

    public ValidateOptionsResult Validate(string? name, CredentialMigrationOptions options)
    {
        var failures = new List<string>();
        var directory = _directoryOptions.Value;
        var legacy = _legacyOptions.Value;

        var directoryDestinationEnabled = directory.Enabled && directory.AuthenticationEnabled;
        var legacyDestinationEnabled = legacy.Enabled && legacy.Stage2MigrationEnabled;
        if (options.Enabled && !directoryDestinationEnabled && !legacyDestinationEnabled)
        {
            failures.Add("Credential migration requires at least one enabled password destination.");
        }

        if (!Enum.IsDefined(options.EmailOtpPolicyFloor))
        {
            failures.Add("Email OTP policy floor is invalid.");
        }

        if (options.MigrationEmailOtpEnabled && (!options.Enabled || !options.RecoveryEmailEnabled))
        {
            failures.Add("Migration email OTP requires credential migration and recovery email to be enabled.");
        }

        if (options.RecoveryAdminAssistanceEnabled && (!options.Enabled || !options.RecoveryEmailEnabled))
        {
            failures.Add("Recovery administration requires credential migration and recovery email to be enabled.");
        }

        if (options.EmailOtpPolicyFloor != EmailOtpPolicy.Disabled && !options.MigrationEmailOtpEnabled)
        {
            failures.Add("A required migration email OTP policy needs migration email OTP to be enabled.");
        }

        if (options.RecoveryOtpLifetimeMinutes is < 1 or > 10)
        {
            failures.Add("Recovery OTP lifetime must be between one and ten minutes.");
        }

        if (options.RecoveryOtpMaxAttempts is < 1 or > 5)
        {
            failures.Add("Recovery OTP attempts must be between one and five.");
        }

        if (options.RecoveryOtpResendCooldownSeconds < 60)
        {
            failures.Add("Recovery OTP resend cooldown must be at least sixty seconds.");
        }

        if (options.RecoveryApprovalLifetimeMinutes is < 1 or > 10)
        {
            failures.Add("Recovery approval lifetime must be between one and ten minutes.");
        }

        if (options.MigrationWindowStartsAtUtc is { } startsAt &&
            options.MigrationWindowEndsAtUtc is { } endsAt && startsAt >= endsAt)
        {
            failures.Add("Migration window end must be after its start.");
        }

        if (options.MigrationWindowStartsAtUtc is { } migrationStart &&
            options.SunsetAtUtc is { } sunsetAt && migrationStart >= sunsetAt)
        {
            failures.Add("Migration sunset must be after the migration window start.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
