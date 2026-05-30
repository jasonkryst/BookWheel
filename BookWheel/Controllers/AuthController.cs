using BookWheel.Models;
using BookWheel.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
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

        await _authService.CreateAccountAsync(request.Username, request.Password);
        _logger.LogInformation(
            "Initial account created for username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
            request.Username,
            GetClientIp(),
            GetRequestPath(),
            GetRequestId(),
            GetUserAgent());
        await SignInAsync();
        return Ok(new { message = "Account created." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
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

        if (!await _authService.ValidateCredentialsAsync(request.Username, request.Password))
        {
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
        await SignInAsync();
        return Ok(new { message = "Logged in." });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        _authService.SignOut(HttpContext);
        return Ok(new { message = "Logged out." });
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        return _authService.IsAuthenticated(HttpContext) ? Ok(new { authenticated = true }) : Unauthorized();
    }

    private Task SignInAsync()
    {
        var token = _authService.CreateSession();
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
