using BookWheel.HealthChecks;
using BookWheel.Storage.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task DatabaseHealthCheck_Returns_Healthy_When_Reachable()
    {
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<BookWheelDbContext>(o => o.UseNpgsql(_fixture.ConnectionString));
        var contextFactory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookWheelDbContext>>();
        var check = new DatabaseHealthCheck(contextFactory);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
