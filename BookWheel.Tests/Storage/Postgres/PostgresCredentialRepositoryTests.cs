using BookWheel.Storage.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookWheel.Tests.Storage.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class PostgresCredentialRepositoryTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private PostgresCredentialRepository _repository = null!;

    public PostgresCredentialRepositoryTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<BookWheelDbContext>(options => options.UseNpgsql(_fixture.ConnectionString));
        var contextFactory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookWheelDbContext>>();
        _repository = new PostgresCredentialRepository(contextFactory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateInitialAccountAsync_Creates_First_Account_As_Admin()
    {
        var user = await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        Assert.True(user.IsAdmin);
        Assert.True(await _repository.HasAccountAsync());
    }

    [Fact]
    public async Task ValidateCredentialsAsync_With_Correct_Password_Returns_Record()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        var result = await _repository.ValidateCredentialsAsync("admin-one", "correct-password");

        Assert.NotNull(result);
        Assert.Equal("admin-one", result!.Username);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_With_Wrong_Password_Returns_Null()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        var result = await _repository.ValidateCredentialsAsync("admin-one", "wrong-password");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_Is_Case_Insensitive_On_Username()
    {
        await _repository.CreateInitialAccountAsync("Admin-One", "correct-password");

        var result = await _repository.ValidateCredentialsAsync("admin-one", "correct-password");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateUserAsync_Adds_NonAdmin_User()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        var user = await _repository.CreateUserAsync("reader-one", isAdmin: false);

        Assert.False(user.IsAdmin);
        var users = await _repository.GetUsersAsync();
        Assert.Equal(2, users.Count);
    }

    [Fact]
    public async Task CreateUserAsync_With_Duplicate_Username_Throws()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password");
        await _repository.CreateUserAsync("reader-one", isAdmin: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.CreateUserAsync("Reader-One", isAdmin: false));
    }

    [Fact]
    public async Task UpdateUserAsync_Demoting_Last_Admin_Throws()
    {
        var admin = await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.UpdateUserAsync(admin.UserId, admin.Username, isAdmin: false));
    }

    [Fact]
    public async Task DeleteUserAsync_Removes_NonFirst_User()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password");
        var reader = await _repository.CreateUserAsync("reader-one", isAdmin: false);

        var deleted = await _repository.DeleteUserAsync(reader.UserId);

        Assert.Equal(reader.UserId, deleted.UserId);
        var users = await _repository.GetUsersAsync();
        Assert.Single(users);
    }

    [Fact]
    public async Task DeleteUserAsync_On_First_Account_Throws()
    {
        var admin = await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.DeleteUserAsync(admin.UserId));
    }

    [Fact]
    public async Task MarkForPasswordResetAsync_Sets_ForcePasswordReset_And_Clears_Lock()
    {
        var admin = await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        var marked = await _repository.MarkForPasswordResetAsync(admin.UserId);

        Assert.True(marked.ForcePasswordReset);
        Assert.False(marked.IsLocked);
    }

    [Fact]
    public async Task SetPasswordAsync_Updates_Password_And_Clears_ForcePasswordReset()
    {
        var admin = await _repository.CreateInitialAccountAsync("admin-one", "correct-password");
        await _repository.MarkForPasswordResetAsync(admin.UserId);

        var username = await _repository.SetPasswordAsync(admin.UserId, "new-password");

        Assert.Equal("admin-one", username);
        var validated = await _repository.ValidateCredentialsAsync("admin-one", "new-password");
        Assert.NotNull(validated);
        Assert.False(validated!.ForcePasswordReset);
    }

    [Fact]
    public async Task GetUsernameAsync_Returns_Null_For_Unknown_User()
    {
        var username = await _repository.GetUsernameAsync(Guid.NewGuid());

        Assert.Null(username);
    }
}
