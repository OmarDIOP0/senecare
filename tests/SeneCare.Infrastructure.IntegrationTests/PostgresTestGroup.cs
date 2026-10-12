using SeneCare.Testing;

namespace SeneCare.Infrastructure.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
