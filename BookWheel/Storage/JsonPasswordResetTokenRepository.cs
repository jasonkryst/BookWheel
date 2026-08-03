using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using BookWheel.Models;
using BookWheel.Services;
using Microsoft.AspNetCore.DataProtection;

namespace BookWheel.Storage;

public sealed class JsonPasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private const int CurrentResetTokenSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _dataDirectory;
    private readonly string _corruptDataDirectory;
    private readonly string _passwordResetTokenFilePath;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed class PasswordResetTokenDocument
    {
        public int SchemaVersion { get; set; } = CurrentResetTokenSchemaVersion;
        public List<PasswordResetTokenRecord> Tokens { get; set; } = [];
    }

    public JsonPasswordResetTokenRepository(IWebHostEnvironment environment, IDataProtectionProvider dataProtectionProvider)
    {
        _dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        _corruptDataDirectory = Path.Combine(_dataDirectory, "corrupt");
        _passwordResetTokenFilePath = Path.Combine(_dataDirectory, "password-reset-tokens.dat");
        _protector = dataProtectionProvider.CreateProtector("BookWheel.Credentials.v1");
    }

    public async Task<(string RawToken, DateTimeOffset ExpiresAtUtc)> CreateAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            var tokens = await ReadTokensUnsafeAsync();
            var now = DateTimeOffset.UtcNow;
            tokens.RemoveAll(existingToken => existingToken.ExpiresAtUtc <= now || existingToken.UserId == userId);

            var rawToken = GenerateResetToken();
            var expiresAtUtc = now.AddHours(24);
            tokens.Add(new PasswordResetTokenRecord
            {
                UserId = userId,
                TokenHash = HashToken(rawToken),
                CreatedAtUtc = now,
                ExpiresAtUtc = expiresAtUtc
            });

            await WriteTokensUnsafeAsync(tokens);
            return (rawToken, expiresAtUtc);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PasswordResetTokenLookup> ValidateAsync(string token)
    {
        await _gate.WaitAsync();
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return new PasswordResetTokenLookup { IsValid = false };
            }

            var tokens = await ReadTokensUnsafeAsync();
            var now = DateTimeOffset.UtcNow;
            tokens.RemoveAll(existingToken => existingToken.ExpiresAtUtc <= now);

            var tokenHash = HashToken(token.Trim());
            var matchingToken = tokens.FirstOrDefault(existingToken => existingToken.TokenHash == tokenHash);

            await WriteTokensUnsafeAsync(tokens);

            return matchingToken is null
                ? new PasswordResetTokenLookup { IsValid = false }
                : new PasswordResetTokenLookup
                {
                    IsValid = true,
                    UserId = matchingToken.UserId,
                    ExpiresAtUtc = matchingToken.ExpiresAtUtc
                };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Guid> CompleteAsync(string token)
    {
        await _gate.WaitAsync();
        try
        {
            var tokens = await ReadTokensUnsafeAsync();
            var now = DateTimeOffset.UtcNow;
            tokens.RemoveAll(existingToken => existingToken.ExpiresAtUtc <= now);

            var tokenHash = HashToken((token ?? string.Empty).Trim());
            var matchingToken = tokens.FirstOrDefault(existingToken => existingToken.TokenHash == tokenHash)
                ?? throw new InvalidOperationException("The password reset link is invalid or has expired.");

            tokens.RemoveAll(existingToken => existingToken.TokenHash == tokenHash);
            await WriteTokensUnsafeAsync(tokens);

            return matchingToken.UserId;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<PasswordResetTokenRecord>> ReadTokensUnsafeAsync()
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

    private async Task WriteTokensUnsafeAsync(List<PasswordResetTokenRecord> tokens)
    {
        Directory.CreateDirectory(_dataDirectory);

        var json = JsonSerializer.Serialize(new PasswordResetTokenDocument { Tokens = tokens }, JsonOptions);
        var protectedPayload = _protector.Protect(json);
        await File.WriteAllTextAsync(_passwordResetTokenFilePath, protectedPayload);
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
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
