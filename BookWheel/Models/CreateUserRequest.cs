using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class CreateUserRequest
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(64, MinimumLength = 1, ErrorMessage = "Username must be between 1 and 64 characters.")]
    public string Username { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }
}
