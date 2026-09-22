using BookWheel.Models;
using BookWheel.Storage;

namespace BookWheel.Services;

public sealed class AppMetricsService
{
    private long _loginFailureCount;
    private long _loginLockoutCount;
    private long _successfulLoginCount;
    private long _spinsSinceRestart;

    public void IncrementLoginFailure()
    {
        Interlocked.Increment(ref _loginFailureCount);
    }

    public void IncrementLoginLockout()
    {
        Interlocked.Increment(ref _loginLockoutCount);
    }

    public void IncrementSuccessfulLogin()
    {
        Interlocked.Increment(ref _successfulLoginCount);
    }

    public void IncrementSpinsSinceRestart()
    {
        Interlocked.Increment(ref _spinsSinceRestart);
    }

    public async Task<MetricsSnapshot> GetSnapshotAsync(IBookRepository bookRepository)
    {
        return new MetricsSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            LoginFailureCount = Interlocked.Read(ref _loginFailureCount),
            LoginLockoutCount = Interlocked.Read(ref _loginLockoutCount),
            SuccessfulLoginCount = Interlocked.Read(ref _successfulLoginCount),
            SpinsSinceRestart = Interlocked.Read(ref _spinsSinceRestart),
            TotalBookCount = await bookRepository.GetTotalBookCountAsync()
        };
    }
}
