using BookWheel.HealthChecks;
using BookWheel.Logging;
using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;
using BookWheel.Storage.Postgres;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Reflection;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var dataProtectionKeyPath = builder.Configuration["DataProtection:KeyDirectory"];
if (string.IsNullOrWhiteSpace(dataProtectionKeyPath) && !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
	dataProtectionKeyPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys");
}

var dataProtectionBuilder = builder.Services.AddDataProtection().SetApplicationName("BookWheel");
if (!string.IsNullOrWhiteSpace(dataProtectionKeyPath))
{
	Directory.CreateDirectory(dataProtectionKeyPath);
	dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath));
}
var connectionString = builder.Configuration.GetConnectionString("BookWheel");
if (string.IsNullOrWhiteSpace(connectionString))
{
	throw new InvalidOperationException(
		"ConnectionStrings:BookWheel is not configured. Set it in appsettings.json, an environment variable (ConnectionStrings__BookWheel), or a deployment secret.");
}

var migrationConnectionString = builder.Configuration.GetConnectionString("BookWheelMigrations");
if (string.IsNullOrWhiteSpace(migrationConnectionString))
{
	// No least-privilege split configured — migrations and runtime queries share
	// the same role, matching pre-2.15.0 behavior exactly.
	migrationConnectionString = connectionString;
}

builder.Services.AddPooledDbContextFactory<BookWheelDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
builder.Services.Configure<ObservabilityOptions>(builder.Configuration.GetSection(ObservabilityOptions.SectionName));
builder.Services.Configure<BookMetadataOptions>(builder.Configuration.GetSection(BookMetadataOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));
builder.Services.Configure<RetailerLinksOptions>(builder.Configuration.GetSection(RetailerLinksOptions.SectionName));
var authSecurityOptions = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<AppMetricsService>();
builder.Services.AddSingleton<UserManagementService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<AccountEmailService>();
builder.Services.AddLocalization();
builder.Services.AddSingleton<ApiMessageLocalizer>();

builder.Services.AddSingleton<JsonBookRepository>();
builder.Services.AddSingleton<JsonCredentialRepository>();
builder.Services.AddSingleton<JsonPasswordResetTokenRepository>();

builder.Services.AddSingleton<PostgresBookRepository>();
builder.Services.AddSingleton<IBookRepository>(sp => sp.GetRequiredService<PostgresBookRepository>());

builder.Services.AddSingleton<PostgresSpinHistoryRepository>();
builder.Services.AddSingleton<ISpinHistoryRepository>(sp => sp.GetRequiredService<PostgresSpinHistoryRepository>());

builder.Services.AddSingleton<PostgresSpinStatsRepository>();
builder.Services.AddSingleton<ISpinStatsRepository>(sp => sp.GetRequiredService<PostgresSpinStatsRepository>());

builder.Services.AddSingleton<PostgresCredentialRepository>();
builder.Services.AddSingleton<ICredentialRepository>(sp => sp.GetRequiredService<PostgresCredentialRepository>());

builder.Services.AddSingleton<PostgresPasswordResetTokenRepository>();
builder.Services.AddSingleton<IPasswordResetTokenRepository>(sp => sp.GetRequiredService<PostgresPasswordResetTokenRepository>());

builder.Services.AddSingleton<PostgresLoginThrottleRepository>();
builder.Services.AddSingleton<ILoginThrottleRepository>(sp => sp.GetRequiredService<PostgresLoginThrottleRepository>());

builder.Services.AddSingleton<PostgresUserPreferencesRepository>();
builder.Services.AddSingleton<IUserPreferencesRepository>(sp => sp.GetRequiredService<PostgresUserPreferencesRepository>());

builder.Services.AddSingleton<DataMigrationService>();
builder.Services.AddSingleton<PostgresMigrationService>();
builder.Services.AddControllers()
	.ConfigureApiBehaviorOptions(options =>
	{
		options.InvalidModelStateResponseFactory = context =>
		{
			var localizer = context.HttpContext.RequestServices.GetRequiredService<ApiMessageLocalizer>();
			var problemDetails = new ValidationProblemDetails(context.ModelState)
			{
				Status = StatusCodes.Status400BadRequest,
				Instance = context.HttpContext.Request.Path
			};

			foreach (var key in problemDetails.Errors.Keys.ToList())
			{
				problemDetails.Errors[key] = problemDetails.Errors[key]
					.Select(localizer.Localize)
					.ToArray();
			}

			return new BadRequestObjectResult(problemDetails)
			{
				ContentTypes = { "application/json" }
			};
		};
	});
