using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PreferencesController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly IUserPreferencesRepository _preferencesRepository;

    public PreferencesController(AuthService authService, IUserPreferencesRepository preferencesRepository)
    {
        _authService = authService;
        _preferencesRepository = preferencesRepository;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        var preferences = await _preferencesRepository.GetAsync(user.UserId);
        return Ok(preferences);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdatePreferencesRequest request)
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        var preferences = await _preferencesRepository.UpdateAsync(
            user.UserId,
            request.Theme,
            request.AnalyticsConsentOptedOut,
            request.PreferredBookInfoProviderId);
        return Ok(preferences);
    }
}
