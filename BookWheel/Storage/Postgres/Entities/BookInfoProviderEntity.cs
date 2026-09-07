namespace BookWheel.Storage.Postgres.Entities;

public sealed class BookInfoProviderEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
