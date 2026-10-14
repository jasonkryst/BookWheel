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
    private readonly IBookInfoProviderRepository _bookInfoProviders;
    private readonly ApiMessageLocalizer _errors;

    public PreferencesController(
        IUserPreferencesRepository preferencesRepository,
        IBookInfoProviderRepository bookInfoProviders,
        ApiMessageLocalizer errors)
    {
        _preferencesRepository = preferencesRepository;
        _bookInfoProviders = bookInfoProviders;
        _errors = errors;
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

        if (request.PreferredBookInfoProviderId is not null
            && !await _bookInfoProviders.ExistsAsync(request.PreferredBookInfoProviderId.Value))
        {
            return BadRequest(new { message = _errors.Localize("Book info provider must be a valid provider.") });
        }

        var preferences = await _preferencesRepository.UpdateAsync(
            userId,
            request.Theme,
            request.AnalyticsConsentOptedOut,
            request.PreferredBookInfoProviderId,
            request.Locale);
        return Ok(preferences);
    }
}
