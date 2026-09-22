using BookWheel.Logging;
using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly AppMetricsService _metricsService;
    private readonly ILogger<AuthController> _logger;
    private readonly ApiMessageLocalizer _errors;
    private readonly AppOptions _appOptions;
    private readonly ICredentialRepository _credentialRepository;

    public AuthController(AuthService authService, AppMetricsService metricsService, ILogger<AuthController> logger, ApiMessageLocalizer errors, IOptions<AppOptions> appOptions, ICredentialRepository credentialRepository)
    {
        _authService = authService;
        _metricsService = metricsService;
        _logger = logger;
        _errors = errors;
        _appOptions = appOptions.Value;
        _credentialRepository = credentialRepository;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var hasAccount = await _authService.HasAccountAsync();
        return Ok(new { setupRequired = !hasAccount });
    }

    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] SetupAccountRequest request)
    {
        var hasAccount = await _authService.HasAccountAsync();
        if (hasAccount)
        {
            _logger.LogWarning(
                "Account setup rejected because an account already exists. Username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
                LogSanitizer.Sanitize(request.Username),
                GetClientIp(),
                GetRequestPath(),
                GetRequestId(),
                GetUserAgent());
            return Conflict(new { message = _errors.Localize("An account already exists.") });
        }

        var user = await _authService.CreateAccountAsync(request.Username, request.Password, request.Email);
        _logger.LogInformation(
            "Initial account created for username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
            LogSanitizer.Sanitize(request.Username),
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
                isAdmin = user.IsAdmin,
                email = user.Email
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
                    LogSanitizer.Sanitize(request.Username),
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
                    LogSanitizer.Sanitize(request.Username),
                    GetClientIp(),
                    GetRequestPath(),
                    GetRequestId(),
                    GetUserAgent());
                return Unauthorized(new { message = _errors.Localize("Invalid username or password.") });
            }

            if (validationResult.RequiresPasswordReset)
            {
                _metricsService.IncrementLoginFailure();
                _logger.LogWarning(
                    "Login rejected because password reset is required. Username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
                    LogSanitizer.Sanitize(request.Username),
                    GetClientIp(),
                    GetRequestPath(),
                    GetRequestId(),
                    GetUserAgent());
                return Unauthorized(new { message = _errors.Localize("Invalid username or password.") });
            }

            if (validationResult.IsLockedOut)
            {
                _metricsService.IncrementLoginFailure();
                _metricsService.IncrementLoginLockout();
                _logger.LogWarning(
                    "Login blocked by username lockout. Username {Username} until {LockoutUntilUtc} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
                    LogSanitizer.Sanitize(request.Username),
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
                    LogSanitizer.Sanitize(request.Username),
                    GetClientIp(),
                    GetRequestPath(),
                    GetRequestId(),
                    GetUserAgent());
                return Unauthorized(new { message = _errors.Localize("Invalid username or password.") });
            }

            _logger.LogInformation(
                "Login succeeded for username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
                LogSanitizer.Sanitize(request.Username),
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
                    isAdmin = user.IsAdmin,
                    email = user.Email
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
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = _errors.Localize(ex.Message) });
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
            return BadRequest(new { message = _errors.Localize(ex.Message) });
        }
    }

    [HttpPost("password-reset/request")]
    public async Task<IActionResult> RequestPasswordReset([FromBody] RequestPasswordResetRequest request)
    {
        // Deliberately NOT derived from Request.Scheme/Request.Host: that header is
        // attacker-controlled (AllowedHosts is "*"), and this link is mailed blind to
        // whoever owns the account, not just returned to the caller who set the header.
        // Use the operator-configured App:BaseUrl instead — see AppOptions.
        var appBaseUrl = _appOptions.BaseUrl.TrimEnd('/');
        await _authService.RequestPasswordResetAsync(request.Username, appBaseUrl);
        _logger.LogInformation(
            "Password reset requested. Username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
            LogSanitizer.Sanitize(request.Username),
            GetClientIp(),
            GetRequestPath(),
            GetRequestId(),
            GetUserAgent());
        return Ok(new { message = "If that account exists and has an email on file, a reset link has been sent." });
    }

    [HttpPost("forgot-username")]
    public async Task<IActionResult> ForgotUsername([FromBody] ForgotUsernameRequest request)
    {
        await _authService.RequestForgottenUsernameAsync(request.Email);
        _logger.LogInformation(
            "Forgotten-username requested from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
            GetClientIp(),
            GetRequestPath(),
            GetRequestId(),
            GetUserAgent());
        return Ok(new { message = "If that email is on file, we've sent the associated username." });
    }

    [HttpPost("password-reset/validate")]
    public async Task<IActionResult> ValidatePasswordResetToken([FromBody] ValidatePasswordResetTokenRequest request)
    {
        var result = await _authService.ValidatePasswordResetTokenAsync(request.Token);
        return result.IsValid
            ? Ok(new { isValid = true, username = result.Username, expiresAtUtc = result.ExpiresAtUtc })
            : BadRequest(new { message = _errors.Localize("The password reset link is invalid or has expired.") });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        // Looked up fresh rather than read from the cached session record: Email can
        // change mid-session (e.g. an admin just used the self-service "add your
        // email" flow), and the session cache otherwise wouldn't reflect that until
        // the next login.
        var current = await _credentialRepository.FindByUsernameAsync(user.Username);

        return Ok(new
        {
            authenticated = true,
            userId = user.UserId,
            username = user.Username,
            isAdmin = user.IsAdmin,
            email = current?.Email
        });
    }

    private Task SignInAsync(AuthenticatedUser user)
    {
        var token = _authService.CreateSession(user);
        Response.Cookies.Append("BookWheel.Auth", token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = Request.IsHttps,
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
