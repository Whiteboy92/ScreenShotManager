using System;
using System.Linq;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services;

namespace ScreenShotManager.Tests.MusicSync;

public class PairingTests
{
    [Fact]
    public void Generated_tokens_are_64_hex_chars_and_unique()
    {
        var a = PairingTokens.Generate();
        var b = PairingTokens.Generate();

        Assert.Matches("^[0-9a-f]{64}$", a);
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("Bearer secret", true)]
    [InlineData("bearer secret", true)]
    [InlineData("Bearer  secret ", true)]
    [InlineData("Bearer wrong", false)]
    [InlineData("Bearer secre", false)]
    [InlineData("secret", false)]
    [InlineData("Basic secret", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAuthorized_requires_exact_bearer_token(string? header, bool expected)
    {
        Assert.Equal(expected, PairingTokens.IsAuthorized(header, "secret"));
    }

    [Fact]
    public void IsAuthorized_rejects_everything_when_no_token_configured()
    {
        Assert.False(PairingTokens.IsAuthorized("Bearer ", ""));
    }

    [Fact]
    public void Pairing_uri_escapes_every_field()
    {
        var info = new PairingInfo("My PC & Co", new[] { "192.168.1.5", "10.0.0.2" }, 47821, "tok+en", "ABCD");

        var uri = new Uri(info.ToUri());
        var query = uri.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

        Assert.Equal("musicsync", uri.Scheme);
        Assert.Equal("pair", uri.Host);
        Assert.Equal("1", query["v"]);
        Assert.Equal("My PC & Co", query["name"]);
        Assert.Equal("192.168.1.5,10.0.0.2", query["hosts"]);
        Assert.Equal("47821", query["port"]);
        Assert.Equal("tok+en", query["token"]);
        Assert.Equal("ABCD", query["fp"]);
    }

    [Fact]
    public void DisplayEndpoints_lists_host_port_pairs()
    {
        var info = new PairingInfo("pc", new[] { "192.168.1.5", "10.0.0.2" }, 47821, "t", "f");
        Assert.Equal("192.168.1.5:47821, 10.0.0.2:47821", info.DisplayEndpoints);
        Assert.Equal("(no LAN address found)", (info with { Hosts = Array.Empty<string>() }).DisplayEndpoints);
    }

    [Theory]
    [InlineData(new byte[] { 192, 168, 1, 5 }, true)]
    [InlineData(new byte[] { 10, 0, 0, 1 }, true)]
    [InlineData(new byte[] { 172, 16, 0, 1 }, true)]
    [InlineData(new byte[] { 172, 31, 255, 1 }, true)]
    [InlineData(new byte[] { 172, 32, 0, 1 }, false)]
    [InlineData(new byte[] { 169, 254, 1, 1 }, false)]
    [InlineData(new byte[] { 8, 8, 8, 8 }, false)]
    [InlineData(new byte[] { 127, 0, 0, 1 }, false)]
    public void IsPrivate_matches_rfc1918_ranges(byte[] address, bool expected)
    {
        Assert.Equal(expected, LanAddressProvider.IsPrivate(address));
    }
}
