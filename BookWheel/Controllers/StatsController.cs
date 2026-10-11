using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class StatsController : ControllerBase
{
    private readonly ISpinStatsRepository _statsRepo;

    public StatsController(ISpinStatsRepository statsRepo)
    {
        _statsRepo = statsRepo;
    }

    [HttpGet]
    public async Task<IActionResult> GetStats()
    {
        var userId = User.GetUserId();

        var stats = await _statsRepo.GetForUserAsync(userId);
        return Ok(stats);
    }

    [HttpGet("aggregate")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> GetAggregate()
    {
        var aggregate = await _statsRepo.GetAggregateAsync();
        return Ok(aggregate);
    }
}