builder.Services.AddAuthentication(AuthDefaults.Scheme)
	.AddCookie(AuthDefaults.Scheme, options =>
	{
		options.Cookie.Name = AuthDefaults.CookieName;
		options.Cookie.HttpOnly = true;
		options.Cookie.SameSite = SameSiteMode.Strict;
		options.Cookie.IsEssential = true;
		options.Cookie.Path = "/";
		// SameAsRequest => Secure when Request.IsHttps, which (after UseForwardedHeaders)
		// is true behind an HTTPS reverse proxy.
		options.Cookie.SecurePolicy = authSecurityOptions.AlwaysSecureCookie
			? CookieSecurePolicy.Always
			: CookieSecurePolicy.SameAsRequest;
		options.ExpireTimeSpan = TimeSpan.FromHours(authSecurityOptions.IdleTimeoutHours);
		options.SlidingExpiration = true;

		// API/SPA: never redirect to an HTML login/access-denied page.
		options.Events.OnRedirectToLogin = context =>
		{
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			return Task.CompletedTask;
		};
		options.Events.OnRedirectToAccessDenied = context =>
		{
			context.Response.StatusCode = StatusCodes.Status403Forbidden;
			return Task.CompletedTask;
		};

		// Session revocation (AUTH-1): the cookie is self-contained, so on every request
		// re-check the account and compare the security stamp. Any security-relevant change
		// (role, disable/lock, forced reset, rename, password) rotates the stamp.
		options.Events.OnValidatePrincipal = async context =>
		{
			var principal = context.Principal;
			var repository = context.HttpContext.RequestServices.GetRequiredService<ICredentialRepository>();
			var userId = principal?.GetUserId() ?? Guid.Empty;
			var stamp = principal?.FindFirstValue(AuthDefaults.SecurityStampClaimType);

			var account = userId == Guid.Empty ? null : await repository.FindByIdAsync(userId);
			var issuedAt = principal?.GetIssuedAt();
			var absoluteLifetime = TimeSpan.FromHours(authSecurityOptions.AbsoluteSessionLifetimeHours);
			var isRevoked =
				account is null ||
				account.IsDisabled ||
				account.ForcePasswordReset ||
				(account.IsLocked && (account.LockedUntilUtc ?? DateTimeOffset.MaxValue) > DateTimeOffset.UtcNow) ||
				string.IsNullOrEmpty(stamp) ||
				!string.Equals(account.SecurityStamp, stamp, StringComparison.Ordinal) ||
				// Absolute lifetime cap: sliding expiry must not let a session live indefinitely.
				AuthDefaults.IsBeyondAbsoluteLifetime(issuedAt, DateTimeOffset.UtcNow, absoluteLifetime);

			if (isRevoked)
			{
				context.RejectPrincipal();
				await context.HttpContext.SignOutAsync(AuthDefaults.Scheme);
			}
		};
	});
builder.Services.AddAuthorization(options =>
{
	options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
	options.AddPolicy(AuthDefaults.AdminPolicy, policy => policy.RequireRole(AuthDefaults.AdminRole));
});
builder.Services.AddHttpClient("central-log-shipper");
builder.Services.AddHttpClient<OpenLibraryBookMetadataLookupService>(client =>
{
	client.BaseAddress = new Uri("https://openlibrary.org/");
	client.Timeout = TimeSpan.FromSeconds(8);
	client.DefaultRequestHeaders.UserAgent.ParseAdd("BookWheel/1.0 (+https://github.com/jasonkryst/BookWheel)");
});
builder.Services.AddHttpClient<GoogleBooksBookMetadataLookupService>(client =>
{
	client.BaseAddress = new Uri("https://www.googleapis.com/books/v1/");
	client.Timeout = TimeSpan.FromSeconds(8);
	client.DefaultRequestHeaders.UserAgent.ParseAdd("BookWheel/1.0 (+https://github.com/jasonkryst/BookWheel)");
});
builder.Services.AddMemoryCache(options => options.SizeLimit = 2048);
builder.Services.AddTransient(sp => new BookMetadataLookupDispatcher(
	sp.GetRequiredService<OpenLibraryBookMetadataLookupService>(),
	sp.GetRequiredService<GoogleBooksBookMetadataLookupService>(),
	sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()));
