using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class UpdateBookRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;
}
