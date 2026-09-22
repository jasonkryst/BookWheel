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
}