builder.Services.AddSingleton<BookSearchLinksService>();
builder.Services.AddHostedService<StartupDiagnosticsService>();
builder.Services.AddHostedService<LogShippingService>();
builder.Services.AddHostedService<LoginThrottleCleanupService>();
builder.Services.AddHealthChecks()
	.AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
	.AddCheck<LoggingHealthCheck>("logging", tags: ["ready"])
	.AddCheck("app", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("App is running."), tags: ["live", "ready"]);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
	options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
	// Do NOT call KnownIPNetworks.Clear() or KnownProxies.Clear() here.
	// The defaults restrict header trust to loopback (127.0.0.1, ::1).
	// Clearing them lets any client spoof X-Forwarded-For, bypassing the IP-keyed rate limiter.
	// If a reverse proxy is in use, opt in explicitly via ForwardedHeaders:KnownNetworks / KnownProxies
	// (or the FORWARDED_KNOWN_NETWORKS env, comma-separated CIDRs). These are ADDED to the loopback defaults.
	var forwardedSettings = builder.Configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>() ?? new ForwardedHeadersSettings();
	ForwardedHeadersConfigurator.Apply(
		options,
		forwardedSettings.KnownNetworks,
		forwardedSettings.KnownProxies,
		Environment.GetEnvironmentVariable("FORWARDED_KNOWN_NETWORKS"));
});

var securityOptions = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
var observabilityOptions = builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>() ?? new ObservabilityOptions();
var googleAnalyticsId = builder.Configuration["Analytics:GoogleAnalyticsId"] ?? string.Empty;

var logDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "logs");
builder.Logging.AddProvider(new JsonFileLoggerProvider(logDirectory, new JsonFileLoggerOptions
{
	RetentionDays = Math.Max(1, securityOptions.LogRetentionDays),
	MaxFileSizeBytes = Math.Max(1, securityOptions.LogMaxFileSizeMb) * 1024L * 1024L
}));

