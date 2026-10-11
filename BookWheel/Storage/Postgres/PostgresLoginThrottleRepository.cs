using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BookWheel.Storage.Postgres;

public sealed class PostgresLoginThrottleRepository : ILoginThrottleRepository
{
	private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

	public PostgresLoginThrottleRepository(IDbContextFactory<BookWheelDbContext> contextFactory)
	{
		_contextFactory = contextFactory;
	}

	public async Task<DateTimeOffset?> GetActiveLockoutAsync(string username)
	{
		var key = LoginThrottleKey.Normalize(username);
		var now = DateTimeOffset.UtcNow;
		await using var context = await _contextFactory.CreateDbContextAsync();
		var lockedUntil = await context.LoginThrottles
			.AsNoTracking()
			.Where(x => x.NormalizedUsername == key)
			.Select(x => x.LockedUntilUtc)
			.FirstOrDefaultAsync();
		return lockedUntil.HasValue && lockedUntil.Value > now ? lockedUntil : null;
	}

	// A single INSERT ... ON CONFLICT DO UPDATE ... RETURNING statement, so the
	// increment and the threshold decision happen atomically inside Postgres (row lock
	// on conflict). Concurrent attempts across instances therefore never lose a count.
	// Caveat: GetActiveLockoutAsync + RegisterFailedAttemptAsync are separate calls, so a
	// burst of simultaneous attempts that all pass the lockout check before it is set may
	// each be counted (and a later one may re-lock); this only affects the exact moment of
	// lockout and is best-effort, as it was with the in-memory implementation.
	public async Task<LoginLockoutOutcome> RegisterFailedAttemptAsync(string username, int threshold, TimeSpan lockoutDuration)
	{
		var key = LoginThrottleKey.Normalize(username);
		var now = DateTimeOffset.UtcNow;
		var until = now.Add(lockoutDuration);
		var effectiveThreshold = Math.Max(1, threshold);

		const string sql = """
			INSERT INTO login_throttle ("NormalizedUsername", "FailedCount", "LockedUntilUtc", "UpdatedAtUtc")
			VALUES (@key,
				CASE WHEN 1 >= @threshold THEN 0 ELSE 1 END,
				CASE WHEN 1 >= @threshold THEN @until ELSE NULL END,
				@now)
			ON CONFLICT ("NormalizedUsername") DO UPDATE SET
				"FailedCount" = CASE WHEN login_throttle."FailedCount" + 1 >= @threshold THEN 0 ELSE login_throttle."FailedCount" + 1 END,
				"LockedUntilUtc" = CASE WHEN login_throttle."FailedCount" + 1 >= @threshold THEN @until ELSE login_throttle."LockedUntilUtc" END,
				"UpdatedAtUtc" = @now
			RETURNING "FailedCount", "LockedUntilUtc";
			""";

		await using var context = await _contextFactory.CreateDbContextAsync();
		var connection = (NpgsqlConnection)context.Database.GetDbConnection();
		await connection.OpenAsync();
		try
		{
			await using var command = new NpgsqlCommand(sql, connection);
			command.Parameters.AddWithValue("key", key);
			command.Parameters.AddWithValue("threshold", effectiveThreshold);
			command.Parameters.Add(new NpgsqlParameter("until", NpgsqlDbType.TimestampTz) { Value = until.UtcDateTime });
			command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now.UtcDateTime });

			await using var reader = await command.ExecuteReaderAsync();
			await reader.ReadAsync();
			var failedCount = reader.GetInt32(0);
			if (failedCount == 0)
			{
				var lockedUntil = new DateTimeOffset(reader.GetFieldValue<DateTime>(1), TimeSpan.Zero);
				return new LoginLockoutOutcome { IsLockedOut = true, LockedUntilUtc = lockedUntil };
			}

			return new LoginLockoutOutcome { IsLockedOut = false };
		}
		finally
		{
			await connection.CloseAsync();
		}
	}

	public async Task ClearAsync(string username)
	{
		var key = LoginThrottleKey.Normalize(username);
		await using var context = await _contextFactory.CreateDbContextAsync();
		await context.LoginThrottles.Where(x => x.NormalizedUsername == key).ExecuteDeleteAsync();
	}

	public async Task<int> DeleteStaleAsync(DateTimeOffset olderThanUpdatedAtUtc)
	{
		await using var context = await _contextFactory.CreateDbContextAsync();
		return await context.LoginThrottles.Where(x => x.UpdatedAtUtc < olderThanUpdatedAtUtc).ExecuteDeleteAsync();
	}
}
