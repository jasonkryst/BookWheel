using BookWheel.Logging;

namespace BookWheel.Tests.Logging;

public sealed class LogSanitizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("alice", "alice")]
    [InlineData("alice@example.com", "alice@example.com")]
    public void Sanitize_Returns_Safe_Value_Unchanged(string? input, string expected)
    {
        Assert.Equal(expected, LogSanitizer.Sanitize(input));
    }

    [Theory]
    [InlineData("alice\nbob", "alice_bob")]
    [InlineData("alice\rbob", "alice_bob")]
    [InlineData("alice\r\nbob", "alice__bob")]
    [InlineData("badcontrol", "bad_control")]
    [InlineData("\nalert(1)", "_alert(1)")]
    public void Sanitize_Replaces_Control_Characters(string input, string expected)
    {
        Assert.Equal(expected, LogSanitizer.Sanitize(input));
    }

    [Fact]
    public void Sanitize_Preserves_Normal_Unicode()
    {
        const string value = "Ændrée café 日本語";
        Assert.Equal(value, LogSanitizer.Sanitize(value));
    }

    [Theory]
    [InlineData("reader@example.com", "r***@example.com")]
    [InlineData("a@b.co", "a***@b.co")]
    public void MaskEmail_Keeps_First_Char_And_Domain(string input, string expected)
    {
        Assert.Equal(expected, LogSanitizer.MaskEmail(input));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("not-an-email", "***")]
    [InlineData("@example.com", "***")]
    [InlineData("trailing@", "***")]
    public void MaskEmail_Handles_Missing_Or_Malformed_Addresses(string? input, string expected)
    {
        Assert.Equal(expected, LogSanitizer.MaskEmail(input));
    }

    [Fact]
    public void MaskEmail_Sanitizes_Control_Characters_In_Domain()
    {
        Assert.Equal("r***@ex_ample.com", LogSanitizer.MaskEmail("reader@ex\nample.com"));
    }
}
