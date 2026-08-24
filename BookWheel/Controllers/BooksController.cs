using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class BooksController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly AppMetricsService _metricsService;
    private readonly IBookRepository _store;
    private readonly ApiMessageLocalizer _errors;
    private readonly IBookMetadataLookupService _metadataLookup;

    public BooksController(
        AuthService authService,
        AppMetricsService metricsService,
        IBookRepository store,
        ApiMessageLocalizer errors,
        IBookMetadataLookupService metadataLookup)
    {
        _authService = authService;
        _metricsService = metricsService;
        _store = store;
        _errors = errors;
        _metadataLookup = metadataLookup;
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
            var book = await _store.AddAsync(user.UserId, request.Title, normalizedIsbn, NormalizeOptional(request.Author), NormalizeOptional(request.CoverUrl));
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

        BookMetadataResult? result;
        if (!string.IsNullOrWhiteSpace(isbn))
        {
            if (!IsbnValidator.TryNormalize(isbn, out var normalizedIsbn))
            {
                return BadRequest(new { message = _errors.Localize("The provided ISBN is not valid.") });
            }

            result = await _metadataLookup.LookupByIsbnAsync(normalizedIsbn, HttpContext.RequestAborted);
        }
        else
        {
            result = await _metadataLookup.LookupByTitleAsync(title!.Trim(), HttpContext.RequestAborted);
        }

        if (result is null)
        {
            var message = !string.IsNullOrWhiteSpace(isbn)
                ? "No book metadata found for that ISBN."
                : "No book metadata found for that title.";
            return NotFound(new { message = _errors.Localize(message) });
        }

        return Ok(result);
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
