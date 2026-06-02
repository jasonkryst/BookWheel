namespace BookWheel.Models;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public int LogRetentionDays { get; set; } = 14;
    public int LogMaxFileSizeMb { get; set; } = 5;
    public int UsernameLockoutThreshold { get; set; } = 5;
    public int UsernameLockoutMinutes { get; set; } = 3;
}
