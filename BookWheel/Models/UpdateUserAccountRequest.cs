using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class UpdateUserAccountRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Username { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }
    public bool IsDisabled { get; set; }
    public bool ForcePasswordReset { get; set; }
    public bool IsLocked { get; set; }
}
