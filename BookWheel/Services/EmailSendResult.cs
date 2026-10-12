namespace BookWheel.Services;

public sealed record EmailSendResult(bool Success, string? Error)
{
    public static EmailSendResult Ok() => new(true, null);

    public static EmailSendResult Fail(string error) => new(false, error);
}
