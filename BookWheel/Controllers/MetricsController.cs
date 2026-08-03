using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class MetricsController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly AppMetricsService _metricsService;
    private readonly IBookRepository _bookStore;

    public MetricsController(AuthService authService, AppMetricsService metricsService, IBookRepository bookStore)
    {
        _authService = authService;
        _metricsService = metricsService;
        _bookStore = bookStore;
    }

    [HttpGet]
    public async Task<IActionResult> GetMetrics()
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

        var snapshot = await _metricsService.GetSnapshotAsync(_bookStore);
        return Ok(snapshot);
    }
}
