using System.Text.Json;
using Core.Application.DTOs;
using Infrastructure.Services;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class ProviderProofJsonContextTests
{
    [Fact]
    public void Deserialize_AuthenticatedProof_ProducesValidContract()
    {
        const string json =
            "{\"contractVersion\":\"1.0\",\"outcome\":\"Authenticated\"," +
            "\"providerNamespace\":\"example.provider\",\"stableSubject\":\"opaque-subject\"," +
            "\"canonicalAccount\":\"account\",\"assurance\":{" +
            "\"stableSubjectAssured\":true,\"canonicalAccountAssured\":true}," +
            "\"requiredActions\":[]}";

        var result = JsonSerializer.Deserialize(json, ProviderProofJsonContext.Default.ProofResult);

        Assert.NotNull(result);
        Assert.Equal(ProofOutcome.Authenticated, result.Outcome);
        Assert.True(result.TryValidate(out var error), error);
    }
}
