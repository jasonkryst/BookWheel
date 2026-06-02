using BookWheel.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class MetricsController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly AppMetricsService _metricsService;
    private readonly BookStore _bookStore;

    public MetricsController(AuthService authService, AppMetricsService metricsService, BookStore bookStore)
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
