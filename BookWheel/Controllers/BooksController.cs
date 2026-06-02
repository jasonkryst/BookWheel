using BookWheel.Models;
using BookWheel.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class BooksController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly AppMetricsService _metricsService;
    private readonly BookStore _store;

    public BooksController(AuthService authService, AppMetricsService metricsService, BookStore store)
    {
        _authService = authService;
        _metricsService = metricsService;
        _store = store;
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
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
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
            return BadRequest(new { message = "Book title is required." });
        }

        try
        {
            var book = await _store.AddAsync(user.UserId, request.Title);
            return Ok(book);
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
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
            return BadRequest(new { message = "Book title is required." });
        }

        try
        {
            var book = await _store.UpdateAsync(user.UserId, id, request.Title);
            return Ok(book);
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

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
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
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
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
