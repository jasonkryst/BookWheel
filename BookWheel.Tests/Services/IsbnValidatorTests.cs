using BookWheel.Services;

namespace BookWheel.Tests.Services;

public sealed class IsbnValidatorTests
{
    [Theory]
    [InlineData("0201558025")]
    [InlineData("0-201-55802-5")]
    [InlineData("0 201 55802 5")]
    public void TryNormalize_Accepts_Valid_Isbn10_Ignoring_Separators(string raw)
    {
        var accepted = IsbnValidator.TryNormalize(raw, out var normalized);

        Assert.True(accepted);
        Assert.Equal("0201558025", normalized);
    }

    [Fact]
    public void TryNormalize_Accepts_Valid_Isbn10_With_X_Check_Digit()
    {
        var accepted = IsbnValidator.TryNormalize("155860832x", out var normalized);

        Assert.True(accepted);
        Assert.Equal("155860832X", normalized);
    }

    [Theory]
    [InlineData("9780134685991")]
    [InlineData("978-0-13-468599-1")]
    [InlineData("978 0 13 468599 1")]
    public void TryNormalize_Accepts_Valid_Isbn13_Ignoring_Separators(string raw)
    {
        var accepted = IsbnValidator.TryNormalize(raw, out var normalized);

        Assert.True(accepted);
        Assert.Equal("9780134685991", normalized);
    }

    [Theory]
    [InlineData("0201558026")]
    [InlineData("9780134685992")]
    [InlineData("12345")]
    [InlineData("abcdefghij")]
    [InlineData("97801346859912")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryNormalize_Rejects_Invalid_Or_Malformed_Input(string? raw)
    {
        var accepted = IsbnValidator.TryNormalize(raw, out var normalized);

        Assert.False(accepted);
        Assert.Equal(string.Empty, normalized);
    }
}
