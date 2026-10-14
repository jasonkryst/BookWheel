using BookWheel.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

/// <summary>
/// GH #224: a single, data-driven source for the lookup lists the frontend used to
/// hard-code (book types, book info providers) — the UI builds its &lt;select&gt;
/// options from this instead of a static list baked into index.html/app.js.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ReferenceController : ControllerBase
{
    // Icon and i18n key for the three seeded, built-in book types. A future
    // admin-created type (not yet supported) would have no entry here and fall
    // back to the default icon with its own stored name instead of an i18n key.
    private static readonly Dictionary<int, (string Icon, string I18nKey)> BuiltInBookTypeDisplay = new()
    {
        [1] = ("📖", "books.typePhysical"),
        [2] = ("📱", "books.typeDigital"),
        [3] = ("🔖", "books.typeNookOnly")
    };

    private const string DefaultBookTypeIcon = "📖";

    private readonly IBookTypeRepository _bookTypes;
    private readonly IBookInfoProviderRepository _bookInfoProviders;

    public ReferenceController(IBookTypeRepository bookTypes, IBookInfoProviderRepository bookInfoProviders)
    {
        _bookTypes = bookTypes;
        _bookInfoProviders = bookInfoProviders;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAsync()
    {
        var bookTypes = await _bookTypes.GetAllAsync();
        var bookInfoProviders = await _bookInfoProviders.GetAllAsync();

        return Ok(new
        {
            bookTypes = bookTypes.Select(t =>
            {
                var hasDisplay = BuiltInBookTypeDisplay.TryGetValue(t.Id, out var display);
                return new
                {
                    id = t.Id,
                    name = t.Name,
                    icon = hasDisplay ? display.Icon : DefaultBookTypeIcon,
                    i18nKey = hasDisplay ? display.I18nKey : null
                };
            }),
            bookInfoProviders = bookInfoProviders.Select(p => new { id = p.Id, name = p.Name })
        });
    }
}
