using System.Text.Json;
using System.Text.Json.Serialization;
using BookWheel.Models;

namespace BookWheel.Services;

public sealed class BookStore
{
    private const int CurrentBookSchemaVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dataDirectory;
    private readonly string _corruptDataDirectory;
    private readonly string _dataFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed class BookStoreDocument
    {
        public int SchemaVersion { get; set; } = CurrentBookSchemaVersion;
        public Dictionary<string, List<BookRecord>> Users { get; set; } = [];
    }

    public sealed class BookMigrationResult
    {
        public bool Migrated { get; set; }
        public int BooksAffected { get; set; }
        public Guid? BooksOwnerUserId { get; set; }
    }

    public BookStore(IWebHostEnvironment environment)
    {
        _dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        _corruptDataDirectory = Path.Combine(_dataDirectory, "corrupt");
        _dataFilePath = Path.Combine(_dataDirectory, "books.json");
    }

    public async Task<bool> HasLegacyPayloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var raw = await ReadRawUnsafeAsync();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            if (TryDeserialize<Dictionary<string, List<BookRecord>>>(raw) is not null)
            {
                return false;
            }

            return TryDeserialize<List<BookRecord>>(raw) is not null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BookMigrationResult> MigrateLegacyPayloadAsync(Guid? ownerUserId)
    {
        await _gate.WaitAsync();
        try
        {
            var raw = await ReadRawUnsafeAsync();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new BookMigrationResult();
            }

            var storeDocument = TryDeserialize<BookStoreDocument>(raw);
            var booksByUser = storeDocument?.Users;
            if (booksByUser is null)
            {
                booksByUser = TryDeserialize<Dictionary<string, List<BookRecord>>>(raw);
            }
            if (booksByUser is not null)
            {
                if (!booksByUser.TryGetValue("legacy-unassigned", out var unassignedBooks))
                {
                    return new BookMigrationResult();
                }

                var owner = ownerUserId ?? Guid.Empty;
                booksByUser.Remove("legacy-unassigned");
                booksByUser[owner.ToString()] = unassignedBooks;
                await WriteStoreUnsafeAsync(booksByUser);

                return new BookMigrationResult
                {
                    Migrated = true,
                    BooksAffected = unassignedBooks.Count,
                    BooksOwnerUserId = owner
                };
            }

            var legacyBooks = TryDeserialize<List<BookRecord>>(raw);
            if (legacyBooks is null)
            {
                return new BookMigrationResult();
            }

            var targetOwner = ownerUserId ?? Guid.Empty;
            var migratedStore = new Dictionary<string, List<BookRecord>>
            {
                [targetOwner.ToString()] = legacyBooks
            };

            await WriteStoreUnsafeAsync(migratedStore);

            return new BookMigrationResult
            {
                Migrated = true,
                BooksAffected = legacyBooks.Count,
                BooksOwnerUserId = targetOwner
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<BookRecord>> GetAllAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            return GetBooksForUser(booksByUser, userId).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BookRecord> AddAsync(Guid userId, string title)
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            var books = GetBooksForUser(booksByUser, userId);
            var record = new BookRecord
            {
                Id = Guid.NewGuid(),
                Title = title.Trim()
            };

            books.Add(record);
            await WriteStoreUnsafeAsync(booksByUser);
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BookRecord> UpdateAsync(Guid userId, Guid id, string title)
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            var books = GetBooksForUser(booksByUser, userId);
            var book = books.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Book not found.");
            book.Title = title.Trim();
            await WriteStoreUnsafeAsync(booksByUser);
            return book;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BookRecord> SelectRandomAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            var books = GetBooksForUser(booksByUser, userId);
            if (books.Count == 0)
            {
                throw new InvalidOperationException("No books are available in the wheel.");
            }

            var selected = books[Random.Shared.Next(books.Count)];
            return selected;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BookRecord> RemoveAsync(Guid userId, Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            var books = GetBooksForUser(booksByUser, userId);
            var book = books.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Book not found.");
            books.RemoveAll(x => x.Id == id);
            await WriteStoreUnsafeAsync(booksByUser);
            return book;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RemoveUserDataAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            var userKey = userId.ToString();
            if (!booksByUser.TryGetValue(userKey, out var userBooks))
            {
                return 0;
            }

            var removedCount = userBooks.Count;
            booksByUser.Remove(userKey);
            await WriteStoreUnsafeAsync(booksByUser);
            return removedCount;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> GetTotalBookCountAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            return booksByUser.Values.Sum(books => books.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, List<BookRecord>>> ReadStoreUnsafeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);

        if (!File.Exists(_dataFilePath))
        {
            await File.WriteAllTextAsync(_dataFilePath, "{}");
            return [];
        }

        var raw = await ReadRawUnsafeAsync();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var storeDocument = TryDeserialize<BookStoreDocument>(raw);
        var booksByUser = storeDocument?.Users;
        if (booksByUser is null)
        {
            booksByUser = TryDeserialize<Dictionary<string, List<BookRecord>>>(raw);
        }
        if (booksByUser is not null)
        {
            return booksByUser;
        }

        var legacyBooks = TryDeserialize<List<BookRecord>>(raw);
        if (legacyBooks is not null)
        {
            return [];
        }

        QuarantineCorruptBooksUnsafe();
        throw new CorruptedDataException("Book data is corrupted and has been quarantined. Restore App_Data from backup.");
    }

    private async Task WriteStoreUnsafeAsync(Dictionary<string, List<BookRecord>> booksByUser)
    {
        var migrated = booksByUser
            .Where(pair => !string.Equals(pair.Key, "legacy-unassigned", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        await using var stream = File.Open(_dataFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        var document = new BookStoreDocument { Users = migrated };
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions);
    }

    private static List<BookRecord> GetBooksForUser(Dictionary<string, List<BookRecord>> booksByUser, Guid userId)
    {
        var userKey = userId.ToString();
        if (!booksByUser.TryGetValue(userKey, out var books))
        {
            books = [];
            booksByUser[userKey] = books;
        }

        return books;
    }

    private async Task<string?> ReadRawUnsafeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);

        if (!File.Exists(_dataFilePath))
        {
            await File.WriteAllTextAsync(_dataFilePath, "{}");
            return null;
        }

        return await File.ReadAllTextAsync(_dataFilePath);
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

    private void QuarantineCorruptBooksUnsafe()
    {
        if (!File.Exists(_dataFilePath))
        {
            return;
        }

        Directory.CreateDirectory(_corruptDataDirectory);
        var quarantineName = $"books.json-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.corrupt";
        var quarantinePath = Path.Combine(_corruptDataDirectory, quarantineName);
        File.Move(_dataFilePath, quarantinePath, overwrite: true);
        File.WriteAllText(_dataFilePath, "{}");
    }
}
