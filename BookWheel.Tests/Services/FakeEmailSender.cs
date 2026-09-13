using BookWheel.Services;

namespace BookWheel.Tests.Services;

public sealed class FakeEmailSender : IEmailSender
{
    public List<(string ToAddress, string Subject, string Body)> SentEmails { get; } = [];

    public Task SendAsync(string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken = default)
    {
        SentEmails.Add((toAddress, subject, plainTextBody));
        return Task.CompletedTask;
    }
}
