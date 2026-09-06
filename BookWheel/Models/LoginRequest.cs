using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(64, MinimumLength = 1, ErrorMessage = "Username must be between 1 and 64 characters.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    public string Password { get; set; } = string.Empty;
}
