using System.Globalization;
using System.Security.Claims;
using BookWheel.Models;

namespace BookWheel.Services;

public static class AuthDefaults
{
	public const string Scheme = "BookWheel.Cookies";
	public const string CookieName = "BookWheel.Auth";
	public const string AdminRole = "Admin";
	public const string AdminPolicy = "Admin";
	public const string SecurityStampClaimType = "security_stamp";
	public const string IssuedAtClaimType = "auth_issued_at";

	// Default absolute session lifetime (configurable via Security:AbsoluteSessionLifetimeHours;
	// the sliding idle timeout is separate, Security:IdleTimeoutHours). Pinned at sign-in via the issued-at claim (which survives
	// sliding renewals, unlike the ticket's IssuedUtc), and enforced in OnValidatePrincipal
	// so an actively-used session cannot slide indefinitely.
	public static readonly TimeSpan AbsoluteSessionLifetime = TimeSpan.FromHours(24);

	// A ticket is past its absolute cap when it has no issued-at stamp (defensive: pre-upgrade
	// or malformed) or was issued longer ago than the absolute lifetime.
	public static bool IsBeyondAbsoluteLifetime(DateTimeOffset? issuedAt, DateTimeOffset now, TimeSpan absoluteLifetime) =>
		issuedAt is null || now - issuedAt.Value > absoluteLifetime;
}

public static class ClaimsPrincipalExtensions
{
	public static Guid GetUserId(this ClaimsPrincipal principal)
	{
		var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
		return Guid.TryParse(value, out var id) ? id : Guid.Empty;
	}

	public static string? GetUsername(this ClaimsPrincipal principal)
	{
		return principal.FindFirstValue(ClaimTypes.Name);
	}

	public static DateTimeOffset? GetIssuedAt(this ClaimsPrincipal principal)
	{
		var value = principal.FindFirstValue(AuthDefaults.IssuedAtClaimType);
		return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var issuedAt)
			? issuedAt
			: null;
	}

	public static ClaimsPrincipal BuildPrincipal(AuthenticatedUser user, string scheme)
	{
		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
			new(ClaimTypes.Name, user.Username),
			new(AuthDefaults.SecurityStampClaimType, user.SecurityStamp),
			new(AuthDefaults.IssuedAtClaimType, DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture))
		};

		if (user.IsAdmin)
		{
			claims.Add(new Claim(ClaimTypes.Role, AuthDefaults.AdminRole));
		}

		return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
	}
}
