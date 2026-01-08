using Xunit;

namespace Tests.SystemTests;

[CollectionDefinition("Shared Server")]
public class SharedServerCollection : ICollectionFixture<WebIdPServerFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
