using System.Text.Json;
using System.Text.Json.Serialization;
using BookWheel.Models;

namespace BookWheel.Services;

public sealed class BookStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dataDirectory;
    private readonly string _dataFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BookStore(IWebHostEnvironment environment)
    {
        _dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        _dataFilePath = Path.Combine(_dataDirectory, "books.json");
    }

    public async Task<IReadOnlyList<BookRecord>> GetAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return (await ReadAllUnsafeAsync()).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BookRecord> AddAsync(string title)
    {
        await _gate.WaitAsync();
        try
        {
            var books = await ReadAllUnsafeAsync();
            var record = new BookRecord
            {
                Id = Guid.NewGuid(),
                Title = title.Trim()
            };

            books.Add(record);
            await WriteAllUnsafeAsync(books);
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BookRecord> UpdateAsync(Guid id, string title)
    {
        await _gate.WaitAsync();
        try
        {
            var books = await ReadAllUnsafeAsync();
            var book = books.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Book not found.");
            book.Title = title.Trim();
            await WriteAllUnsafeAsync(books);
            return book;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BookRecord> SelectRandomAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var books = await ReadAllUnsafeAsync();
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

    public async Task<BookRecord> RemoveAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            var books = await ReadAllUnsafeAsync();
            var book = books.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Book not found.");
            books.RemoveAll(x => x.Id == id);
            await WriteAllUnsafeAsync(books);
            return book;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<BookRecord>> ReadAllUnsafeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);

        if (!File.Exists(_dataFilePath))
        {
            await File.WriteAllTextAsync(_dataFilePath, "[]");
        }

        await using var stream = File.Open(_dataFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var books = await JsonSerializer.DeserializeAsync<List<BookRecord>>(stream, JsonOptions);
        return books ?? [];
    }

    private async Task WriteAllUnsafeAsync(List<BookRecord> books)
    {
        await using var stream = File.Open(_dataFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, books, JsonOptions);
    }
}
