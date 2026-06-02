using System.Text.Json;
using System.Security.Cryptography;
using BookWheel.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace BookWheel.Services;

public sealed class CredentialStore
{
    private const int CurrentCredentialSchemaVersion = 2;
    private const int CurrentResetTokenSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly PasswordHasher<string> PasswordHasher = new();

    private readonly string _dataDirectory;
    private readonly string _corruptDataDirectory;
    private readonly string _credentialFilePath;
    private readonly string _passwordResetTokenFilePath;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public sealed class CredentialMigrationResult
    {
        public bool Migrated { get; set; }
        public int UsersAffected { get; set; }
    }

    private sealed class CredentialDocument
    {
        public int SchemaVersion { get; set; } = CurrentCredentialSchemaVersion;
        public List<CredentialRecord> Users { get; set; } = [];
    }

    private sealed class PasswordResetTokenDocument
    {
        public int SchemaVersion { get; set; } = CurrentResetTokenSchemaVersion;
        public List<PasswordResetTokenRecord> Tokens { get; set; } = [];
    }

    public CredentialStore(IWebHostEnvironment environment, IDataProtectionProvider dataProtectionProvider)
    {
        _dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        _corruptDataDirectory = Path.Combine(_dataDirectory, "corrupt");
        _credentialFilePath = Path.Combine(_dataDirectory, "user.cred");
        _passwordResetTokenFilePath = Path.Combine(_dataDirectory, "password-reset-tokens.dat");
        _protector = dataProtectionProvider.CreateProtector("BookWheel.Credentials.v1");
    }

    public async Task<bool> HasAccountAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            return users.Count > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> HasLegacyPayloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var json = await ReadProtectedCredentialJsonUnsafeAsync();
            if (json is null)
            {
                return false;
            }

            if (IsCurrentCredentialDocument(json))
            {
                return false;
            }

            return TryDeserialize<List<CredentialRecord>>(json) is not null
                || TryDeserialize<CredentialRecord>(json) is not null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CredentialMigrationResult> MigrateLegacyPayloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var json = await ReadProtectedCredentialJsonUnsafeAsync();
            if (json is null)
            {
                return new CredentialMigrationResult();
            }

            if (IsCurrentCredentialDocument(json))
            {
                return new CredentialMigrationResult();
            }

            List<CredentialRecord>? users = TryDeserialize<List<CredentialRecord>>(json);
            if (users is null)
            {
                var singleUser = TryDeserialize<CredentialRecord>(json);
                users = singleUser is null ? [] : [singleUser];
            }

            if (users.Count == 0)
            {
                return new CredentialMigrationResult();
            }

            var adminFound = false;
            for (var index = 0; index < users.Count; index++)
            {
                if (users[index].UserId == Guid.Empty)
                {
                    users[index].UserId = Guid.NewGuid();
                }

                if (users[index].CreatedAtUtc == default)
                {
                    users[index].CreatedAtUtc = DateTimeOffset.UtcNow;
                }

                if (users[index].IsAdmin)
                {
                    adminFound = true;
                }
            }

            if (!adminFound)
            {
                users[0].IsAdmin = true;
            }

