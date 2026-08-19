namespace BookWheel.Tests.Storage.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class PostgresTestFixtureTests
{
    private readonly PostgresTestFixture _fixture;

    public PostgresTestFixtureTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Fixture_Provides_A_Reachable_Migrated_Database()
    {
        await using var context = _fixture.CreateContext();

        var canConnect = await context.Database.CanConnectAsync();

        Assert.True(canConnect);
    }
}
