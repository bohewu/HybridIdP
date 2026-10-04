namespace Infrastructure.Options;

public sealed class RecoveryThrottleOptions
{
    public const string Section = "RecoveryThrottle";
    // Supply the same random secret (at least 32 characters) to every instance via secure configuration.
    public string? HashKey { get; set; }
}