            await WriteUsersUnsafeAsync(users);
            return new CredentialMigrationResult
            {
                Migrated = true,
                UsersAffected = users.Count
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CredentialRecord> CreateInitialAccountAsync(string username, string password)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            if (users.Count > 0)
            {
                throw new InvalidOperationException("An account already exists.");
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Username and password are required.");
            }

            var normalizedUsername = username.Trim();

            var record = new CredentialRecord
            {
                UserId = Guid.NewGuid(),
                Username = normalizedUsername,
                PasswordHash = PasswordHasher.HashPassword(normalizedUsername, password),
                IsAdmin = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            users.Add(record);
            await WriteUsersUnsafeAsync(users);
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CredentialRecord?> ValidateCredentialsAsync(string username, string password)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            var normalizedUsername = username.Trim();
            var record = users.FirstOrDefault(user =>
                string.Equals(user.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));

            if (record is null)
            {
                return null;
            }

            var result = PasswordHasher.VerifyHashedPassword(record.Username, record.PasswordHash, password);
            return result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded
                ? record
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<UserAccountSummary>> GetUsersAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            return users
                .OrderBy(user => user.CreatedAtUtc)
                .Select(ToSummary)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserAccountSummary> CreateUserAsync(string username, string password, bool isAdmin)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            if (users.Count == 0)
            {
                throw new InvalidOperationException("Create the initial account first.");
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Username and password are required.");
            }

            var normalizedUsername = username.Trim();
            if (users.Any(user => string.Equals(user.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Username already exists.");
            }

            var record = new CredentialRecord
            {
                UserId = Guid.NewGuid(),
                Username = normalizedUsername,
                PasswordHash = PasswordHasher.HashPassword(normalizedUsername, password),
                IsAdmin = isAdmin,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            users.Add(record);
            await WriteUsersUnsafeAsync(users);
            return ToSummary(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            var record = users.FirstOrDefault(user => user.UserId == userId)
                ?? throw new InvalidOperationException("User not found.");

            var normalizedUsername = username.Trim();
            if (string.IsNullOrWhiteSpace(normalizedUsername))
            {
                throw new InvalidOperationException("Username is required.");
            }

            var duplicateUser = users.FirstOrDefault(user =>
                user.UserId != userId && string.Equals(user.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));

            if (duplicateUser is not null)
            {
                throw new InvalidOperationException("Username already exists.");
            }

            if (!isAdmin)
            {
                var adminCount = users.Count(user => user.IsAdmin);
                if (record.IsAdmin && adminCount <= 1)
                {
                    throw new InvalidOperationException("At least one administrator account is required.");
                }
            }

            record.Username = normalizedUsername;
            record.IsAdmin = isAdmin;

            await WriteUsersUnsafeAsync(users);
            return ToSummary(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin, bool isDisabled, bool forcePasswordReset, bool isLocked)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            var record = users.FirstOrDefault(user => user.UserId == userId)
                ?? throw new InvalidOperationException("User not found.");

            var normalizedUsername = username.Trim();
            if (string.IsNullOrWhiteSpace(normalizedUsername))
            {
                throw new InvalidOperationException("Username is required.");
            }

            var duplicateUser = users.FirstOrDefault(user =>
                user.UserId != userId && string.Equals(user.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));

            if (duplicateUser is not null)
            {
                throw new InvalidOperationException("Username already exists.");
            }

            if (!isAdmin)
            {
                var adminCount = users.Count(user => user.IsAdmin);
                if (record.IsAdmin && adminCount <= 1)
                {
                    throw new InvalidOperationException("At least one administrator account is required.");
                }
            }

            record.Username = normalizedUsername;
            record.IsAdmin = isAdmin;
            record.IsDisabled = isDisabled;
            record.ForcePasswordReset = forcePasswordReset;
            record.IsLocked = isLocked;
            record.LockedUntilUtc = isLocked ? DateTimeOffset.UtcNow.AddHours(12) : null;

            await WriteUsersUnsafeAsync(users);
            return ToSummary(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(string ResetLink, DateTimeOffset ExpiresAtUtc, string Username)> CreatePasswordResetLinkAsync(Guid userId, string appBaseUrl)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            var user = users.FirstOrDefault(existingUser => existingUser.UserId == userId)
                ?? throw new InvalidOperationException("User not found.");

            var tokens = await ReadPasswordResetTokensUnsafeAsync();
            var now = DateTimeOffset.UtcNow;
            tokens.RemoveAll(token => token.ExpiresAtUtc <= now || token.UserId == userId);

            var rawToken = GenerateResetToken();
            var expiresAtUtc = now.AddHours(24);
            tokens.Add(new PasswordResetTokenRecord
            {
                UserId = userId,
                TokenHash = HashToken(rawToken),
                CreatedAtUtc = now,
                ExpiresAtUtc = expiresAtUtc
            });

            user.ForcePasswordReset = true;
            user.IsLocked = false;
            user.LockedUntilUtc = null;

            await WriteUsersUnsafeAsync(users);
            await WritePasswordResetTokensUnsafeAsync(tokens);

            var trimmedBaseUrl = appBaseUrl.TrimEnd('/');
            var resetLink = $"{trimmedBaseUrl}/?resetToken={Uri.EscapeDataString(rawToken)}";
            return (resetLink, expiresAtUtc, user.Username);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> CompletePasswordResetAsync(string token, string newPassword)
    {
        await _gate.WaitAsync();
        try
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(newPassword))
            {
                throw new InvalidOperationException("A valid token and password are required.");
            }

            var users = await ReadUsersUnsafeAsync();
            var tokens = await ReadPasswordResetTokensUnsafeAsync();
            var now = DateTimeOffset.UtcNow;
            tokens.RemoveAll(existingToken => existingToken.ExpiresAtUtc <= now);

            var tokenHash = HashToken(token.Trim());
            var matchingToken = tokens.FirstOrDefault(existingToken => existingToken.TokenHash == tokenHash)
                ?? throw new InvalidOperationException("The password reset link is invalid or has expired.");

            if (matchingToken.ExpiresAtUtc <= now)
            {
                tokens.RemoveAll(existingToken => existingToken.TokenHash == tokenHash);
                await WritePasswordResetTokensUnsafeAsync(tokens);
                throw new InvalidOperationException("The password reset link is invalid or has expired.");
            }

            var user = users.FirstOrDefault(existingUser => existingUser.UserId == matchingToken.UserId)
                ?? throw new InvalidOperationException("User not found for this reset link.");

            user.PasswordHash = PasswordHasher.HashPassword(user.Username, newPassword);
            user.ForcePasswordReset = false;
            user.IsLocked = false;
            user.LockedUntilUtc = null;
            tokens.RemoveAll(existingToken => existingToken.TokenHash == tokenHash);
            await WriteUsersUnsafeAsync(users);
            await WritePasswordResetTokensUnsafeAsync(tokens);
            return user.Username;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PasswordResetTokenValidationResult> ValidatePasswordResetTokenAsync(string token)
    {
        await _gate.WaitAsync();
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return new PasswordResetTokenValidationResult { IsValid = false };
            }

            var users = await ReadUsersUnsafeAsync();
            var tokens = await ReadPasswordResetTokensUnsafeAsync();
            var now = DateTimeOffset.UtcNow;
            tokens.RemoveAll(existingToken => existingToken.ExpiresAtUtc <= now);

            var tokenHash = HashToken(token.Trim());
            var matchingToken = tokens.FirstOrDefault(existingToken => existingToken.TokenHash == tokenHash);
            if (matchingToken is null)
            {
                await WritePasswordResetTokensUnsafeAsync(tokens);
                return new PasswordResetTokenValidationResult { IsValid = false };
            }

            var user = users.FirstOrDefault(existingUser => existingUser.UserId == matchingToken.UserId);
            if (user is null)
            {
                return new PasswordResetTokenValidationResult { IsValid = false };
            }

            await WritePasswordResetTokensUnsafeAsync(tokens);
            return new PasswordResetTokenValidationResult
            {
                IsValid = true,
                Username = user.Username,
                ExpiresAtUtc = matchingToken.ExpiresAtUtc
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserAccountSummary> DeleteUserAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            var record = users.FirstOrDefault(user => user.UserId == userId)
                ?? throw new InvalidOperationException("User not found.");

            var firstUser = users
                .OrderBy(user => user.CreatedAtUtc)
                .FirstOrDefault();

            if (firstUser is not null && firstUser.UserId == userId)
            {
                throw new InvalidOperationException("The first account cannot be removed.");
            }

            users.RemoveAll(user => user.UserId == userId);
            await WriteUsersUnsafeAsync(users);
            return ToSummary(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<CredentialRecord>> ReadUsersUnsafeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);

        if (!File.Exists(_credentialFilePath))
        {
            return [];
        }

        var protectedPayload = await File.ReadAllTextAsync(_credentialFilePath);
        if (string.IsNullOrWhiteSpace(protectedPayload))
        {
            return [];
        }

        string json;
        try
        {
            json = _protector.Unprotect(protectedPayload);
        }
        catch (Exception)
        {
            QuarantineCorruptFileUnsafe(_credentialFilePath, "user.cred");
            throw new CorruptedDataException("Credential data is corrupted and has been quarantined. Restore App_Data from backup.");
        }

        var document = TryDeserialize<CredentialDocument>(json);
        if (document?.Users is { Count: > 0 })
        {
            return document.Users;
        }

        var users = TryDeserialize<List<CredentialRecord>>(json);
        if (users is { Count: > 0 })
        {
            return users;
        }

        var legacy = TryDeserialize<CredentialRecord>(json);
        if (legacy is null)
        {
            QuarantineCorruptFileUnsafe(_credentialFilePath, "user.cred");
            throw new CorruptedDataException("Credential data is corrupted and has been quarantined. Restore App_Data from backup.");
        }

        if (legacy.UserId == Guid.Empty)
        {
            legacy.UserId = Guid.NewGuid();
        }

        if (legacy.CreatedAtUtc == default)
        {
            legacy.CreatedAtUtc = DateTimeOffset.UtcNow;
        }

        legacy.IsAdmin = true;
        return [legacy];
    }

    private async Task<string?> ReadProtectedCredentialJsonUnsafeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);

        if (!File.Exists(_credentialFilePath))
        {
            return null;
        }

        var protectedPayload = await File.ReadAllTextAsync(_credentialFilePath);
        if (string.IsNullOrWhiteSpace(protectedPayload))
        {
            return null;
        }

        return _protector.Unprotect(protectedPayload);
    }

    private async Task<List<PasswordResetTokenRecord>> ReadPasswordResetTokensUnsafeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);

        if (!File.Exists(_passwordResetTokenFilePath))
        {
            return [];
        }

        var protectedPayload = await File.ReadAllTextAsync(_passwordResetTokenFilePath);
        if (string.IsNullOrWhiteSpace(protectedPayload))
        {
            return [];
        }

        string json;
        try
        {
            json = _protector.Unprotect(protectedPayload);
        }
        catch (Exception)
        {
            QuarantineCorruptFileUnsafe(_passwordResetTokenFilePath, "password-reset-tokens.dat");
            throw new CorruptedDataException("Password reset token data is corrupted and has been quarantined. Restore App_Data from backup.");
        }

        var tokenDocument = TryDeserialize<PasswordResetTokenDocument>(json);
        if (tokenDocument?.Tokens is { Count: >= 0 })
        {
            return tokenDocument.Tokens;
        }

        var tokens = TryDeserialize<List<PasswordResetTokenRecord>>(json);
        return tokens ?? [];
    }

    private async Task WritePasswordResetTokensUnsafeAsync(List<PasswordResetTokenRecord> tokens)
    {
        Directory.CreateDirectory(_dataDirectory);

        var json = JsonSerializer.Serialize(new PasswordResetTokenDocument { Tokens = tokens }, JsonOptions);
        var protectedPayload = _protector.Protect(json);
        await File.WriteAllTextAsync(_passwordResetTokenFilePath, protectedPayload);
    }

    private async Task WriteUsersUnsafeAsync(List<CredentialRecord> users)
    {
        Directory.CreateDirectory(_dataDirectory);

        var json = JsonSerializer.Serialize(new CredentialDocument { Users = users }, JsonOptions);
        var protectedPayload = _protector.Protect(json);
        await File.WriteAllTextAsync(_credentialFilePath, protectedPayload);
    }

    private static UserAccountSummary ToSummary(CredentialRecord record)
    {
        return new UserAccountSummary
        {
            UserId = record.UserId,
            Username = record.Username,
            IsAdmin = record.IsAdmin,
            IsDisabled = record.IsDisabled,
            ForcePasswordReset = record.ForcePasswordReset,
            IsLocked = record.IsLocked,
            LockedUntilUtc = record.LockedUntilUtc,
            CreatedAtUtc = record.CreatedAtUtc
        };
    }

    private void QuarantineCorruptFileUnsafe(string sourcePath, string fileNamePrefix)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        Directory.CreateDirectory(_corruptDataDirectory);
        var quarantineName = $"{fileNamePrefix}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.corrupt";
        var quarantinePath = Path.Combine(_corruptDataDirectory, quarantineName);
        File.Move(sourcePath, quarantinePath, overwrite: true);
    }

    private static T? TryDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string GenerateResetToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static bool IsCurrentCredentialDocument(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Users", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}