using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class CreateUserRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }
}
