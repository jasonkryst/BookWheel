using BookWheel.Services;

namespace BookWheel.Tests.Services;

public sealed class SessionAbsoluteLifetimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsBeyondAbsoluteLifetime_Is_True_When_Issued_At_Is_Missing()
    {
        Assert.True(AuthDefaults.IsBeyondAbsoluteLifetime(null, Now, AuthDefaults.AbsoluteSessionLifetime));
    }

    [Fact]
    public void IsBeyondAbsoluteLifetime_Is_False_For_A_Recently_Issued_Ticket()
    {
        Assert.False(AuthDefaults.IsBeyondAbsoluteLifetime(Now - TimeSpan.FromHours(1), Now, AuthDefaults.AbsoluteSessionLifetime));
    }

    [Fact]
    public void IsBeyondAbsoluteLifetime_Is_False_Just_Inside_The_Cap()
    {
        Assert.False(AuthDefaults.IsBeyondAbsoluteLifetime(Now - AuthDefaults.AbsoluteSessionLifetime + TimeSpan.FromMinutes(1), Now, AuthDefaults.AbsoluteSessionLifetime));
    }

    [Fact]
    public void IsBeyondAbsoluteLifetime_Is_True_Just_Past_The_Cap()
    {
        Assert.True(AuthDefaults.IsBeyondAbsoluteLifetime(Now - AuthDefaults.AbsoluteSessionLifetime - TimeSpan.FromMinutes(1), Now, AuthDefaults.AbsoluteSessionLifetime));
    }

    [Fact]
    public void IsBeyondAbsoluteLifetime_Default_Cap_Is_24_Hours()
    {
        Assert.Equal(TimeSpan.FromHours(24), AuthDefaults.AbsoluteSessionLifetime);
    }

    [Fact]
    public void IsBeyondAbsoluteLifetime_Honors_The_Supplied_Lifetime_Rather_Than_The_Idle_Window()
    {
        var lifetime = TimeSpan.FromHours(24);

        // Older than the 8h idle window but inside the 24h cap: an active session survives.
        Assert.False(AuthDefaults.IsBeyondAbsoluteLifetime(Now - TimeSpan.FromHours(9), Now, lifetime));
        Assert.True(AuthDefaults.IsBeyondAbsoluteLifetime(Now - TimeSpan.FromHours(25), Now, lifetime));
        Assert.True(AuthDefaults.IsBeyondAbsoluteLifetime(Now - TimeSpan.FromHours(9), Now, TimeSpan.FromHours(8)));
    }
}
