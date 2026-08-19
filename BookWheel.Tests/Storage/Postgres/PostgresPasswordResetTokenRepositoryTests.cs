using BookWheel.Storage.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookWheel.Tests.Storage.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class PostgresPasswordResetTokenRepositoryTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private PostgresPasswordResetTokenRepository _repository = null!;

    public PostgresPasswordResetTokenRepositoryTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<BookWheelDbContext>(options => options.UseNpgsql(_fixture.ConnectionString));
        var contextFactory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookWheelDbContext>>();
        _repository = new PostgresPasswordResetTokenRepository(contextFactory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateAsync_Returns_Token_That_Validates_Successfully()
    {
        var userId = Guid.NewGuid();

        var (rawToken, expiresAtUtc) = await _repository.CreateAsync(userId);

        Assert.False(string.IsNullOrWhiteSpace(rawToken));
        Assert.True(expiresAtUtc > DateTimeOffset.UtcNow);

        var lookup = await _repository.ValidateAsync(rawToken);
        Assert.True(lookup.IsValid);
        Assert.Equal(userId, lookup.UserId);
    }

    [Fact]
    public async Task CreateAsync_For_Same_User_Invalidates_Previous_Token()
    {
        var userId = Guid.NewGuid();
        var (firstToken, _) = await _repository.CreateAsync(userId);

        var (secondToken, _) = await _repository.CreateAsync(userId);

        var firstLookup = await _repository.ValidateAsync(firstToken);
        var secondLookup = await _repository.ValidateAsync(secondToken);
        Assert.False(firstLookup.IsValid);
        Assert.True(secondLookup.IsValid);
    }

    [Fact]
    public async Task CompleteAsync_Consumes_Token_So_It_Cannot_Be_Reused()
    {
        var userId = Guid.NewGuid();
        var (rawToken, _) = await _repository.CreateAsync(userId);

        var completedUserId = await _repository.CompleteAsync(rawToken);

        Assert.Equal(userId, completedUserId);
        var lookupAfterComplete = await _repository.ValidateAsync(rawToken);
        Assert.False(lookupAfterComplete.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_With_Unknown_Token_Returns_Invalid()
    {
        var lookup = await _repository.ValidateAsync("not-a-real-token");

        Assert.False(lookup.IsValid);
    }

    [Fact]
    public async Task CompleteAsync_With_Unknown_Token_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.CompleteAsync("not-a-real-token"));
    }
}
