namespace BookWheel.Storage.Postgres.Entities;

public sealed class SpinSelectionEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public DateTimeOffset SelectedAtUtc { get; set; }
}
