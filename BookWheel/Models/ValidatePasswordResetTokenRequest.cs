using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class ValidatePasswordResetTokenRequest
{
    [Required(ErrorMessage = "A reset token is required.")]
    public string Token { get; set; } = string.Empty;
}
