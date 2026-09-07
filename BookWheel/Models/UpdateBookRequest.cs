using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class UpdateBookRequest
{
    [Required(ErrorMessage = "Book title is required.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Book title must be between 1 and 200 characters.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "ISBN must be 20 characters or fewer.")]
    public string? Isbn { get; set; }

    [StringLength(300, ErrorMessage = "Author must be 300 characters or fewer.")]
    public string? Author { get; set; }

    [StringLength(2048, ErrorMessage = "Cover URL must be 2048 characters or fewer.")]
    public string? CoverUrl { get; set; }

    public bool AddedByScanner { get; set; }

    [Range(1, 3, ErrorMessage = "Book type must be a valid type.")]
    public int BookTypeId { get; set; } = 1;

    [Range(1, 2, ErrorMessage = "Book info provider must be a valid provider.")]
    public int? BookInfoProviderId { get; set; }
}
