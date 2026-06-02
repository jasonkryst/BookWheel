using BookWheel.HealthChecks;
using BookWheel.Logging;
using BookWheel.Models;
using BookWheel.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
builder.Services.Configure<ObservabilityOptions>(builder.Configuration.GetSection(ObservabilityOptions.SectionName));
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<AppMetricsService>();
builder.Services.AddSingleton<CredentialStore>();
builder.Services.AddSingleton<BookStore>();
builder.Services.AddSingleton<DataMigrationService>();
builder.Services.AddControllers();
builder.Services.AddHttpClient("central-log-shipper");
builder.Services.AddHostedService<StartupDiagnosticsService>();
builder.Services.AddHostedService<LogShippingService>();
builder.Services.AddHealthChecks()
	.AddCheck<StorageHealthCheck>("storage", tags: ["ready"])
	.AddCheck<LoggingHealthCheck>("logging", tags: ["ready"])
	.AddCheck("app", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("App is running."), tags: ["live", "ready"]);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
	options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
	options.KnownNetworks.Clear();
	options.KnownProxies.Clear();
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

		return RateLimitPartition.GetNoLimiter("default");
	});
});

var app = builder.Build();

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

var startupMigrationReport = await migrationService.RunAsync();
migrationLogger.LogInformation(
	"Startup migration completed. Credential migrated: {CredentialMigrated} users affected: {CredentialUsersAffected}. Books migrated: {BooksMigrated} books affected: {BooksAffected} owner user id: {OwnerUserId}",
	startupMigrationReport.CredentialPayloadMigrated,
	startupMigrationReport.CredentialUsersAffected,
	startupMigrationReport.BooksPayloadMigrated,
	startupMigrationReport.BooksAffected,
	startupMigrationReport.BooksOwnerUserId);

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

app.UseForwardedHeaders();
app.UseRateLimiter();

async Task WriteConfiguredIndexAsync(HttpContext context)
{
	var webRootPath = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
	var indexPath = Path.Combine(webRootPath, "index.html");
	var html = await File.ReadAllTextAsync(indexPath);
	html = html.Replace("__GOOGLE_ANALYTICS_ID__", googleAnalyticsId, StringComparison.Ordinal);

	context.Response.ContentType = "text/html; charset=utf-8";
	await context.Response.WriteAsync(html);
}

app.MapGet("/", WriteConfiguredIndexAsync);
app.MapGet("/index.html", WriteConfiguredIndexAsync);

app.UseStaticFiles();
app.MapGet("/api/version", () =>
{
	var assembly = Assembly.GetExecutingAssembly();
	var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
	var assemblyVersion = assembly.GetName().Version?.ToString();
	var resolvedVersion = informationalVersion ?? assemblyVersion ?? "unknown";

	return Results.Ok(new { version = resolvedVersion });
});
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
	Predicate = check => check.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
	Predicate = check => check.Tags.Contains("ready")
});
app.MapControllers();

app.Run();

public partial class Program { }
