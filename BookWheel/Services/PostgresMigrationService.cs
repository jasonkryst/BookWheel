using BookWheel.Models;
using BookWheel.Storage;
using BookWheel.Storage.Postgres;
using BookWheel.Storage.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Services;

public sealed class PostgresMigrationService
{
    private readonly DataMigrationService _legacyJsonMigrationService;
    private readonly JsonCredentialRepository _jsonCredentialRepository;
    private readonly JsonBookRepository _jsonBookRepository;
    private readonly JsonPasswordResetTokenRepository _jsonPasswordResetTokenRepository;
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public PostgresMigrationService(
        DataMigrationService legacyJsonMigrationService,
        JsonCredentialRepository jsonCredentialRepository,
        JsonBookRepository jsonBookRepository,
        JsonPasswordResetTokenRepository jsonPasswordResetTokenRepository,
        IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _legacyJsonMigrationService = legacyJsonMigrationService;
        _jsonCredentialRepository = jsonCredentialRepository;
        _jsonBookRepository = jsonBookRepository;
        _jsonPasswordResetTokenRepository = jsonPasswordResetTokenRepository;
        _contextFactory = contextFactory;
    }

    public async Task<PostgresMigrationReport> RunAsync()
    {
        await _legacyJsonMigrationService.RunAsync();

        var users = await _jsonCredentialRepository.GetAllForMigrationAsync();
        var booksByUser = await _jsonBookRepository.GetAllForMigrationAsync();
        var tokens = await _jsonPasswordResetTokenRepository.GetAllForMigrationAsync();

        await using var context = await _contextFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();

        if (await context.Users.AnyAsync())
        {
            throw new InvalidOperationException(
                "PostgreSQL already contains user data. Refusing to overwrite an existing migration target.");
        }

        foreach (var user in users)
        {
            context.Users.Add(new UserEntity
            {
                Id = user.UserId,
                Username = user.Username,
                PasswordHash = user.PasswordHash,
                IsAdmin = user.IsAdmin,
                IsDisabled = user.IsDisabled,
                ForcePasswordReset = user.ForcePasswordReset,
                IsLocked = user.IsLocked,
                LockedUntilUtc = user.LockedUntilUtc,
                CreatedAtUtc = user.CreatedAtUtc
            });
        }

        var booksMigrated = 0;
        foreach (var (userIdKey, books) in booksByUser)
        {
            if (!Guid.TryParse(userIdKey, out var userId))
            {
                continue;
            }

            foreach (var book in books)
            {
                context.Books.Add(new BookEntity { Id = book.Id, UserId = userId, Title = book.Title });
                booksMigrated++;
            }
        }

        foreach (var token in tokens)
        {
            context.PasswordResetTokens.Add(new PasswordResetTokenEntity
            {
                Id = Guid.NewGuid(),
                UserId = token.UserId,
                TokenHash = token.TokenHash,
                CreatedAtUtc = token.CreatedAtUtc,
                ExpiresAtUtc = token.ExpiresAtUtc
            });
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return new PostgresMigrationReport
        {
            ExecutedAtUtc = DateTimeOffset.UtcNow,
            UsersMigrated = users.Count,
            BooksMigrated = booksMigrated,
            PasswordResetTokensMigrated = tokens.Count,
            Message = users.Count == 0 && booksMigrated == 0 && tokens.Count == 0
                ? "No legacy file data found to migrate."
                : "Legacy file data migrated to PostgreSQL."
        };
    }
}
