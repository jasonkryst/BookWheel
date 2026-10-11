using System.Net;
using BookWheel.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace BookWheel.Tests.Services;

public sealed class ForwardedHeadersConfiguratorTests
{
	[Fact]
	public void Valid_Cidr_Is_Added_And_Loopback_Defaults_Are_Kept()
	{
		var options = new ForwardedHeadersOptions();
		var defaultNetworks = options.KnownIPNetworks.Count;
		var defaultProxies = options.KnownProxies.Count;

		var (networks, proxies, skipped) = ForwardedHeadersConfigurator.Apply(options, ["172.16.0.0/12"], null);

		Assert.Equal((1, 0, 0), (networks, proxies, skipped));
		Assert.Equal(defaultNetworks + 1, options.KnownIPNetworks.Count);
		Assert.Equal(defaultProxies, options.KnownProxies.Count);
		Assert.Contains(options.KnownIPNetworks, n => n.Contains(IPAddress.Parse("172.18.0.5")));
		Assert.DoesNotContain(options.KnownIPNetworks, n => n.Contains(IPAddress.Parse("203.0.113.9")));
		Assert.Null(options.ForwardLimit);
		Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
	}

	[Fact]
	public void Invalid_Entries_Are_Skipped_And_Counted()
	{
		var options = new ForwardedHeadersOptions();
		var defaultNetworks = options.KnownIPNetworks.Count;
		var defaultProxies = options.KnownProxies.Count;

		var (networks, proxies, skipped) = ForwardedHeadersConfigurator.Apply(
			options, ["not-a-cidr", "10.0.0.0/99"], ["999.1.1.1", "nope"]);

		Assert.Equal((0, 0, 4), (networks, proxies, skipped));
		Assert.Equal(defaultNetworks, options.KnownIPNetworks.Count);
		Assert.Equal(defaultProxies, options.KnownProxies.Count);
	}

	[Fact]
	public void Empty_Input_Adds_Nothing_And_Keeps_Default_Limit()
	{
		var options = new ForwardedHeadersOptions();
		var defaultNetworks = options.KnownIPNetworks.Count;
		var defaultProxies = options.KnownProxies.Count;
		var defaultLimit = options.ForwardLimit;

		var result = ForwardedHeadersConfigurator.Apply(options, null, null, "  ");

		Assert.Equal((0, 0, 0), result);
		Assert.Equal(defaultNetworks, options.KnownIPNetworks.Count);
		Assert.Equal(defaultProxies, options.KnownProxies.Count);
		Assert.Equal(defaultLimit, options.ForwardLimit);
	}

	[Fact]
	public void Extra_Csv_Is_Parsed_And_Merged_With_Networks()
	{
		var options = new ForwardedHeadersOptions();

		var (networks, _, skipped) = ForwardedHeadersConfigurator.Apply(
			options, ["192.168.0.0/16"], null, "172.16.0.0/12, 10.0.0.0/8;bogus");

		Assert.Equal(3, networks);
		Assert.Equal(1, skipped);
	}

	[Fact]
	public void Proxies_Are_Added()
	{
		var options = new ForwardedHeadersOptions();
		var defaultProxies = options.KnownProxies.Count;

		var (networks, proxies, skipped) = ForwardedHeadersConfigurator.Apply(options, null, ["172.18.0.5", "::2"]);

		Assert.Equal((0, 2, 0), (networks, proxies, skipped));
		Assert.Equal(defaultProxies + 2, options.KnownProxies.Count);
		Assert.Contains(IPAddress.Parse("172.18.0.5"), options.KnownProxies);
		Assert.Null(options.ForwardLimit);
	}
}