builder.Services.AddRateLimiter(options =>
{
	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
	options.OnRejected = async (context, token) =>
	{
		var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RateLimitAudit");
		logger.LogWarning(
			"Rate limit rejected for path {Path} from {ClientIp} request {RequestId} user agent {UserAgent}",
			context.HttpContext.Request.Path.Value ?? string.Empty,
			context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			context.HttpContext.TraceIdentifier,
			context.HttpContext.Request.Headers.UserAgent.ToString());

		context.HttpContext.Response.ContentType = "application/json";
		await context.HttpContext.Response.WriteAsync("{\"message\":\"Too many requests. Please try again shortly.\"}", token);
	};

	options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
	{
		if (context.Request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase))
		{
			var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
			return RateLimitPartition.GetFixedWindowLimiter(
				$"login:{clientIp}",
				_ => new FixedWindowRateLimiterOptions
				{
					PermitLimit = 5,
					Window = TimeSpan.FromMinutes(1),
					QueueLimit = 0,
					AutoReplenishment = true
				});
		}

		if (context.Request.Path.Equals("/api/auth/password-reset/request", StringComparison.OrdinalIgnoreCase)
			|| context.Request.Path.Equals("/api/auth/forgot-username", StringComparison.OrdinalIgnoreCase))
		{
			// Same per-IP shape as login above. AuthService also rate-limits per
			// username/email (TryConsumeRateLimit), but that alone is trivially
			// bypassed by cycling through different usernames/emails from one IP —
			// this partition closes that gap.
			var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
			return RateLimitPartition.GetFixedWindowLimiter(
				$"{context.Request.Path.Value}:{clientIp}",
				_ => new FixedWindowRateLimiterOptions
				{
					PermitLimit = 5,
					Window = TimeSpan.FromMinutes(1),
					QueueLimit = 0,
					AutoReplenishment = true
				});
		}

		if (context.Request.Path.Equals("/api/books/lookup", StringComparison.OrdinalIgnoreCase)
			|| context.Request.Path.Equals("/api/books/info", StringComparison.OrdinalIgnoreCase))
		{
			// Metadata lookups fan out to third-party APIs; bound them per authenticated
			// user, falling back to client IP for unauthenticated callers. Key on the
			// resolved user id, NOT the raw auth-cookie value: a user can mint many
			// sessions (each login issues a fresh token), and an anonymous caller can send
			// any cookie value, so keying on the cookie would hand out a new bucket per
			// session/value and defeat the limit — the same cycle-the-key bypass as XFF.
			// The rate limiter runs before UseAuthentication, so read the user id straight
			// from the (tamper-proof, Data Protection encrypted) auth cookie ticket. A forged
			// or undecryptable cookie yields no user and falls back to the client IP.
			var authenticatedUserId = TryGetUserIdFromAuthCookie(context);
			var keyMaterial = authenticatedUserId is not null
				? $"user:{authenticatedUserId}"
				: $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

			return RateLimitPartition.GetFixedWindowLimiter(
				$"lookup:{keyMaterial}",
				_ => new FixedWindowRateLimiterOptions
				{
					PermitLimit = Math.Max(1, securityOptions.MetadataLookupRateLimitPerMinute),
					Window = TimeSpan.FromMinutes(1),
					QueueLimit = 0,
					AutoReplenishment = true
				});
		}

		if (context.Request.Path.Equals("/api/auth/password", StringComparison.OrdinalIgnoreCase))
		{
			// Self-service password change verifies the current password, so bound
			// guesses per user (a stolen cookie must not allow brute-forcing it).
			// Falls back to client IP when the auth cookie is missing/unreadable.
			var authenticatedUserId = TryGetUserIdFromAuthCookie(context);
			var keyMaterial = authenticatedUserId is not null
				? $"user:{authenticatedUserId}"
				: $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

			return RateLimitPartition.GetFixedWindowLimiter(
				$"password-change:{keyMaterial}",
				_ => new FixedWindowRateLimiterOptions
				{
					PermitLimit = 5,
					Window = TimeSpan.FromMinutes(1),
					QueueLimit = 0,
					AutoReplenishment = true
				});
		}

		return RateLimitPartition.GetNoLimiter("default");
	});
});

builder.Services.AddResponseCompression(options =>
{
	// This app has no reflected user-controlled secrets in compressible responses
	// (the BREACH-attack scenario response compression normally disables for HTTPS
	// by default) — it's a fixed-shape SPA shell plus JSON APIs scoped to the
	// authenticated caller's own data. Safe to enable for HTTPS here.
	options.EnableForHttps = true;
	options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
	{
		"application/javascript",
		"application/manifest+json"
	});
});

var app = builder.Build();

var migrationOptionsBuilder = new DbContextOptionsBuilder<BookWheelDbContext>();
migrationOptionsBuilder.UseNpgsql(migrationConnectionString);
await using (var startupDbContext = new BookWheelDbContext(migrationOptionsBuilder.Options))
{
	await startupDbContext.Database.MigrateAsync();
}

var assembly = Assembly.GetExecutingAssembly();
var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
var assemblyVersion = assembly.GetName().Version?.ToString();
var appVersion = informationalVersion ?? assemblyVersion ?? "unknown";

var migrationService = app.Services.GetRequiredService<DataMigrationService>();
var migrationLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DataMigration");
var runMigrationOnly = args.Any(arg => string.Equals(arg, "--migrate-data", StringComparison.OrdinalIgnoreCase));

if (runMigrationOnly)
{
	var report = await migrationService.RunAsync();
	migrationLogger.LogInformation(
		"Migration command executed. Credential migrated: {CredentialMigrated} users affected: {CredentialUsersAffected}. Books migrated: {BooksMigrated} books affected: {BooksAffected} owner user id: {OwnerUserId}",
		report.CredentialPayloadMigrated,
		report.CredentialUsersAffected,
		report.BooksPayloadMigrated,
		report.BooksAffected,
		report.BooksOwnerUserId);

	Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	}));

	return;
}

