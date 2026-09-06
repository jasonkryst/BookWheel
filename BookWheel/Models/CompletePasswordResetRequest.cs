using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class CompletePasswordResetRequest
{
    [Required(ErrorMessage = "A reset token is required.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    public string NewPassword { get; set; } = string.Empty;
}
