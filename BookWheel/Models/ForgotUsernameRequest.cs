using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class ForgotUsernameRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    [StringLength(256, ErrorMessage = "Email must be 256 characters or fewer.")]
    public string Email { get; set; } = string.Empty;
}