var runPostgresMigrationOnly = args.Any(arg => string.Equals(arg, "--migrate-to-postgres", StringComparison.OrdinalIgnoreCase));
if (runPostgresMigrationOnly)
{
	var postgresMigrationService = app.Services.GetRequiredService<PostgresMigrationService>();
	var postgresReport = await postgresMigrationService.RunAsync();
	Console.WriteLine(JsonSerializer.Serialize(postgresReport, new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	}));
	return;
}

var startupMigrationReport = await migrationService.RunAsync();
migrationLogger.LogInformation(
	"Startup migration completed. Credential migrated: {CredentialMigrated} users affected: {CredentialUsersAffected}. Books migrated: {BooksMigrated} books affected: {BooksAffected} owner user id: {OwnerUserId}",
	startupMigrationReport.CredentialPayloadMigrated,
	startupMigrationReport.CredentialUsersAffected,
	startupMigrationReport.BooksPayloadMigrated,
	startupMigrationReport.BooksAffected,
	startupMigrationReport.BooksOwnerUserId);

// Must run first so Request.IsHttps / RemoteIpAddress reflect the proxy headers for all later middleware.
app.UseForwardedHeaders();

{
	var forwardedLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ForwardedHeaders");
	// Count only operator-configured entries (parsed into a throwaway options instance).
	var forwardedLogSettings = builder.Configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>() ?? new ForwardedHeadersSettings();
	var (configuredNetworks, configuredProxies, _) = ForwardedHeadersConfigurator.Apply(
		new ForwardedHeadersOptions(),
		forwardedLogSettings.KnownNetworks,
		forwardedLogSettings.KnownProxies,
		Environment.GetEnvironmentVariable("FORWARDED_KNOWN_NETWORKS"));
	if (configuredNetworks + configuredProxies > 0)
	{
		forwardedLogger.LogInformation(
			"Forwarded headers trusted from {NetworkCount} configured network(s) and {ProxyCount} configured proxy(ies) in addition to loopback.",
			configuredNetworks,
			configuredProxies);
	}
	else if (!app.Environment.IsDevelopment())
	{
		forwardedLogger.LogWarning(
			"No ForwardedHeaders:KnownNetworks or KnownProxies configured. Forwarded headers are trusted only from loopback. " +
			"If this app runs behind a reverse proxy, set ForwardedHeaders:KnownNetworks (or FORWARDED_KNOWN_NETWORKS) so client IPs, rate limiting and the Secure cookie work correctly.");
	}
}

if (!app.Environment.IsDevelopment())
{
	app.UseHsts();
}

if (!app.Environment.IsEnvironment("Testing"))
{
	app.UseHttpsRedirection();
}

if (observabilityOptions.EnableRequestCorrelationLogging)
{
	app.Use(async (context, next) =>
	{
		var correlationId = context.Request.Headers.TryGetValue("X-Correlation-ID", out var incoming)
			? incoming.ToString()
			: context.TraceIdentifier;

		context.Response.Headers["X-Correlation-ID"] = correlationId;
		var requestLogger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RequestCorrelation");
		using (requestLogger.BeginScope(new Dictionary<string, object?>
		{
			["CorrelationId"] = correlationId,
			["Path"] = context.Request.Path.Value,
			["Method"] = context.Request.Method
		}))
		{
			requestLogger.LogInformation(
				"Request started {Method} {Path} correlation {CorrelationId}",
				context.Request.Method,
				context.Request.Path.Value,
				correlationId);
			await next();
			requestLogger.LogInformation(
				"Request completed {StatusCode} {Method} {Path} correlation {CorrelationId}",
				context.Response.StatusCode,
				context.Request.Method,
				context.Request.Path.Value,
				correlationId);
		}
	});
}

app.Use(async (context, next) =>
{
	var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
	context.Items["csp-nonce"] = nonce;

	context.Response.Headers["X-Content-Type-Options"] = "nosniff";
	context.Response.Headers["X-Frame-Options"] = "DENY";
	context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
	context.Response.Headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=(), browsing-topics=()";
	var cspHeaderName = securityOptions.ContentSecurityPolicyReportOnly
		? "Content-Security-Policy-Report-Only"
		: "Content-Security-Policy";
	context.Response.Headers[cspHeaderName] = BuildContentSecurityPolicy(nonce, !string.IsNullOrWhiteSpace(googleAnalyticsId));
	await next();
});
app.UseRateLimiter();
app.UseResponseCompression();

