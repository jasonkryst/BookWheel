using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/[controller]")]
public sealed class MetricsController : ControllerBase
{
    private readonly AppMetricsService _metricsService;
    private readonly IBookRepository _bookStore;

    public MetricsController(AppMetricsService metricsService, IBookRepository bookStore)
    {
        _metricsService = metricsService;
        _bookStore = bookStore;
    }

    [HttpGet]
    public async Task<IActionResult> GetMetrics()
    {
        var snapshot = await _metricsService.GetSnapshotAsync(_bookStore);
        return Ok(snapshot);
    }
}
