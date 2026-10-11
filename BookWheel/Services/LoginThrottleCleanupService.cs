using BookWheel.Storage;

namespace BookWheel.Services;

// The login throttle table is keyed by arbitrary usernames (including ones that do not
// exist), so it needs periodic pruning to stay bounded.
public sealed class LoginThrottleCleanupService : BackgroundService
{
	private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
	private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

	private readonly ILoginThrottleRepository _repository;
	private readonly ILogger<LoginThrottleCleanupService> _logger;

	public LoginThrottleCleanupService(ILoginThrottleRepository repository, ILogger<LoginThrottleCleanupService> logger)
	{
		_repository = repository;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await Task.Delay(InitialDelay, stoppingToken);
			while (!stoppingToken.IsCancellationRequested)
			{
				await CleanupOnceAsync();
				await Task.Delay(Interval, stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
			// Shutting down.
		}
	}

	internal async Task CleanupOnceAsync()
	{
		try
		{
			var removed = await _repository.DeleteStaleAsync(DateTimeOffset.UtcNow - Retention);
			if (removed > 0)
			{
				_logger.LogInformation("Login throttle cleanup removed {Count} stale record(s).", removed);
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Login throttle cleanup failed; will retry on the next cycle.");
		}
	}
}
