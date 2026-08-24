using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class UpdateBookRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [StringLength(20)]
    public string? Isbn { get; set; }

    [StringLength(300)]
    public string? Author { get; set; }

    [StringLength(2048)]
    public string? CoverUrl { get; set; }

    public bool AddedByScanner { get; set; }
}
