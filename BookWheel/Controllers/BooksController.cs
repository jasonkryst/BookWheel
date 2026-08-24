using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class BooksController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly AppMetricsService _metricsService;
    private readonly IBookRepository _store;
    private readonly ISpinHistoryRepository _spinHistory;
    private readonly ApiMessageLocalizer _errors;
    private readonly IBookMetadataLookupService _metadataLookup;
    private readonly IOptionsSnapshot<BookMetadataOptions> _metadataOptions;

    public BooksController(
        AuthService authService,
        AppMetricsService metricsService,
        IBookRepository store,
        ISpinHistoryRepository spinHistory,
        ApiMessageLocalizer errors,
        IBookMetadataLookupService metadataLookup,
        IOptionsSnapshot<BookMetadataOptions> metadataOptions)
    {
        _authService = authService;
        _metricsService = metricsService;
        _store = store;
        _spinHistory = spinHistory;
        _errors = errors;
        _metadataLookup = metadataLookup;
        _metadataOptions = metadataOptions;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        try
        {
            var books = await _store.GetAllAsync(user.UserId);
            return Ok(new
            {
                books,
                activeBooks = books.ToList()
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
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        try
        {
            var books = await _store.GetAllForExportAsync(user.UserId);
            var history = await _spinHistory.GetForUserAsync(user.UserId);

            return Ok(new
            {
                schemaVersion = 1,
                exportedAtUtc = DateTimeOffset.UtcNow,
                account = new { username = user.Username },
                books = books.Select(book => new
                {
                    title = book.Title,
                    isbn = book.Isbn,
                    author = book.Author,
                    coverUrl = book.CoverUrl,
                    deletedAtUtc = book.DeletedAtUtc
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
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        if (request.Books is null || request.Books.Count == 0)
        {
            return BadRequest(new { message = _errors.Localize("No books were provided to import.") });
        }

        try
        {
            var existingBooks = await _store.GetAllForExportAsync(user.UserId);
            var existingTitles = new HashSet<string>(
                existingBooks.Select(book => NormalizeTitleForMatch(book.Title)),
                StringComparer.Ordinal);
            var existingIsbns = new HashSet<string>(
                existingBooks.Where(book => book.Isbn is not null).Select(book => book.Isbn!),
                StringComparer.Ordinal);

            var added = 0;
            var skipped = 0;

            foreach (var item in request.Books)
            {
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
                var isDuplicate = existingTitles.Contains(normalizedTitle)
                    || (normalizedIsbn is not null && existingIsbns.Contains(normalizedIsbn));

                if (isDuplicate)
                {
                    skipped += 1;
                    continue;
                }

                existingTitles.Add(normalizedTitle);
                if (normalizedIsbn is not null)
                {
                    existingIsbns.Add(normalizedIsbn);
                }

                await _store.AddAsync(user.UserId, title, normalizedIsbn, NormalizeOptional(item.Author), NormalizeOptional(item.CoverUrl));
                added += 1;
            }

            return Ok(new { added, skipped });
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
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

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
            var book = await _store.AddAsync(user.UserId, request.Title, normalizedIsbn, NormalizeOptional(request.Author), NormalizeOptional(request.CoverUrl), request.AddedByScanner);
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
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

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
            var book = await _store.UpdateAsync(user.UserId, id, request.Title, normalizedIsbn, NormalizeOptional(request.Author), NormalizeOptional(request.CoverUrl));
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
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(isbn) && string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { message = _errors.Localize("Provide an ISBN or a title to look up.") });
        }

        if (!string.IsNullOrWhiteSpace(isbn))
        {
            if (!IsbnValidator.TryNormalize(isbn, out var normalizedIsbn))
            {
                return BadRequest(new { message = _errors.Localize("The provided ISBN is not valid.") });
            }

            var result = await _metadataLookup.LookupByIsbnAsync(normalizedIsbn, HttpContext.RequestAborted);
            if (result is null)
            {
                return NotFound(new { message = _errors.Localize("No book metadata found for that ISBN.") });
            }

            return Ok(result);
        }

        var maxResults = Math.Clamp(_metadataOptions.Value.TitleSearchResultLimit, 1, 25);
        var results = await _metadataLookup.LookupByTitleAsync(title!.Trim(), maxResults, HttpContext.RequestAborted);
        if (results.Count == 0)
        {
            return NotFound(new { message = _errors.Localize("No book metadata found for that title.") });
        }

        return Ok(new { results });
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [HttpPost("spin")]
    public async Task<IActionResult> Spin()
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        try
        {
            var selected = await _store.SelectRandomAsync(user.UserId);
            await _spinHistory.RecordAsync(user.UserId, selected.Id, DateTimeOffset.UtcNow);
            _metricsService.IncrementSpinCount();
            var books = await _store.GetAllAsync(user.UserId);
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
    public async Task<IActionResult> GetSpinHistory()
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        var history = await _spinHistory.GetForUserAsync(user.UserId);
        return Ok(new { history });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        try
        {
            var book = await _store.RemoveAsync(user.UserId, id);
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
