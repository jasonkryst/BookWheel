namespace BookWheel.Services;

public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="SendAsync"/> but reports success/failure. Never throws.</summary>
    Task<EmailSendResult> TrySendAsync(string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken = default);
}
