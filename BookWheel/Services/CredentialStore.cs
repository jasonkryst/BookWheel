using System.Text.Json;
using BookWheel.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace BookWheel.Services;

public sealed class CredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly PasswordHasher<string> PasswordHasher = new();

    private readonly string _dataDirectory;
    private readonly string _credentialFilePath;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CredentialStore(IWebHostEnvironment environment, IDataProtectionProvider dataProtectionProvider)
    {
        _dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        _credentialFilePath = Path.Combine(_dataDirectory, "user.cred");
        _protector = dataProtectionProvider.CreateProtector("BookWheel.Credentials.v1");
    }

    public async Task<bool> HasAccountAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return File.Exists(_credentialFilePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CreateAccountAsync(string username, string password)
    {
        await _gate.WaitAsync();
        try
        {
            if (File.Exists(_credentialFilePath))
            {
                throw new InvalidOperationException("An account already exists.");
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Username and password are required.");
            }

            var record = new CredentialRecord
            {
                Username = username.Trim(),
                PasswordHash = PasswordHasher.HashPassword(username.Trim(), password),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            await WriteUnsafeAsync(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ValidateCredentialsAsync(string username, string password)
    {
        await _gate.WaitAsync();
        try
        {
            var record = await ReadUnsafeAsync();
            if (record is null)
            {
                return false;
            }

            if (!string.Equals(username.Trim(), record.Username, StringComparison.Ordinal))
            {
                return false;
            }

            var result = PasswordHasher.VerifyHashedPassword(record.Username, record.PasswordHash, password);
            return result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CredentialRecord?> ReadUnsafeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);

        if (!File.Exists(_credentialFilePath))
        {
            return null;
        }

        var protectedPayload = await File.ReadAllTextAsync(_credentialFilePath);
        var json = _protector.Unprotect(protectedPayload);
        return JsonSerializer.Deserialize<CredentialRecord>(json, JsonOptions);
    }

    private async Task WriteUnsafeAsync(CredentialRecord record)
    {
        Directory.CreateDirectory(_dataDirectory);

        var json = JsonSerializer.Serialize(record, JsonOptions);
        var protectedPayload = _protector.Protect(json);
        await File.WriteAllTextAsync(_credentialFilePath, protectedPayload);
    }
}