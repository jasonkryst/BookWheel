using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class CompletePasswordResetRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;
}
