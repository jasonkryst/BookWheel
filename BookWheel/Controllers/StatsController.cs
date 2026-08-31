using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class StatsController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly ISpinStatsRepository _statsRepo;

    public StatsController(AuthService authService, ISpinStatsRepository statsRepo)
    {
        _authService = authService;
        _statsRepo = statsRepo;
    }

    [HttpGet]
    public async Task<IActionResult> GetStats()
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        var stats = await _statsRepo.GetForUserAsync(user.UserId);
        return Ok(stats);
    }

    [HttpGet("aggregate")]
    public async Task<IActionResult> GetAggregate()
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!user.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var aggregate = await _statsRepo.GetAggregateAsync();
        return Ok(aggregate);
    }
}
