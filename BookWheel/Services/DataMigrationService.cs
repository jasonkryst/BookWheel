using BookWheel.Models;
using BookWheel.Storage;

namespace BookWheel.Services;

public sealed class DataMigrationService
{
    private readonly JsonCredentialRepository _credentialRepository;
    private readonly JsonBookRepository _bookRepository;

    public DataMigrationService(JsonCredentialRepository credentialRepository, JsonBookRepository bookRepository)
    {
        _credentialRepository = credentialRepository;
        _bookRepository = bookRepository;
    }

    public async Task<DataMigrationStatus> GetStatusAsync()
    {
        return new DataMigrationStatus
        {
            HasLegacyCredentialPayload = await _credentialRepository.HasLegacyPayloadAsync(),
            HasLegacyBooksPayload = await _bookRepository.HasLegacyPayloadAsync()
        };
    }

    public async Task<DataMigrationReport> RunAsync()
    {
        var credentials = await _credentialRepository.MigrateLegacyPayloadAsync();
        var users = await _credentialRepository.GetUsersAsync();
        var booksOwnerId = users.OrderBy(user => user.CreatedAtUtc).Select(user => user.UserId).FirstOrDefault();
        var resolvedOwner = booksOwnerId == Guid.Empty ? (Guid?)null : booksOwnerId;
        var books = await _bookRepository.MigrateLegacyPayloadAsync(resolvedOwner);

        return new DataMigrationReport
        {
            ExecutedAtUtc = DateTimeOffset.UtcNow,
            CredentialPayloadMigrated = credentials.Migrated,
            CredentialUsersAffected = credentials.UsersAffected,
            BooksPayloadMigrated = books.Migrated,
            BooksAffected = books.BooksAffected,
            BooksOwnerUserId = books.BooksOwnerUserId,
            Message = !credentials.Migrated && !books.Migrated
                ? "No legacy payloads required migration."
                : "Legacy payload migration completed."
        };
    }
}
