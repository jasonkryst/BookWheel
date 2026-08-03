using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.DataProtection;

namespace BookWheel.Tests.Storage;

public sealed class JsonCredentialRepositoryTests : IDisposable
{
    private readonly string _contentRoot;
    private readonly JsonCredentialRepository _repository;

    public JsonCredentialRepositoryTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"bookwheel-credential-repo-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        var environment = StorageTestEnvironment.Create(_contentRoot);
        var dataProtectionProvider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_contentRoot, "keys")));
        _repository = new JsonCredentialRepository(environment, dataProtectionProvider);
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            try
            {
                Directory.Delete(_contentRoot, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures in tests.
            }
        }
    }

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
    public async Task ValidateCredentialsAsync_With_Unknown_Username_Returns_Null()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        var result = await _repository.ValidateCredentialsAsync("nobody", "correct-password");

        Assert.Null(result);
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
    public async Task DeleteUserAsync_On_Unknown_User_Throws()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.DeleteUserAsync(Guid.NewGuid()));
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
    public async Task SetPasswordAsync_On_Unknown_User_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.SetPasswordAsync(Guid.NewGuid(), "new-password"));
    }

    [Fact]
    public async Task GetUsernameAsync_Returns_Null_For_Unknown_User()
    {
        var username = await _repository.GetUsernameAsync(Guid.NewGuid());

        Assert.Null(username);
    }

    [Fact]
    public async Task ReadUsers_With_Corrupted_Credential_File_Throws_And_Quarantines()
    {
        var dataDirectory = Path.Combine(_contentRoot, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        var credentialFilePath = Path.Combine(dataDirectory, "user.cred");
        await File.WriteAllTextAsync(credentialFilePath, "not-a-protected-payload");

        await Assert.ThrowsAsync<CorruptedDataException>(() => _repository.HasAccountAsync());

        var corruptDirectory = Path.Combine(dataDirectory, "corrupt");
        Assert.True(Directory.Exists(corruptDirectory));
        Assert.NotEmpty(Directory.GetFiles(corruptDirectory));
    }
}
