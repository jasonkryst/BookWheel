using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PreferencesController : ControllerBase
{
    private readonly IUserPreferencesRepository _preferencesRepository;

    public PreferencesController(IUserPreferencesRepository preferencesRepository)
    {
        _preferencesRepository = preferencesRepository;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var userId = User.GetUserId();

        var preferences = await _preferencesRepository.GetAsync(userId);
        return Ok(preferences);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdatePreferencesRequest request)
    {
        var userId = User.GetUserId();

        var preferences = await _preferencesRepository.UpdateAsync(
            userId,
            request.Theme,
            request.AnalyticsConsentOptedOut,
            request.PreferredBookInfoProviderId);
        return Ok(preferences);
    }
}
