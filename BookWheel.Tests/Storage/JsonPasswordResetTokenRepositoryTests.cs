using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.DataProtection;

namespace BookWheel.Tests.Storage;

public sealed class JsonPasswordResetTokenRepositoryTests : IDisposable
{
    private readonly string _contentRoot;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly JsonPasswordResetTokenRepository _repository;

    public JsonPasswordResetTokenRepositoryTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"bookwheel-reset-token-repo-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        var environment = StorageTestEnvironment.Create(_contentRoot);
        _dataProtectionProvider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_contentRoot, "keys")));
        _repository = new JsonPasswordResetTokenRepository(environment, _dataProtectionProvider);
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

    [Fact]
    public async Task ValidateAsync_With_Expired_Token_Returns_Invalid()
    {
        var userId = Guid.NewGuid();
        const string rawToken = "expired-test-token";
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        var expiredDocumentJson = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Tokens = new[]
            {
                new
                {
                    UserId = userId,
                    TokenHash = tokenHash,
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1)
                }
            }
        });

        var dataDirectory = Path.Combine(_contentRoot, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        var tokenFilePath = Path.Combine(dataDirectory, "password-reset-tokens.dat");
        var protector = _dataProtectionProvider.CreateProtector("BookWheel.Credentials.v1");
        await File.WriteAllTextAsync(tokenFilePath, protector.Protect(expiredDocumentJson));

        var lookup = await _repository.ValidateAsync(rawToken);

        Assert.False(lookup.IsValid);
    }

    [Fact]
    public async Task ReadTokens_With_Corrupted_Token_File_Throws_And_Quarantines()
    {
        var dataDirectory = Path.Combine(_contentRoot, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        var tokenFilePath = Path.Combine(dataDirectory, "password-reset-tokens.dat");
        await File.WriteAllTextAsync(tokenFilePath, "not-a-protected-payload");

        await Assert.ThrowsAsync<CorruptedDataException>(() => _repository.ValidateAsync("any-token"));

        var corruptDirectory = Path.Combine(dataDirectory, "corrupt");
        Assert.True(Directory.Exists(corruptDirectory));
        Assert.NotEmpty(Directory.GetFiles(corruptDirectory));
    }
}
