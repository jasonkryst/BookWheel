namespace BookWheel.Tests.Scripts;

/// <summary>
/// GH #198: the 47 KB (it had grown to 61 KB by the time this shipped) README
/// mixed pitch/quickstart with the full user/admin/self-hosting reference and
/// GH-numbered changelog history. These checks pin the split so a future change
/// can't silently let README.md grow back into that state or drop a doc file.
/// </summary>
public sealed class DocumentationStructureTests
{
    private static readonly string RepoRoot = LocateRepoRoot();

    [Fact]
    public void Readme_Is_Under_8Kb()
    {
        var readme = ReadRepoFile("README.md");

        // Positive: the README split's explicit acceptance criterion.
        Assert.True(readme.Length < 8 * 1024, $"README.md is {readme.Length} bytes; expected under 8 KB.");
    }

    [Fact]
    public void Readme_Links_To_Every_Split_Doc()
    {
        var readme = ReadRepoFile("README.md");

        // Positive: a reader landing on the README can find every doc that now
        // holds the content that used to live inline.
        Assert.Contains("docs/user-guide.md", readme, StringComparison.Ordinal);
        Assert.Contains("docs/admin-guide.md", readme, StringComparison.Ordinal);
        Assert.Contains("docs/self-hosting.md", readme, StringComparison.Ordinal);
        Assert.Contains("docs/architecture.md", readme, StringComparison.Ordinal);
        Assert.Contains("CHANGELOG.md", readme, StringComparison.Ordinal);

        // Negative: content that moved out must not still be duplicated inline
        // (a stale copy would drift from the real doc and double the README's
        // size right back up).
        Assert.DoesNotContain("## Reverse proxy and forwarded headers", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("## Troubleshooting", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("## API Overview", readme, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("docs/user-guide.md")]
    [InlineData("docs/admin-guide.md")]
    [InlineData("docs/self-hosting.md")]
    [InlineData("docs/architecture.md")]
    [InlineData("CHANGELOG.md")]
    public void Split_Doc_File_Exists_And_Is_Not_A_Stub(string relativePath)
    {
        var content = ReadRepoFile(relativePath);

        Assert.True(content.Length > 500, $"{relativePath} is only {content.Length} bytes; expected real content, not a stub.");
    }

    [Fact]
    public void SelfHosting_Guide_Has_A_Single_Consolidated_Env_Var_Reference_Table()
    {
        var doc = ReadRepoFile("docs/self-hosting.md");

        // Positive: GH #198's other acceptance criterion — one table, not
        // settings scattered across prose in several sections.
        Assert.Contains("## Environment Variable Reference", doc, StringComparison.Ordinal);

        var tableStart = doc.IndexOf("## Environment Variable Reference", StringComparison.Ordinal);
        var tableSection = doc.Substring(tableStart, Math.Min(6000, doc.Length - tableStart));

        Assert.Contains("ConnectionStrings:BookWheel", tableSection, StringComparison.Ordinal);
        Assert.Contains("Smtp:Host", tableSection, StringComparison.Ordinal);
        Assert.Contains("ForwardedHeaders:KnownNetworks", tableSection, StringComparison.Ordinal);
        Assert.Contains("Security:AlwaysSecureCookie", tableSection, StringComparison.Ordinal);
        Assert.Contains("Analytics:GoogleAnalyticsId", tableSection, StringComparison.Ordinal);
        Assert.Contains("BookMetadata:GoogleBooks:ApiKey", tableSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Changelog_Is_Introduced_With_Versioned_Ghnumbered_Entries()
    {
        var changelog = ReadRepoFile("CHANGELOG.md");

        // Positive: GH #198's third acceptance criterion.
        Assert.Contains("# Changelog", changelog, StringComparison.Ordinal);
        Assert.Contains("GH #232", changelog, StringComparison.Ordinal);
        Assert.Contains("GH #197", changelog, StringComparison.Ordinal);
        Assert.Contains("## [3.15.0]", changelog, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath));

    private static string LocateRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BookWheel.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root (BookWheel.slnx) relative to the test assembly.");
    }
}
