using BookWheel.Logging;
using BookWheel.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<CredentialStore>();
builder.Services.AddSingleton<BookStore>();
builder.Services.AddControllers();

var logDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "logs");
builder.Logging.AddProvider(new JsonFileLoggerProvider(logDirectory));

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

if (!app.Environment.IsDevelopment())
{
	app.UseHsts();
}

if (!app.Environment.IsEnvironment("Testing"))
{
	app.UseHttpsRedirection();
}

app.UseRateLimiter();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

app.Run();

public partial class Program { }
