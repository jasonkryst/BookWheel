namespace BookWheel.Models;

public sealed class PostgresMigrationReport
{
    public DateTimeOffset ExecutedAtUtc { get; set; }
    public int UsersMigrated { get; set; }
    public int BooksMigrated { get; set; }
    public int PasswordResetTokensMigrated { get; set; }
    public string Message { get; set; } = string.Empty;
}
