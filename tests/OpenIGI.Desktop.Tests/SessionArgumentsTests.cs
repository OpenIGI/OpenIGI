using OpenIGI.Host.Multiplayer;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>The multiplayer options, taken off a command line before the host's parser sees it.</summary>
public sealed class SessionArgumentsTests
{
    [Fact]
    public void TheMultiplayerOptionsAreTakenAndTheRestLeft()
    {
        SessionArguments session = SessionArguments.Parse(
            ["-title", "igi2", "-connect", "10.0.0.5:26010", "-name", "Anya", "-password", "x", "-mission", "5"], out string[] rest);

        Assert.Equal("10.0.0.5:26010", session.Connect);
        Assert.Equal("Anya", session.PlayerName);
        Assert.Equal("x", session.Password);
        Assert.False(session.Listen);
        Assert.Equal(["-title", "igi2", "-mission", "5"], rest);
    }

    [Fact]
    public void ADedicatedServersOptionsHaveTheirDefaults()
    {
        SessionArguments session = SessionArguments.Parse(["-listen", "-maxplayers", "99"], out string[] rest);

        Assert.True(session.Listen);
        Assert.Equal(SessionArguments.DefaultPort, session.Port);
        Assert.Equal(32, session.MaxPlayers);
        Assert.Empty(rest);
    }

    [Theory]
    [InlineData("-port", "0")]
    [InlineData("-connect")]
    public void ABadOptionIsRefused(params string[] args) =>
        Assert.Throws<ArgumentException>(() => SessionArguments.Parse(args, out _));
}
