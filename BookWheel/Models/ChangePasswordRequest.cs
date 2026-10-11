using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class ChangePasswordRequest
{
    [Required(ErrorMessage = "Password is required.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    public string NewPassword { get; set; } = string.Empty;
}
