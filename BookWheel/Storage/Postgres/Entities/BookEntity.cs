namespace BookWheel.Storage.Postgres.Entities;

public sealed class BookEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
}
