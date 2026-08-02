using System.Reflection;

namespace Tests.SystemTests;

public sealed class SystemTestCollectionConfigurationTests
{
    [Fact]
    public void OwnershipAuthorizationTests_ShouldUseNonParallelCollection()
    {
        var definition = typeof(IsolatedClientAdminHostCollection)
            .GetCustomAttribute<CollectionDefinitionAttribute>();
        Assert.NotNull(definition);
        Assert.True(definition.DisableParallelization);

        foreach (var testClass in new[]
                 {
                     typeof(ClientOwnershipAuthorizationSystemTests),
                     typeof(ScopeOwnershipAuthorizationSystemTests)
                 })
        {
            var assignment = testClass.CustomAttributes.Single(
                attribute => attribute.AttributeType == typeof(CollectionAttribute));
            var assignedCollectionName = Assert.IsType<string>(
                assignment.ConstructorArguments.Single().Value);

            Assert.Equal(IsolatedClientAdminHostCollection.Name, assignedCollectionName);
        }
    }
}
