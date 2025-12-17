using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// Collection definition to ensure MFA tests run sequentially to avoid shared state issues.
/// All MFA-related tests use the same test user (admin@hybridauth.local), so they must not run in parallel.
/// </summary>
[CollectionDefinition("MFA Tests")]
public class MfaTestsCollection : ICollectionFixture<WebIdPServerFixture>
{
    // This class is intentionally empty.
    // It's just a marker to tell xUnit that all tests in this collection share the same fixture
    // and should run sequentially (not in parallel).
}
