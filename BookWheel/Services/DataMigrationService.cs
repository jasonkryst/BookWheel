using BookWheel.Models;
using BookWheel.Storage;

namespace BookWheel.Services;

public sealed class DataMigrationService
{
    private readonly CredentialStore _credentialStore;
    private readonly JsonBookRepository _bookRepository;

    public DataMigrationService(CredentialStore credentialStore, JsonBookRepository bookRepository)
    {
        _credentialStore = credentialStore;
        _bookRepository = bookRepository;
    }

    public async Task<DataMigrationStatus> GetStatusAsync()
    {
        return new DataMigrationStatus
        {
            HasLegacyCredentialPayload = await _credentialStore.HasLegacyPayloadAsync(),
            HasLegacyBooksPayload = await _bookRepository.HasLegacyPayloadAsync()
        };
    }

    public async Task<DataMigrationReport> RunAsync()
    {
        var credentials = await _credentialStore.MigrateLegacyPayloadAsync();
        var users = await _credentialStore.GetUsersAsync();
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