SupportedLocales.Initialize(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"));
var supportedCultures = SupportedLocales.Values.Select(c => new CultureInfo(c)).ToList();
app.UseRequestLocalization(new RequestLocalizationOptions
{
	DefaultRequestCulture = new RequestCulture(SupportedLocales.Default),
	SupportedCultures = supportedCultures,
	SupportedUICultures = supportedCultures
});
// Static assets are public and served before authentication/authorization so the
// fallback policy never applies to them.
var staticFileContentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
staticFileContentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json";

app.UseStaticFiles(new StaticFileOptions
{
	ContentTypeProvider = staticFileContentTypeProvider,
	OnPrepareResponse = context =>
	{
		// These assets are always requested with a ?v=<version> cache-busting query
		// string (see index.html/sw.js), so it's safe to cache them indefinitely —
		// a new deploy changes the URL, not the file's cached bytes.
		context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
	}
});
app.UseAuthentication();
app.UseAuthorization();

static Guid? TryGetUserIdFromAuthCookie(HttpContext context)
{
	try
	{
		var cookieOptions = context.RequestServices
			.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
			.Get(AuthDefaults.Scheme);
		var cookieValue = cookieOptions.CookieManager.GetRequestCookie(context, cookieOptions.Cookie.Name!);
		if (string.IsNullOrEmpty(cookieValue))
		{
			return null;
		}

		var ticket = cookieOptions.TicketDataFormat.Unprotect(cookieValue);
		var userId = ticket?.Principal.GetUserId();
		return userId is { } id && id != Guid.Empty ? id : null;
	}
	catch
	{
		return null;
	}
}

static string BuildContentSecurityPolicy(string nonce, bool googleAnalyticsEnabled)
{
	var analyticsConnect = googleAnalyticsEnabled
		? " https://*.google-analytics.com https://*.analytics.google.com https://www.googletagmanager.com"
		: string.Empty;
	var analyticsScript = googleAnalyticsEnabled ? " https://www.googletagmanager.com" : string.Empty;
	return "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; "
		+ "img-src 'self' https: data:; font-src 'self'; style-src 'self'; manifest-src 'self'; worker-src 'self'; "
		+ $"connect-src 'self'{analyticsConnect}; "
		+ $"script-src 'self' 'nonce-{nonce}'{analyticsScript}";
}

async Task WriteConfiguredIndexAsync(HttpContext context)
{
	var webRootPath = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
	var indexPath = Path.Combine(webRootPath, "index.html");
	var html = await File.ReadAllTextAsync(indexPath);
	html = GoogleAnalyticsHtmlInjector.Apply(html, googleAnalyticsId);
	html = html.Replace("__ASSET_VERSION__", Uri.EscapeDataString(appVersion), StringComparison.Ordinal);
	var cspNonce = context.Items["csp-nonce"] as string ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
	html = html.Replace("__CSP_NONCE__", cspNonce, StringComparison.Ordinal);

	context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
	context.Response.Headers.Pragma = "no-cache";
	context.Response.Headers.Expires = "0";
	context.Response.ContentType = "text/html; charset=utf-8";
	await context.Response.WriteAsync(html);
}

async Task WriteConfiguredServiceWorkerAsync(HttpContext context)
{
	var webRootPath = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
	var swPath = Path.Combine(webRootPath, "sw.js");
	var script = await File.ReadAllTextAsync(swPath);
	script = script.Replace("__ASSET_VERSION__", Uri.EscapeDataString(appVersion), StringComparison.Ordinal);

	context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
	context.Response.Headers.Pragma = "no-cache";
	context.Response.Headers.Expires = "0";
	context.Response.ContentType = "application/javascript; charset=utf-8";
	await context.Response.WriteAsync(script);
}

app.MapGet("/", WriteConfiguredIndexAsync).AllowAnonymous();
app.MapGet("/index.html", WriteConfiguredIndexAsync).AllowAnonymous();
app.MapGet("/sw.js", WriteConfiguredServiceWorkerAsync).AllowAnonymous();

app.MapGet("/api/version", () =>
{
	return Results.Ok(new { version = appVersion });
}).AllowAnonymous();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
	Predicate = check => check.Tags.Contains("live")
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
	Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program { }
