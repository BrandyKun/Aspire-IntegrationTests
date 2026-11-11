namespace AspireNotifications.IntegrationTests;

/// <summary>
/// xUnit plumbing: every test class marked [Collection(AppHostCollection.Name)] shares ONE
/// AppHostFixture → the system is started once for the whole project, not once per class.
/// (The definition must live in the same assembly as the tests, which is why each test
/// project has its own copy of this tiny file.)
/// </summary>
[CollectionDefinition(Name)]
public sealed class AppHostCollection : ICollectionFixture<AppHostFixture>
{
    public const string Name = "AppHost";
}
