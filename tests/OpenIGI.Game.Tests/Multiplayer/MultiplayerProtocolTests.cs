using OpenIGI.Game.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>The messages a server and a browser or client exchange before a session starts.</summary>
public sealed class MultiplayerProtocolTests
{
    [Fact]
    public void EachGameHasItsOwnProtocolId()
    {
        Assert.NotEqual(MultiplayerProtocol.IdFor("igi1"), MultiplayerProtocol.IdFor("igi2"));
        Assert.Equal(MultiplayerProtocol.IdFor("igi2"), MultiplayerProtocol.IdFor("igi2"));
    }

    [Fact]
    public void ServerInfo_RoundTrips()
    {
        ServerInfo info = new("igi2", "Jones's server", "sandstorm", "objective", 3, 32, true, "0.4.0+abc");
        Assert.Equal(info, ServerInfo.Decode(info.Encode()));
    }

    [Fact]
    public void ServerInfo_RefusesBytesThatAreNotOne() => Assert.Null(ServerInfo.Decode([MultiplayerProtocol.Version, 0xFF]));

    [Fact]
    public void JoinRequest_RoundTripsAndCutsALongName()
    {
        JoinRequest request = new("Jones", "0.4.0", "secret");
        Assert.Equal(request, JoinRequest.Decode(request.Encode()));

        JoinRequest longName = new(new string('x', 100), "0.4.0", string.Empty);
        Assert.Equal(JoinRequest.MaxNameBytes, JoinRequest.Decode(longName.Encode())!.PlayerName.Length);
    }

    [Fact]
    public void JoinRequest_OfAnotherVersionIsRefused()
    {
        byte[] bytes = new JoinRequest("Jones", "0.4.0", string.Empty).Encode();
        bytes[0] ^= 0x7F;
        Assert.Null(JoinRequest.Decode(bytes));
    }
}
