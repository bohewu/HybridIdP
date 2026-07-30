using System.Reflection;

namespace Tests.SystemTests;

public sealed class SystemTestCollectionConfigurationTests
{
    [Fact]
    public void ClientAdminHostRestartTests_ShouldUseNonParallelCollection()
    {
        var definition = typeof(IsolatedClientAdminHostCollection)
            .GetCustomAttribute<CollectionDefinitionAttribute>();
        var assignment = typeof(ClientOwnershipAuthorizationSystemTests)
            .CustomAttributes
            .Single(attribute => attribute.AttributeType == typeof(CollectionAttribute));
        var assignedCollectionName = Assert.IsType<string>(
            assignment.ConstructorArguments.Single().Value);

        Assert.NotNull(definition);
        Assert.True(definition.DisableParallelization);
        Assert.Equal(IsolatedClientAdminHostCollection.Name, assignedCollectionName);
    }
}
