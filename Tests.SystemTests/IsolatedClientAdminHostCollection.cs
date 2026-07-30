namespace Tests.SystemTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IsolatedClientAdminHostCollection : ICollectionFixture<WebIdPServerFixture>
{
    public const string Name = "Isolated Client Admin Host";
}
