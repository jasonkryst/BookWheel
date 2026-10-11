namespace BookWheel.Models;

public sealed class ForwardedHeadersSettings
{
	public const string SectionName = "ForwardedHeaders";

	/// <summary>CIDR ranges (e.g. 172.16.0.0/12) of reverse proxies whose X-Forwarded-* headers are trusted.</summary>
	public string[] KnownNetworks { get; set; } = [];

	/// <summary>Single proxy IP addresses whose X-Forwarded-* headers are trusted.</summary>
	public string[] KnownProxies { get; set; } = [];
}
