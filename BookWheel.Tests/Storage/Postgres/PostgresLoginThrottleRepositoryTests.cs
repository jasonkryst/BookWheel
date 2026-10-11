using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;
using BookWheel.Storage.Postgres;
using BookWheel.Storage.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BookWheel.Tests.Storage.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class PostgresLoginThrottleRepositoryTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private IDbContextFactory<BookWheelDbContext> _contextFactory = null!;
    private PostgresLoginThrottleRepository _repository = null!;

    public PostgresLoginThrottleRepositoryTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<BookWheelDbContext>(options => options.UseNpgsql(_fixture.ConnectionString));
        _contextFactory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookWheelDbContext>>();
        _repository = new PostgresLoginThrottleRepository(_contextFactory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<LoginThrottleEntity?> FindRowAsync(string key)
    {
        await using var context = _fixture.CreateContext();
        return await context.LoginThrottles.AsNoTracking().FirstOrDefaultAsync(x => x.NormalizedUsername == key);
    }

    [Fact]
    public async Task RegisterFailedAttemptAsync_Below_Threshold_Increments_Without_Lockout()
    {
        var first = await _repository.RegisterFailedAttemptAsync("Alice", 3, TimeSpan.FromMinutes(5));
        var second = await _repository.RegisterFailedAttemptAsync("alice", 3, TimeSpan.FromMinutes(5));

        Assert.False(first.IsLockedOut);
        Assert.False(second.IsLockedOut);
        Assert.Null(await _repository.GetActiveLockoutAsync("alice"));

        var row = await FindRowAsync("alice");
        Assert.NotNull(row);
        Assert.Equal(2, row!.FailedCount);
        Assert.Null(row.LockedUntilUtc);
    }

    [Fact]
    public async Task RegisterFailedAttemptAsync_Reaching_Threshold_Locks_And_Resets_Count()
    {
        await _repository.RegisterFailedAttemptAsync("alice", 2, TimeSpan.FromMinutes(5));
        var outcome = await _repository.RegisterFailedAttemptAsync("  ALICE ", 2, TimeSpan.FromMinutes(5));

        Assert.True(outcome.IsLockedOut);
        Assert.NotNull(outcome.LockedUntilUtc);
        Assert.True(outcome.LockedUntilUtc > DateTimeOffset.UtcNow.AddMinutes(4));

        var row = await FindRowAsync("alice");
        Assert.Equal(0, row!.FailedCount);
        Assert.NotNull(row.LockedUntilUtc);

        var active = await _repository.GetActiveLockoutAsync("Alice");
        Assert.NotNull(active);
        Assert.True(active > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GetActiveLockoutAsync_Returns_Null_After_Lockout_Expires()
    {
        await using (var context = _fixture.CreateContext())
        {
            context.LoginThrottles.Add(new LoginThrottleEntity
            {
                NormalizedUsername = "expired",
                FailedCount = 0,
                LockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
            });
            await context.SaveChangesAsync();
        }

        Assert.Null(await _repository.GetActiveLockoutAsync("expired"));
    }

    [Fact]
    public async Task RegisterFailedAttemptAsync_Is_Atomic_Under_Concurrency()
    {
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _repository.RegisterFailedAttemptAsync("racer", 1000, TimeSpan.FromMinutes(5)));
        await Task.WhenAll(tasks);

        var row = await FindRowAsync("racer");
        Assert.Equal(10, row!.FailedCount);
    }

    [Fact]
    public async Task ClearAsync_Removes_Record()
    {
        await _repository.RegisterFailedAttemptAsync("bob", 2, TimeSpan.FromMinutes(5));
        await _repository.RegisterFailedAttemptAsync("bob", 2, TimeSpan.FromMinutes(5));
        Assert.NotNull(await _repository.GetActiveLockoutAsync("bob"));

        await _repository.ClearAsync("BOB");

        Assert.Null(await FindRowAsync("bob"));
        Assert.Null(await _repository.GetActiveLockoutAsync("bob"));
    }

    [Fact]
    public async Task DeleteStaleAsync_Deletes_Only_Rows_Older_Than_Cutoff()
    {
        var now = DateTimeOffset.UtcNow;
        await using (var context = _fixture.CreateContext())
        {
            context.LoginThrottles.AddRange(
                new LoginThrottleEntity { NormalizedUsername = "old", FailedCount = 1, UpdatedAtUtc = now.AddDays(-3) },
                new LoginThrottleEntity { NormalizedUsername = "recent", FailedCount = 1, UpdatedAtUtc = now.AddHours(-1) });
            await context.SaveChangesAsync();
        }

        var removed = await _repository.DeleteStaleAsync(now.AddDays(-1));

        Assert.Equal(1, removed);
        Assert.Null(await FindRowAsync("old"));
        Assert.NotNull(await FindRowAsync("recent"));
    }

    private sealed class NoOpEmailSender : IEmailSender
    {
        public Task SendAsync(string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private AuthService CreateAuthService(int threshold = 3)
    {
        // A new AuthService (and repository instances) each call simulates a process restart:
        // nothing is shared with earlier instances except the database.
        var credentials = new PostgresCredentialRepository(_contextFactory);
        var resetTokens = new PostgresPasswordResetTokenRepository(_contextFactory);
        var throttle = new PostgresLoginThrottleRepository(_contextFactory);
        var options = Options.Create(new SecurityOptions { UsernameLockoutThreshold = threshold, UsernameLockoutMinutes = 5 });
        return new AuthService(credentials, resetTokens, throttle, options, new AccountEmailService(new NoOpEmailSender()));
    }

    [Fact]
    public async Task AuthService_Lockout_Survives_Simulated_Restart()
    {
        var credentials = new PostgresCredentialRepository(_contextFactory);
        await credentials.CreateInitialAccountAsync("reader", "correct-password", "reader@example.com");

        var first = CreateAuthService(threshold: 3);
        Assert.True((await first.ValidateCredentialsAsync("reader", "bad-1")).IsInvalidCredentials);
        Assert.True((await first.ValidateCredentialsAsync("reader", "bad-2")).IsInvalidCredentials);
        var triggered = await first.ValidateCredentialsAsync("reader", "bad-3");
        Assert.True(triggered.LockoutTriggered);
        Assert.True(triggered.IsLockedOut);
        Assert.NotNull(triggered.LockoutEndsAtUtc);

        var restarted = CreateAuthService(threshold: 3);
        var result = await restarted.ValidateCredentialsAsync("READER", "correct-password");

        Assert.True(result.IsLockedOut);
        Assert.False(result.LockoutTriggered);
        Assert.Null(result.User);
        Assert.NotNull(result.LockoutEndsAtUtc);
    }

    [Fact]
    public async Task AuthService_Unknown_Username_Can_Be_Locked_And_Lockout_Persists()
    {
        var first = CreateAuthService(threshold: 2);
        var one = await first.ValidateCredentialsAsync("no-such-user", "whatever");
        Assert.True(one.IsInvalidCredentials);
        Assert.False(one.IsLockedOut);

        var two = await first.ValidateCredentialsAsync("no-such-user", "whatever");
        Assert.True(two.IsInvalidCredentials);
        Assert.True(two.IsLockedOut);
        Assert.True(two.LockoutTriggered);

        var restarted = CreateAuthService(threshold: 2);
        var after = await restarted.ValidateCredentialsAsync("no-such-user", "whatever");
        Assert.True(after.IsLockedOut);
        Assert.False(after.IsInvalidCredentials);
    }

    [Fact]
    public async Task AuthService_Successful_Login_Clears_Failed_Count()
    {
        var credentials = new PostgresCredentialRepository(_contextFactory);
        await credentials.CreateInitialAccountAsync("reader", "correct-password", "reader@example.com");
        var service = CreateAuthService(threshold: 3);

        await service.ValidateCredentialsAsync("reader", "bad-1");
        await service.ValidateCredentialsAsync("reader", "bad-2");
        var ok = await service.ValidateCredentialsAsync("reader", "correct-password");
        Assert.NotNull(ok.User);
        Assert.Null(await FindRowAsync("reader"));

        // Count was cleared, so two more failures do not lock a threshold-3 account.
        Assert.False((await service.ValidateCredentialsAsync("reader", "bad-3")).IsLockedOut);
        Assert.False((await service.ValidateCredentialsAsync("reader", "bad-4")).IsLockedOut);
    }

    [Fact]
    public async Task AuthService_Lockout_Is_Keyed_By_Resolved_Account_Across_Spellings()
    {
        // The lockout protects an account, so failures spelled differently for the same
        // account must share one bucket — not split across per-spelling buckets. Guards the
        // fix for the citext-vs-ToLowerInvariant parser differential.
        var credentials = new PostgresCredentialRepository(_contextFactory);
        await credentials.CreateInitialAccountAsync("Reader", "correct-password", "reader@example.com");
        var service = CreateAuthService(threshold: 2);

        Assert.False((await service.ValidateCredentialsAsync("reader", "bad")).IsLockedOut);
        Assert.True((await service.ValidateCredentialsAsync("READER", "bad")).LockoutTriggered);
        Assert.True((await service.ValidateCredentialsAsync("ReAdEr", "correct-password")).IsLockedOut);
    }
}
