namespace AspireNotifications.E2ETests;

/// <summary>One shared AppHostFixture for all E2E test classes (see the IntegrationTests copy for details).</summary>
[CollectionDefinition(Name)]
public sealed class AppHostCollection : ICollectionFixture<AppHostFixture>
{
    public const string Name = "AppHost";
}
