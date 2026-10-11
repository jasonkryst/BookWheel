using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace BookWheel.Services;

public static class ForwardedHeadersConfigurator
{
	private static readonly char[] CsvSeparators = [',', ';', ' ', '\t', '\r', '\n'];

	/// <summary>
	/// Adds operator-configured trusted proxies to <paramref name="options"/>. Loopback defaults are kept.
	/// Invalid entries are skipped, never thrown. Returns the counts of networks and proxies added and entries skipped.
	/// </summary>
	public static (int networks, int proxies, int skipped) Apply(
		ForwardedHeadersOptions options,
		IEnumerable<string>? knownNetworks,
		IEnumerable<string>? knownProxies,
		string? extraNetworksCsv = null)
	{
		var networks = 0;
		var proxies = 0;
		var skipped = 0;

		var networkEntries = (knownNetworks ?? [])
			.Concat((extraNetworksCsv ?? string.Empty).Split(CsvSeparators, StringSplitOptions.RemoveEmptyEntries));

		foreach (var raw in networkEntries)
		{
			var entry = raw?.Trim();
			if (string.IsNullOrEmpty(entry))
			{
				continue;
			}

			if (System.Net.IPNetwork.TryParse(entry, out var network))
			{
				options.KnownIPNetworks.Add(network);
				networks++;
			}
			else
			{
				skipped++;
			}
		}

		foreach (var raw in knownProxies ?? [])
		{
			var entry = raw?.Trim();
			if (string.IsNullOrEmpty(entry))
			{
				continue;
			}

			if (IPAddress.TryParse(entry, out var address))
			{
				options.KnownProxies.Add(address);
				proxies++;
			}
			else
			{
				skipped++;
			}
		}

		options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
		if (networks + proxies > 0)
		{
			// Allow a chain through the trusted proxy; the middleware still only honors
			// entries forwarded by trusted hops.
			options.ForwardLimit = null;
		}

		return (networks, proxies, skipped);
	}
}
