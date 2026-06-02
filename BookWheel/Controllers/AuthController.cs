using BookWheel.Models;
using BookWheel.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly AppMetricsService _metricsService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AuthService authService, AppMetricsService metricsService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _metricsService = metricsService;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var hasAccount = await _authService.HasAccountAsync();
        return Ok(new { setupRequired = !hasAccount });
    }

    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] LoginRequest request)
    {
        var hasAccount = await _authService.HasAccountAsync();
        if (hasAccount)
        {
            _logger.LogWarning(
                "Account setup rejected because an account already exists. Username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
                request.Username,
                GetClientIp(),
                GetRequestPath(),
                GetRequestId(),
                GetUserAgent());
            return Conflict(new { message = "An account already exists." });
        }

        var user = await _authService.CreateAccountAsync(request.Username, request.Password);
        _logger.LogInformation(
            "Initial account created for username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
            request.Username,
            GetClientIp(),
            GetRequestPath(),
            GetRequestId(),
            GetUserAgent());
        await SignInAsync(user);
        return Ok(new
        {
            message = "Account created.",
            user = new
            {
                userId = user.UserId,
                username = user.Username,
                isAdmin = user.IsAdmin
            }
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            if (!await _authService.HasAccountAsync())
            {
                _logger.LogWarning(
                    "Login rejected because setup is required. Username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
                    request.Username,
                    GetClientIp(),
                    GetRequestPath(),
                    GetRequestId(),
                    GetUserAgent());
                return Conflict(new { message = "No account exists yet. Create one first." });
            }

            var validationResult = await _authService.ValidateCredentialsAsync(request.Username, request.Password);
            if (validationResult.IsDisabled)
            {
                _metricsService.IncrementLoginFailure();
                _logger.LogWarning(
                    "Login rejected because account is disabled. Username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
                    request.Username,
                    GetClientIp(),
                    GetRequestPath(),
                    GetRequestId(),
                    GetUserAgent());
                return StatusCode(StatusCodes.Status423Locked, new { message = "This account is disabled. Contact an administrator." });
            }

            if (validationResult.RequiresPasswordReset)
            {
                _metricsService.IncrementLoginFailure();
                _logger.LogWarning(
                    "Login rejected because password reset is required. Username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
                    request.Username,
                    GetClientIp(),
                    GetRequestPath(),
                    GetRequestId(),
                    GetUserAgent());
                return StatusCode(StatusCodes.Status423Locked, new { message = "Password reset is required. Ask an administrator for a reset link." });
            }

            if (validationResult.IsLockedOut)
            {
                _metricsService.IncrementLoginFailure();
                _metricsService.IncrementLoginLockout();
                _logger.LogWarning(
                    "Login blocked by username lockout. Username {Username} until {LockoutUntilUtc} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
                    request.Username,
                    validationResult.LockoutEndsAtUtc,
                    GetClientIp(),
                    GetRequestPath(),
                    GetRequestId(),
                    GetUserAgent());
                return StatusCode(StatusCodes.Status423Locked, new
                {
                    message = "Too many failed attempts for this username. Try again later.",
                    lockoutEndsAtUtc = validationResult.LockoutEndsAtUtc
                });
            }

            var user = validationResult.User;
            if (user is null)
            {
                _metricsService.IncrementLoginFailure();
                _logger.LogWarning(
                    "Login failed for username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
                    request.Username,
                    GetClientIp(),
                    GetRequestPath(),
                    GetRequestId(),
                    GetUserAgent());
                return Unauthorized(new { message = "Invalid username or password." });
            }

            _logger.LogInformation(
                "Login succeeded for username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
                request.Username,
                GetClientIp(),
                GetRequestPath(),
                GetRequestId(),
                GetUserAgent());
            await SignInAsync(user);
            _metricsService.IncrementSuccessfulLogin();
            return Ok(new
            {
                message = "Logged in.",
                user = new
                {
                    userId = user.UserId,
                    username = user.Username,
                    isAdmin = user.IsAdmin
                }
            });
        }
        catch (CorruptedDataException ex)
        {
            _logger.LogError(ex,
                "Login failed due to credential storage corruption from {ClientIp} path {Path} request {RequestId}",
                GetClientIp(),
                GetRequestPath(),
                GetRequestId());
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        _authService.SignOut(HttpContext);
        return Ok(new { message = "Logged out." });
    }

    [HttpPost("password-reset/complete")]
    public async Task<IActionResult> CompletePasswordReset([FromBody] CompletePasswordResetRequest request)
    {
        try
        {
            var username = await _authService.CompletePasswordResetAsync(request.Token, request.NewPassword);
            _logger.LogInformation(
                "Password reset completed for username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
                username,
                GetClientIp(),
                GetRequestPath(),
                GetRequestId(),
                GetUserAgent());
            return Ok(new { message = "Password updated." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "Password reset rejected from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}. Reason {Reason}",
                GetClientIp(),
                GetRequestPath(),
                GetRequestId(),
                GetUserAgent(),
                ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("password-reset/validate")]
    public async Task<IActionResult> ValidatePasswordResetToken([FromBody] ValidatePasswordResetTokenRequest request)
    {
        var result = await _authService.ValidatePasswordResetTokenAsync(request.Token);
        return result.IsValid
            ? Ok(new { isValid = true, username = result.Username, expiresAtUtc = result.ExpiresAtUtc })
            : BadRequest(new { message = "The password reset link is invalid or has expired." });
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(new
        {
            authenticated = true,
            userId = user.UserId,
            username = user.Username,
            isAdmin = user.IsAdmin
        });
    }

    private Task SignInAsync(AuthenticatedUser user)
    {
        var token = _authService.CreateSession(user);
        var environment = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var secureCookie = !environment.IsDevelopment() && !environment.IsEnvironment("Testing")
            ? true
            : Request.IsHttps;

        Response.Cookies.Append("BookWheel.Auth", token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = secureCookie,
            IsEssential = true,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddHours(8)
        });

        return Task.CompletedTask;
    }

    private string GetClientIp()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private string GetRequestPath()
    {
        return HttpContext.Request.Path.Value ?? string.Empty;
    }

    private string GetRequestId()
    {
        return HttpContext.TraceIdentifier;
    }

    private string GetUserAgent()
    {
        return HttpContext.Request.Headers.UserAgent.ToString();
    }
}
