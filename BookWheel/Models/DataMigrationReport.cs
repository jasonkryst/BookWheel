namespace BookWheel.Models;

public sealed class DataMigrationReport
{
    public DateTimeOffset ExecutedAtUtc { get; set; }
    public bool CredentialPayloadMigrated { get; set; }
    public int CredentialUsersAffected { get; set; }
    public bool BooksPayloadMigrated { get; set; }
    public int BooksAffected { get; set; }
    public Guid? BooksOwnerUserId { get; set; }
    public string Message { get; set; } = string.Empty;
}
