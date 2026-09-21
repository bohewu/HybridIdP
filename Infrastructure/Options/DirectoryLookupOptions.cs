using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

public sealed class DirectoryLookupOptions
{
    public const string Section = "DirectoryLookup";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

public sealed class DirectoryLookupOptionsValidator : IValidateOptions<DirectoryLookupOptions>
{
    public ValidateOptionsResult Validate(string? name, DirectoryLookupOptions options) =>
        options.Timeout > TimeSpan.Zero && options.Timeout <= TimeSpan.FromSeconds(30)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Directory lookup timeout must be between zero and thirty seconds.");
}
