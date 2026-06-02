namespace BookWheel.Models;

public sealed class DataMigrationStatus
{
    public bool HasLegacyCredentialPayload { get; set; }
    public bool HasLegacyBooksPayload { get; set; }
    public bool RequiresMigration => HasLegacyCredentialPayload || HasLegacyBooksPayload;
}
