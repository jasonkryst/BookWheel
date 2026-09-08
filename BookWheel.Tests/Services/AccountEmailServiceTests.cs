using BookWheel.Services;

namespace BookWheel.Tests.Services;

public sealed class AccountEmailServiceTests
{
    [Fact]
    public async Task SendPasswordResetEmailAsync_Sends_Email_With_Link_And_Expiry()
    {
        var fakeSender = new FakeEmailSender();
        var service = new AccountEmailService(fakeSender);
        var expiresAt = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        await service.SendPasswordResetEmailAsync("reader@example.com", "reader-one", "https://bookwheel.example/?resetToken=abc123", expiresAt);

        var sent = Assert.Single(fakeSender.SentEmails);
        Assert.Equal("reader@example.com", sent.ToAddress);
        Assert.Contains("reader-one", sent.Body, StringComparison.Ordinal);
        Assert.Contains("https://bookwheel.example/?resetToken=abc123", sent.Body, StringComparison.Ordinal);
        Assert.Contains("2026-09-09", sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendForgottenUsernameEmailAsync_Sends_Email_With_Username()
    {
        var fakeSender = new FakeEmailSender();
        var service = new AccountEmailService(fakeSender);

        await service.SendForgottenUsernameEmailAsync("reader@example.com", "reader-one");

        var sent = Assert.Single(fakeSender.SentEmails);
        Assert.Equal("reader@example.com", sent.ToAddress);
        Assert.Contains("reader-one", sent.Body, StringComparison.Ordinal);
    }
}
