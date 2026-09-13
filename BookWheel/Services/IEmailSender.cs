namespace BookWheel.Services;

public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken = default);
}
