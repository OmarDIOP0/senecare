using SeneCare.Testing;

namespace SeneCare.Infrastructure.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class DatabaseTestGroup : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
