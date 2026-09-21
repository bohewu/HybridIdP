using System.Text.Json.Serialization;

namespace Core.Application.DTOs;

/// <summary>
/// Defines the version and validation rules shared by proof-provider consumers and producers.
/// </summary>
public static class ProviderProofContract
{
    public const string CurrentVersion = "1.0";

    public static bool IsSupportedVersion(string? version) =>
        string.Equals(version, CurrentVersion, StringComparison.Ordinal);
}

/// <summary>
/// Provider-neutral proof input. Credentials are deliberately supplied separately to the port
/// and are not part of this serializable contract.
/// </summary>
public sealed record ProofRequest
{
    public string ContractVersion { get; init; } = ProviderProofContract.CurrentVersion;
    public string AccountName { get; init; } = string.Empty;
    public EmailOtpPolicy RequestedEmailOtpPolicy { get; init; } = EmailOtpPolicy.Disabled;

    public bool TryValidate(out string? error)
    {
        if (!ProviderProofContract.IsSupportedVersion(ContractVersion))
        {
            error = "Unsupported proof contract version.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(AccountName))
        {
            error = "Account name is required.";
            return false;
        }

        if (!Enum.IsDefined(RequestedEmailOtpPolicy))
        {
            error = "Requested email OTP policy is invalid.";
            return false;
        }

        error = null;
        return true;
    }
}

/// <summary>
/// Provider-neutral result for a single proof authority.
/// </summary>
public sealed record ProofResult
{
    public string ContractVersion { get; init; } = ProviderProofContract.CurrentVersion;
    public ProofOutcome Outcome { get; init; }
    public string? ProviderNamespace { get; init; }
    public string? StableSubject { get; init; }
    public string? CanonicalAccount { get; init; }
    public ProofAssurance? Assurance { get; init; }
    public AssuredProfile? Profile { get; init; }
    public IReadOnlyList<ProofRequiredAction> RequiredActions { get; init; } = [];

    public bool TryValidate(out string? error)
    {
        if (!ProviderProofContract.IsSupportedVersion(ContractVersion))
        {
            error = "Unsupported proof contract version.";
            return false;
        }

        if (!Enum.IsDefined(Outcome))
        {
            error = "Unknown proof outcome.";
            return false;
        }

        if (RequiredActions is null || RequiredActions.Any(action => !Enum.IsDefined(action)))
        {
            error = "Invalid required action.";
            return false;
        }

        if (Outcome != ProofOutcome.Authenticated)
        {
            if (ProviderNamespace is not null || StableSubject is not null || CanonicalAccount is not null ||
                Assurance is not null || Profile is not null || RequiredActions.Count != 0)
            {
                error = "Non-success proof results cannot include identity data.";
                return false;
            }

            error = null;
            return true;
        }

        if (string.IsNullOrWhiteSpace(ProviderNamespace) ||
            string.IsNullOrWhiteSpace(StableSubject) ||
            string.IsNullOrWhiteSpace(CanonicalAccount) ||
            Assurance is not { StableSubjectAssured: true, CanonicalAccountAssured: true })
        {
            error = "Successful proof results require assured identity fields.";
            return false;
        }

        if (Profile is not null && !Profile.IsValid())
        {
            error = "Assured profile is invalid.";
            return false;
        }

        error = null;
        return true;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<ProofOutcome>))]
public enum ProofOutcome
{
    Authenticated,
    InvalidCredentials,
    NotFound,
    Disabled,
    Locked,
    Ineligible,
    Ambiguous,
    Malformed,
    Unavailable,
    Timeout
}

/// <summary>
/// Records the assurance required before a successful proof can create a durable binding.
/// </summary>
public sealed record ProofAssurance
{
    public bool StableSubjectAssured { get; init; }
    public bool CanonicalAccountAssured { get; init; }
}

/// <summary>
/// The only display-profile fields permitted by the proof boundary.
/// </summary>
public sealed record AssuredProfile
{
    public string? DisplayName { get; init; }
    public string? GivenName { get; init; }
    public string? Surname { get; init; }
    public string? Email { get; init; }
    public string? Department { get; init; }
    public string? Title { get; init; }
    public string? EmployeeId { get; init; }
    public IReadOnlyList<AssuredProfileField> AssuredFields { get; init; } = [];

    public bool IsValid()
    {
        if (AssuredFields is null || AssuredFields.Distinct().Count() != AssuredFields.Count ||
            AssuredFields.Any(field => !Enum.IsDefined(field)))
        {
            return false;
        }

        return IsAssured(DisplayName, AssuredProfileField.DisplayName) &&
               IsAssured(GivenName, AssuredProfileField.GivenName) &&
               IsAssured(Surname, AssuredProfileField.Surname) &&
               IsAssured(Email, AssuredProfileField.Email) &&
               IsAssured(Department, AssuredProfileField.Department) &&
               IsAssured(Title, AssuredProfileField.Title) &&
               IsAssured(EmployeeId, AssuredProfileField.EmployeeId);
    }

    private bool IsAssured(string? value, AssuredProfileField field) =>
        string.IsNullOrWhiteSpace(value) || AssuredFields.Contains(field);
}

[JsonConverter(typeof(JsonStringEnumConverter<AssuredProfileField>))]
public enum AssuredProfileField
{
    DisplayName,
    GivenName,
    Surname,
    Email,
    Department,
    Title,
    EmployeeId
}

[JsonConverter(typeof(JsonStringEnumConverter<ProofRequiredAction>))]
public enum ProofRequiredAction
{
    EmailOtp
}

[JsonConverter(typeof(JsonStringEnumConverter<EmailOtpPolicy>))]
public enum EmailOtpPolicy
{
    Disabled = 0,
    ProviderRequested = 1,
    Required = 2
}

public static class EmailOtpPolicies
{
    public static EmailOtpPolicy Effective(EmailOtpPolicy requested, EmailOtpPolicy deploymentFloor)
    {
        if (!Enum.IsDefined(requested) || !Enum.IsDefined(deploymentFloor))
        {
            throw new ArgumentOutOfRangeException(nameof(requested), "Email OTP policy is invalid.");
        }

        return (EmailOtpPolicy)Math.Max((int)requested, (int)deploymentFloor);
    }
}
