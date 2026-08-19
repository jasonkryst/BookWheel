using System.Security.Cryptography;
using BookWheel.Models;
using BookWheel.Storage.Postgres.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookWheel.Storage.Postgres;

public sealed class PostgresCredentialRepository : ICredentialRepository
{
    private static readonly PasswordHasher<string> PasswordHasher = new();
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public PostgresCredentialRepository(IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<bool> HasAccountAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Users.AnyAsync();
    }

    public async Task<CredentialRecord> CreateInitialAccountAsync(string username, string password)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        if (await context.Users.AnyAsync())
        {
            throw new InvalidOperationException("An account already exists.");
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Username and password are required.");
        }

        var normalizedUsername = username.Trim();
        var entity = new UserEntity
        {
            Id = Guid.NewGuid(),
            Username = normalizedUsername,
            PasswordHash = PasswordHasher.HashPassword(normalizedUsername, password),
            IsAdmin = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        context.Users.Add(entity);
        await context.SaveChangesAsync();
        return ToRecord(entity);
    }

    public async Task<CredentialRecord?> ValidateCredentialsAsync(string username, string password)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var normalizedUsername = username.Trim();
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Username == normalizedUsername);
        if (entity is null)
        {
            return null;
        }

        var result = PasswordHasher.VerifyHashedPassword(entity.Username, entity.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded
            ? ToRecord(entity)
            : null;
    }

    public async Task<IReadOnlyList<UserAccountSummary>> GetUsersAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Users
            .OrderBy(u => u.CreatedAtUtc)
            .Select(u => new UserAccountSummary
            {
                UserId = u.Id,
                Username = u.Username,
                IsAdmin = u.IsAdmin,
                IsDisabled = u.IsDisabled,
                ForcePasswordReset = u.ForcePasswordReset,
                IsLocked = u.IsLocked,
                LockedUntilUtc = u.LockedUntilUtc,
                CreatedAtUtc = u.CreatedAtUtc
            })
            .ToListAsync();
    }

    public async Task<UserAccountSummary> CreateUserAsync(string username, bool isAdmin)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        if (!await context.Users.AnyAsync())
        {
            throw new InvalidOperationException("Create the initial account first.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Username is required.");
        }

        var normalizedUsername = username.Trim();
        if (await context.Users.AnyAsync(u => u.Username == normalizedUsername))
        {
            throw new InvalidOperationException("Username already exists.");
        }

        var entity = new UserEntity
        {
            Id = Guid.NewGuid(),
            Username = normalizedUsername,
            PasswordHash = PasswordHasher.HashPassword(normalizedUsername, GenerateTemporaryPassword()),
            IsAdmin = isAdmin,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        context.Users.Add(entity);

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Username already exists.");
        }

        return ToSummary(entity);
    }

    public Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin)
    {
        return UpdateUserCoreAsync(userId, username, isAdmin, isDisabled: null, forcePasswordReset: null, isLocked: null);
    }

    public Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin, bool isDisabled, bool forcePasswordReset, bool isLocked)
    {
        return UpdateUserCoreAsync(userId, username, isAdmin, isDisabled, forcePasswordReset, isLocked);
    }

    private async Task<UserAccountSummary> UpdateUserCoreAsync(Guid userId, string username, bool isAdmin, bool? isDisabled, bool? forcePasswordReset, bool? isLocked)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");

        var normalizedUsername = username.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            throw new InvalidOperationException("Username is required.");
        }

        var duplicateExists = await context.Users.AnyAsync(u => u.Id != userId && u.Username == normalizedUsername);
        if (duplicateExists)
        {
            throw new InvalidOperationException("Username already exists.");
        }

        if (!isAdmin)
        {
            var adminCount = await context.Users.CountAsync(u => u.IsAdmin);
            if (entity.IsAdmin && adminCount <= 1)
            {
                throw new InvalidOperationException("At least one administrator account is required.");
            }
        }

        entity.Username = normalizedUsername;
        entity.IsAdmin = isAdmin;

        if (isDisabled.HasValue)
        {
            entity.IsDisabled = isDisabled.Value;
        }

        if (forcePasswordReset.HasValue)
        {
            entity.ForcePasswordReset = forcePasswordReset.Value;
        }

        if (isLocked.HasValue)
        {
            entity.IsLocked = isLocked.Value;
            entity.LockedUntilUtc = isLocked.Value ? DateTimeOffset.UtcNow.AddHours(12) : null;
        }

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Username already exists.");
        }

        return ToSummary(entity);
    }

    public async Task<UserAccountSummary> DeleteUserAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");

        var firstUserId = await context.Users
            .OrderBy(u => u.CreatedAtUtc)
            .Select(u => u.Id)
            .FirstOrDefaultAsync();

        if (firstUserId == userId)
        {
            throw new InvalidOperationException("The first account cannot be removed.");
        }

        context.Users.Remove(entity);
        await context.SaveChangesAsync();
        return ToSummary(entity);
    }

    public async Task<CredentialRecord> MarkForPasswordResetAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");

        entity.ForcePasswordReset = true;
        entity.IsLocked = false;
        entity.LockedUntilUtc = null;

        await context.SaveChangesAsync();
        return ToRecord(entity);
    }

    public async Task<string> SetPasswordAsync(Guid userId, string newPassword)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found for this reset link.");

        entity.PasswordHash = PasswordHasher.HashPassword(entity.Username, newPassword);
        entity.ForcePasswordReset = false;
        entity.IsLocked = false;
        entity.LockedUntilUtc = null;

        await context.SaveChangesAsync();
        return entity.Username;
    }

    public async Task<string?> GetUsernameAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Users.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync();
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }

    private static CredentialRecord ToRecord(UserEntity entity) => new()
    {
        UserId = entity.Id,
        Username = entity.Username,
        PasswordHash = entity.PasswordHash,
        IsAdmin = entity.IsAdmin,
        IsDisabled = entity.IsDisabled,
        ForcePasswordReset = entity.ForcePasswordReset,
        IsLocked = entity.IsLocked,
        LockedUntilUtc = entity.LockedUntilUtc,
        CreatedAtUtc = entity.CreatedAtUtc
    };

    private static UserAccountSummary ToSummary(UserEntity entity) => new()
    {
        UserId = entity.Id,
        Username = entity.Username,
        IsAdmin = entity.IsAdmin,
        IsDisabled = entity.IsDisabled,
        ForcePasswordReset = entity.ForcePasswordReset,
        IsLocked = entity.IsLocked,
        LockedUntilUtc = entity.LockedUntilUtc,
        CreatedAtUtc = entity.CreatedAtUtc
    };

    private static string GenerateTemporaryPassword()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    }
}
