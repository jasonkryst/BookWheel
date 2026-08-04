using BookWheel.Models;
using BookWheel.Storage;

namespace BookWheel.Services;

public sealed class AppMetricsService
{
    private long _loginFailureCount;
    private long _loginLockoutCount;
    private long _successfulLoginCount;
    private long _spinCount;

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

    public void IncrementSpinCount()
    {
        Interlocked.Increment(ref _spinCount);
    }

    public async Task<MetricsSnapshot> GetSnapshotAsync(IBookRepository bookRepository)
    {
        return new MetricsSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            LoginFailureCount = Interlocked.Read(ref _loginFailureCount),
            LoginLockoutCount = Interlocked.Read(ref _loginLockoutCount),
            SuccessfulLoginCount = Interlocked.Read(ref _successfulLoginCount),
            SpinCount = Interlocked.Read(ref _spinCount),
            TotalBookCount = await bookRepository.GetTotalBookCountAsync()
        };
    }
}
