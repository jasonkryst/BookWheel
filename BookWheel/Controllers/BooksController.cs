using BookWheel.Models;
using BookWheel.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class BooksController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly BookStore _store;

    public BooksController(AuthService authService, BookStore store)
    {
        _authService = authService;
        _store = store;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        if (!_authService.IsAuthenticated(HttpContext))
        {
            return Unauthorized();
        }

        var books = await _store.GetAllAsync();
        return Ok(new
        {
            books,
            activeBooks = books.ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] UpdateBookRequest request)
    {
        if (!_authService.IsAuthenticated(HttpContext))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { message = "Book title is required." });
        }

        var book = await _store.AddAsync(request.Title);
        return Ok(book);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBookRequest request)
    {
        if (!_authService.IsAuthenticated(HttpContext))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { message = "Book title is required." });
        }

        try
        {
            var book = await _store.UpdateAsync(id, request.Title);
            return Ok(book);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("spin")]
    public async Task<IActionResult> Spin()
    {
        if (!_authService.IsAuthenticated(HttpContext))
        {
            return Unauthorized();
        }

        try
        {
            var selected = await _store.SelectRandomAsync();
            var books = await _store.GetAllAsync();
            return Ok(new
            {
                selected,
                activeBooks = books.ToList()
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        if (!_authService.IsAuthenticated(HttpContext))
        {
            return Unauthorized();
        }

        try
        {
            var book = await _store.RemoveAsync(id);
            return Ok(book);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
