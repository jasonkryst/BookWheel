using BookWheel.Models;
using BookWheel.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class UsersController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly CredentialStore _credentialStore;
    private readonly BookStore _bookStore;
    private readonly ILogger<UsersController> _logger;

    public UsersController(AuthService authService, CredentialStore credentialStore, BookStore bookStore, ILogger<UsersController> logger)
    {
        _authService = authService;
        _credentialStore = credentialStore;
        _bookStore = bookStore;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        var currentUser = _authService.GetAuthenticatedUser(HttpContext);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!currentUser.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var users = await _credentialStore.GetUsersAsync();
        return Ok(new { users });
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        var currentUser = _authService.GetAuthenticatedUser(HttpContext);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!currentUser.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            var user = await _credentialStore.CreateUserAsync(request.Username, request.Password, request.IsAdmin);
            _logger.LogInformation(
                "User account created. Actor {ActorUsername} target {TargetUsername} role {IsAdmin} request {RequestId}",
                currentUser.Username,
                user.Username,
                user.IsAdmin,
                HttpContext.TraceIdentifier);
            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserAccountRequest request)
    {
        var currentUser = _authService.GetAuthenticatedUser(HttpContext);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!currentUser.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (id == currentUser.UserId)
        {
            return BadRequest(new { message = "Administrators can only update other user accounts." });
        }

        try
        {
            var before = (await _credentialStore.GetUsersAsync()).FirstOrDefault(user => user.UserId == id);
            var user = await _credentialStore.UpdateUserAsync(id, request.Username, request.IsAdmin, request.IsDisabled, request.ForcePasswordReset, request.IsLocked);
            if (before is not null)
            {
                if (before.IsAdmin != user.IsAdmin)
                {
                    _logger.LogInformation(
                        "Role changed. Actor {ActorUsername} target {TargetUsername} from {OldIsAdmin} to {NewIsAdmin} request {RequestId}",
                        currentUser.Username,
                        user.Username,
                        before.IsAdmin,
                        user.IsAdmin,
                        HttpContext.TraceIdentifier);
                }

                if (before.IsDisabled != user.IsDisabled || before.IsLocked != user.IsLocked || before.ForcePasswordReset != user.ForcePasswordReset)
                {
                    _logger.LogInformation(
                        "Account security state changed. Actor {ActorUsername} target {TargetUsername} disabled {IsDisabled} locked {IsLocked} forceReset {ForcePasswordReset} request {RequestId}",
                        currentUser.Username,
                        user.Username,
                        user.IsDisabled,
                        user.IsLocked,
                        user.ForcePasswordReset,
                        HttpContext.TraceIdentifier);
                }
            }

            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/password-reset-link")]
    public async Task<IActionResult> CreatePasswordResetLink(Guid id)
    {
        var currentUser = _authService.GetAuthenticatedUser(HttpContext);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!currentUser.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (id == currentUser.UserId)
        {
            return BadRequest(new { message = "Administrators can only generate reset links for other user accounts." });
        }

        try
        {
            var appBaseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
            var result = await _credentialStore.CreatePasswordResetLinkAsync(id, appBaseUrl);
            _logger.LogInformation(
                "Forced password reset link generated. Actor {ActorUsername} target {TargetUsername} request {RequestId}",
                currentUser.Username,
                result.Username,
                HttpContext.TraceIdentifier);
            return Ok(new
            {
                username = result.Username,
                resetLink = result.ResetLink,
                expiresAtUtc = result.ExpiresAtUtc
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var currentUser = _authService.GetAuthenticatedUser(HttpContext);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!currentUser.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (id == currentUser.UserId)
        {
            return BadRequest(new { message = "Administrators can only remove other user accounts." });
        }

        try
        {
            var deletedUser = await _credentialStore.DeleteUserAsync(id);
            var removedBooks = await _bookStore.RemoveUserDataAsync(id);
            _authService.RemoveSessionsForUser(id);
            _logger.LogInformation(
                "User account deleted. Actor {ActorUsername} target {TargetUsername} removed books {RemovedBooks} request {RequestId}",
                currentUser.Username,
                deletedUser.Username,
                removedBooks,
                HttpContext.TraceIdentifier);
            return Ok(new
            {
                userId = deletedUser.UserId,
                username = deletedUser.Username,
                removedBooks
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
