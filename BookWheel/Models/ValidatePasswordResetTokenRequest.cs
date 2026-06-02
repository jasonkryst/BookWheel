using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class ValidatePasswordResetTokenRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;
}
