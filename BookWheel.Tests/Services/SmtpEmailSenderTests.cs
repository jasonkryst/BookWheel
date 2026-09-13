using BookWheel.Models;
using BookWheel.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookWheel.Tests.Services;

public sealed class SmtpEmailSenderTests
{
    [Fact]
    public async Task SendAsync_When_Host_Not_Configured_Does_Not_Throw()
    {
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions()), NullLogger<SmtpEmailSender>.Instance);

        var exception = await Record.ExceptionAsync(() => sender.SendAsync("someone@example.com", "Subject", "Body"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendAsync_When_Connection_Fails_Does_Not_Throw()
    {
        // Port 1 on loopback has no listener in any normal environment, so this
        // deterministically exercises the real MailKit connect-failure path
        // (connection refused) without needing a live or fake SMTP server.
        var options = new EmailOptions
        {
            Host = "127.0.0.1",
            Port = 1,
            FromAddress = "noreply@bookwheel.example",
            FromName = "Book Wheel"
        };
        var sender = new SmtpEmailSender(Options.Create(options), NullLogger<SmtpEmailSender>.Instance);

        var exception = await Record.ExceptionAsync(() => sender.SendAsync("someone@example.com", "Subject", "Body"));

        Assert.Null(exception);
    }
}
