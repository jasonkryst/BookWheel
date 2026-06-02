namespace BookWheel.Models;

public sealed class AuthenticatedUser
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}
