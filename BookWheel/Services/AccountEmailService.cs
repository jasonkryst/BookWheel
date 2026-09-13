namespace BookWheel.Services;

public sealed class AccountEmailService
{
    private readonly IEmailSender _emailSender;

    public AccountEmailService(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    public Task SendPasswordResetEmailAsync(string toAddress, string username, string resetLink, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
    {
        const string subject = "Reset your Book Wheel password";
        var body =
            $"Hello {username},\n\n" +
            "A password reset was requested for your Book Wheel account. Use the link below to set a new password:\n\n" +
            $"{resetLink}\n\n" +
            $"This link expires at {expiresAtUtc:u}.\n\n" +
            "If you did not request this, you can safely ignore this email.";

        return _emailSender.SendAsync(toAddress, subject, body, cancellationToken);
    }

    public Task SendForgottenUsernameEmailAsync(string toAddress, string username, CancellationToken cancellationToken = default)
    {
        const string subject = "Your Book Wheel username";
        var body =
            "Hello,\n\n" +
            "You (or someone using this email address) requested a reminder of your Book Wheel username.\n\n" +
            $"Your username is: {username}\n\n" +
            "If you did not request this, you can safely ignore this email.";

        return _emailSender.SendAsync(toAddress, subject, body, cancellationToken);
    }
}
