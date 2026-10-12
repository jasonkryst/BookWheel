using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BookWheel.Controllers;

[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/system")]
public sealed class SystemController : ControllerBase
{
    private readonly IOptions<EmailOptions> _emailOptions;
    private readonly IOptions<AppOptions> _appOptions;
    private readonly IOptions<BookMetadataOptions> _metadataOptions;
    private readonly IConfiguration _configuration;
    private readonly ICredentialRepository _credentials;
    private readonly IEmailSender _emailSender;
    private readonly ApiMessageLocalizer _errors;

    public SystemController(
        IOptions<EmailOptions> emailOptions,
        IOptions<AppOptions> appOptions,
        IOptions<BookMetadataOptions> metadataOptions,
        IConfiguration configuration,
        ICredentialRepository credentials,
        IEmailSender emailSender,
        ApiMessageLocalizer errors)
    {
        _emailOptions = emailOptions;
        _appOptions = appOptions;
        _metadataOptions = metadataOptions;
        _configuration = configuration;
        _credentials = credentials;
        _emailSender = emailSender;
        _errors = errors;
    }

    /// <summary>Booleans only — never returns hosts, URLs, keys or IP ranges.</summary>
    [HttpGet("config-status")]
    public IActionResult GetConfigStatus()
    {
        var forwarded = _configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>() ?? new ForwardedHeadersSettings();
        var proxyTrusted =
            forwarded.KnownNetworks.Any(v => !string.IsNullOrWhiteSpace(v)) ||
            forwarded.KnownProxies.Any(v => !string.IsNullOrWhiteSpace(v)) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORWARDED_KNOWN_NETWORKS"));

        return Ok(new
        {
            smtpConfigured = !string.IsNullOrWhiteSpace(_emailOptions.Value.Host),
            baseUrlConfigured = !string.IsNullOrWhiteSpace(_appOptions.Value.BaseUrl),
            googleBooksKeyConfigured = !string.IsNullOrWhiteSpace(_metadataOptions.Value.GoogleBooks.ApiKey),
            proxyTrustConfigured = proxyTrusted
        });
    }

    [HttpPost("test-email")]
    public async Task<IActionResult> SendTestEmail(CancellationToken cancellationToken)
    {
        var record = await _credentials.FindByIdAsync(User.GetUserId());
        if (record is null || string.IsNullOrWhiteSpace(record.Email))
        {
            return BadRequest(new { message = _errors.Localize("No email address is set for your account.") });
        }

        var result = await _emailSender.TrySendAsync(
            record.Email,
            _errors.Localize("Book Wheel test email"),
            _errors.Localize("This is a test email from Book Wheel. If you can read this, email delivery is working."),
            cancellationToken);

        return Ok(new { success = result.Success, error = result.Error });
    }
}
