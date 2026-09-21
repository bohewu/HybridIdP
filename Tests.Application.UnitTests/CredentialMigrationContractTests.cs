using System.Text.Json;
using Core.Application.DTOs;

namespace Tests.Application.UnitTests;

public class CredentialMigrationContractTests
{
    [Fact]
    public void ProofResult_AuthenticatedResult_SerializesTheAssuredAllowlist()
    {
        var result = new ProofResult
        {
            Outcome = ProofOutcome.Authenticated,
            ProviderNamespace = "example.provider",
            StableSubject = "opaque-subject",
            CanonicalAccount = "canonical-account",
            Assurance = new ProofAssurance
            {
                StableSubjectAssured = true,
                CanonicalAccountAssured = true
            },
            Profile = new AssuredProfile
            {
                DisplayName = "Example User",
                Email = "user@example.test",
                AssuredFields = [AssuredProfileField.DisplayName, AssuredProfileField.Email]
            },
            RequiredActions = [ProofRequiredAction.EmailOtp]
        };

        var json = JsonSerializer.Serialize(result);
        var roundTripped = JsonSerializer.Deserialize<ProofResult>(json);

        Assert.NotNull(roundTripped);
        Assert.True(roundTripped.TryValidate(out _));
        Assert.Contains("Authenticated", json, StringComparison.Ordinal);
        Assert.DoesNotContain("National", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DirectoryName", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProofResult_UnknownOrMalformedVersion_IsRejected()
    {
        var unknownVersion = new ProofResult
        {
            ContractVersion = "2.0",
            Outcome = ProofOutcome.InvalidCredentials
        };
        var malformedVersion = new ProofResult
        {
            ContractVersion = string.Empty,
            Outcome = ProofOutcome.InvalidCredentials
        };
        var malformedSuccess = new ProofResult
        {
            Outcome = ProofOutcome.Authenticated,
            ProviderNamespace = "example.provider",
            StableSubject = "opaque-subject",
            CanonicalAccount = "canonical-account"
        };

        Assert.False(unknownVersion.TryValidate(out _));
        Assert.False(malformedVersion.TryValidate(out _));
        Assert.False(malformedSuccess.TryValidate(out _));
    }

    [Fact]
    public void ProofOutcome_DefinesExactlyTheFrozenTenOutcomes()
    {
        var outcomes = Enum.GetValues<ProofOutcome>();

        Assert.Equal(10, outcomes.Length);
        Assert.Contains(ProofOutcome.Authenticated, outcomes);
        Assert.Contains(ProofOutcome.InvalidCredentials, outcomes);
        Assert.Contains(ProofOutcome.NotFound, outcomes);
        Assert.Contains(ProofOutcome.Disabled, outcomes);
        Assert.Contains(ProofOutcome.Locked, outcomes);
        Assert.Contains(ProofOutcome.Ineligible, outcomes);
        Assert.Contains(ProofOutcome.Ambiguous, outcomes);
        Assert.Contains(ProofOutcome.Malformed, outcomes);
        Assert.Contains(ProofOutcome.Unavailable, outcomes);
        Assert.Contains(ProofOutcome.Timeout, outcomes);
    }
}
