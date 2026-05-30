namespace BookWheel.Models;

public sealed class BookRecord
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
}
