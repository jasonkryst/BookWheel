using BookWheel.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/system/migrations")]
public sealed class MigrationController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly DataMigrationService _dataMigrationService;

    public MigrationController(AuthService authService, DataMigrationService dataMigrationService)
    {
        _authService = authService;
        _dataMigrationService = dataMigrationService;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var authResult = await AuthorizeMigrationRequestAsync();
        if (authResult is not null)
        {
            return authResult;
        }

        var status = await _dataMigrationService.GetStatusAsync();
        return Ok(status);
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run()
    {
        var authResult = await AuthorizeMigrationRequestAsync();
        if (authResult is not null)
        {
            return authResult;
        }

        var report = await _dataMigrationService.RunAsync();
        return Ok(report);
    }

    private async Task<IActionResult?> AuthorizeMigrationRequestAsync()
    {
        var hasAccount = await _authService.HasAccountAsync();
        if (!hasAccount)
        {
            return null;
        }

        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!user.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return null;
    }
}
