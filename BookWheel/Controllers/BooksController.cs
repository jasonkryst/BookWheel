using System.Globalization;
using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BookWheel.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class BooksController : ControllerBase
{
    private readonly AppMetricsService _metricsService;
    private readonly IBookRepository _store;
    private readonly ISpinHistoryRepository _spinHistory;
    private readonly ApiMessageLocalizer _errors;
    private readonly BookMetadataLookupDispatcher _metadataLookup;
    private readonly BookSearchLinksService _searchLinks;
    private readonly IUserPreferencesRepository _preferencesRepository;
    private readonly IOptionsSnapshot<BookMetadataOptions> _metadataOptions;

    public BooksController(
        AppMetricsService metricsService,
        IBookRepository store,
        ISpinHistoryRepository spinHistory,
        ApiMessageLocalizer errors,
        BookMetadataLookupDispatcher metadataLookup,
        BookSearchLinksService searchLinks,
        IUserPreferencesRepository preferencesRepository,
        IOptionsSnapshot<BookMetadataOptions> metadataOptions)
    {
        _metricsService = metricsService;
        _store = store;
        _spinHistory = spinHistory;
        _errors = errors;
        _metadataLookup = metadataLookup;
        _searchLinks = searchLinks;
        _preferencesRepository = preferencesRepository;
        _metadataOptions = metadataOptions;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId = User.GetUserId();

        try
        {
            var books = await _store.GetAllAsync(userId);
            return Ok(new
            {
                books,
                activeBooks = books
            });
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = _errors.Localize(ex.Message) });
        }
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var userId = User.GetUserId();

        try
        {
            var books = await _store.GetAllForExportAsync(userId);
            var history = await _spinHistory.GetForUserAsync(userId);

            return Ok(new
            {
                schemaVersion = 1,
                exportedAtUtc = DateTimeOffset.UtcNow,
                account = new { username = User.GetUsername() },
                books = books.Select(book => new
                {
                    title = book.Title,
                    isbn = book.Isbn,
                    author = book.Author,
                    coverUrl = book.CoverUrl,
                    deletedAtUtc = book.DeletedAtUtc,
                    bookTypeId = book.BookTypeId
                }),
                spinHistory = history.Select(entry => new
                {
                    title = entry.Title,
                    selectedAtUtc = entry.SelectedAtUtc
                })
            });
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = _errors.Localize(ex.Message) });
        }
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] ImportBooksRequest request)
    {
        var userId = User.GetUserId();

        if (request.Books is null || request.Books.Count == 0)
        {
            return BadRequest(new { message = _errors.Localize("No books were provided to import.") });
        }

        try
        {
            var added = 0;
            var restored = 0;
            var skipped = 0;
            var warnings = 0;

            foreach (var item in request.Books)
            {
                // Exports include removed books with a deletedAtUtc stamp; importing must not
                // put them back on the wheel as active, so skip them entirely.
                if (item.DeletedAtUtc is not null)
                {
                    skipped += 1;
                    continue;
                }

                var title = item.Title?.Trim();
                if (string.IsNullOrEmpty(title))
                {
                    continue;
                }

                string? normalizedIsbn = null;
                if (!string.IsNullOrWhiteSpace(item.Isbn))
                {
                    IsbnValidator.TryNormalize(item.Isbn, out normalizedIsbn);
                    normalizedIsbn = string.IsNullOrEmpty(normalizedIsbn) ? null : normalizedIsbn;
                }

                var normalizedTitle = NormalizeTitleForMatch(title);

                if (await _store.FindActiveByTitleOrIsbnAsync(userId, normalizedTitle, normalizedIsbn) is not null)
                {
                    skipped += 1;
                    continue;
                }

                var removed = await _store.FindDeletedByTitleOrIsbnAsync(userId, normalizedTitle, normalizedIsbn);
                if (removed is not null)
                {
                    await _store.RestoreAsync(userId, removed.Id);
                    restored += 1;
                    continue;
                }

                var bookTypeId = item.BookTypeId ?? 1;
                if (item.BookTypeId is < 1 or > 3)
                {
                    bookTypeId = 1;
                    warnings += 1;
                }

                await _store.AddAsync(userId, title, normalizedIsbn, NormalizeOptional(item.Author), NormalizeOptional(item.CoverUrl), bookTypeId: bookTypeId);
                added += 1;
            }

            return Ok(new { added, restored, skipped, warnings });
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = _errors.Localize(ex.Message) });
        }
    }

    private static string NormalizeTitleForMatch(string title) => title.Trim().ToLowerInvariant();

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] UpdateBookRequest request)
    {
        var userId = User.GetUserId();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { message = _errors.Localize("Book title is required.") });
        }

        string? normalizedIsbn = null;
        if (!string.IsNullOrWhiteSpace(request.Isbn) && !IsbnValidator.TryNormalize(request.Isbn, out normalizedIsbn))
        {
            return BadRequest(new { message = _errors.Localize("The provided ISBN is not valid.") });
        }

        try
        {
            if (!request.AllowDuplicate)
            {
                var normalizedTitle = NormalizeTitleForMatch(request.Title);

                var removedMatch = await _store.FindDeletedByTitleOrIsbnAsync(userId, normalizedTitle, normalizedIsbn);
                if (removedMatch is not null)
                {
                    return Conflict(new { reason = "removed", match = removedMatch, message = _errors.Localize("You removed this book before.") });
                }

                var activeMatch = await _store.FindActiveByTitleOrIsbnAsync(userId, normalizedTitle, normalizedIsbn);
                if (activeMatch is not null)
                {
                    return Conflict(new { reason = "exact", match = activeMatch, message = _errors.Localize("This book is already on your wheel.") });
                }
            }

            var book = await _store.AddAsync(userId, request.Title, normalizedIsbn, NormalizeOptional(request.Author), NormalizeOptional(request.CoverUrl), request.AddedByScanner, request.BookTypeId, bookInfoProviderId: request.BookInfoProviderId);
            return Ok(book);
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = _errors.Localize(ex.Message) });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBookRequest request)
    {
        var userId = User.GetUserId();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { message = _errors.Localize("Book title is required.") });
        }

        string? normalizedIsbn = null;
        if (!string.IsNullOrWhiteSpace(request.Isbn) && !IsbnValidator.TryNormalize(request.Isbn, out normalizedIsbn))
        {
            return BadRequest(new { message = _errors.Localize("The provided ISBN is not valid.") });
        }

        try
        {
            var book = await _store.UpdateAsync(userId, id, request.Title, normalizedIsbn, NormalizeOptional(request.Author), NormalizeOptional(request.CoverUrl), request.BookTypeId, bookInfoProviderId: request.BookInfoProviderId);
            return Ok(book);
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = _errors.Localize(ex.Message) });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = _errors.Localize(ex.Message) });
        }
    }

    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] string? isbn, [FromQuery] string? title)
    {
        var userId = User.GetUserId();

        if (string.IsNullOrWhiteSpace(isbn) && string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { message = _errors.Localize("Provide an ISBN or a title to look up.") });
        }

        var preferences = await _preferencesRepository.GetAsync(userId);

        if (!string.IsNullOrWhiteSpace(isbn))
        {
            if (!IsbnValidator.TryNormalize(isbn, out var normalizedIsbn))
            {
                return BadRequest(new { message = _errors.Localize("The provided ISBN is not valid.") });
            }

            var result = await _metadataLookup.LookupByIsbnAsync(normalizedIsbn, preferences.PreferredBookInfoProviderId, HttpContext.RequestAborted);
            if (result is null)
            {
                return NotFound(new { message = _errors.Localize("No book metadata found for that ISBN.") });
            }

            return Ok(result);
        }

        var maxResults = Math.Clamp(_metadataOptions.Value.TitleSearchResultLimit, 1, 25);
        var results = await _metadataLookup.LookupByTitleAsync(title!.Trim(), maxResults, preferences.PreferredBookInfoProviderId, HttpContext.RequestAborted);
        if (results.Count == 0)
        {
            return NotFound(new { message = _errors.Localize("No book metadata found for that title.") });
        }

        return Ok(new { results });
    }

    [HttpGet("info")]
    public async Task<IActionResult> GetBookInfo([FromQuery] string? isbn, [FromQuery] string? title, [FromQuery] string? author)
    {
        var userId = User.GetUserId();

        if (string.IsNullOrWhiteSpace(isbn) && string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { message = _errors.Localize("Provide an ISBN or a title to look up.") });
        }

        var preferences = await _preferencesRepository.GetAsync(userId);

        if (!string.IsNullOrWhiteSpace(isbn))
        {
            if (!IsbnValidator.TryNormalize(isbn, out var normalizedIsbn))
            {
                return BadRequest(new { message = _errors.Localize("The provided ISBN is not valid.") });
            }

            var metadata = await _metadataLookup.LookupByIsbnAsync(normalizedIsbn, preferences.PreferredBookInfoProviderId, HttpContext.RequestAborted);
            return Ok(ToInfoResult(metadata, normalizedIsbn, author));
        }

        var maxResults = Math.Clamp(_metadataOptions.Value.TitleSearchResultLimit, 1, 25);
        var results = await _metadataLookup.LookupByTitleAsync(title!.Trim(), maxResults, preferences.PreferredBookInfoProviderId, HttpContext.RequestAborted);

        if (results.Count == 0)
        {
            return NotFound(new { message = _errors.Localize("No book metadata found for that title.") });
        }

        if (results.Count == 1)
        {
            return Ok(ToInfoResult(results[0], null, null));
        }

        return Ok(new { results = results.Select(r => ToInfoResult(r, null, null)).ToList() });
    }

    private BookInfoResult ToInfoResult(BookMetadataResult? metadata, string? fallbackIsbn, string? fallbackAuthor)
    {
        var isbn = metadata?.Isbn ?? fallbackIsbn;
        var resolvedAuthor = metadata?.Author ?? fallbackAuthor;
        return new BookInfoResult
        {
            Title = metadata?.Title,
            Author = resolvedAuthor,
            Isbn = isbn,
            CoverUrl = metadata?.CoverUrl,
            ProviderId = metadata?.ProviderId,
            Links = _searchLinks.BuildLinks(isbn, metadata?.Title, resolvedAuthor, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
        };
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [HttpPost("spin")]
    public async Task<IActionResult> Spin()
    {
        var userId = User.GetUserId();

        try
        {
            var selected = await _store.SelectRandomAsync(userId);
            await _spinHistory.RecordAsync(userId, selected.Id, DateTimeOffset.UtcNow);
            _metricsService.IncrementSpinsSinceRestart();
            var books = await _store.GetAllAsync(userId);
            return Ok(new
            {
                selected,
                activeBooks = books.ToList()
            });
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = _errors.Localize(ex.Message) });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = _errors.Localize(ex.Message) });
        }
    }

    [HttpGet("spin-history")]
    public async Task<IActionResult> GetSpinHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = User.GetUserId();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (history, total) = await _spinHistory.GetPagedForUserAsync(userId, page, pageSize);
        return Ok(new { history, page, pageSize, total });
    }

    [HttpGet("deleted")]
    public async Task<IActionResult> GetDeleted()
    {
        var userId = User.GetUserId();

        try
        {
            var books = await _store.GetDeletedAsync(userId);
            return Ok(new { books });
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = _errors.Localize(ex.Message) });
        }
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id)
    {
        var userId = User.GetUserId();

        try
        {
            var book = await _store.RestoreAsync(userId, id);
            return Ok(book);
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = _errors.Localize(ex.Message) });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = _errors.Localize(ex.Message) });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var userId = User.GetUserId();

        try
        {
            var book = await _store.RemoveAsync(userId, id);
            return Ok(book);
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = _errors.Localize(ex.Message) });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = _errors.Localize(ex.Message) });
        }
    }
}
