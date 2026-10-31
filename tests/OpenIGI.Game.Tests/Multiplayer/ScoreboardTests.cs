using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>The scoreboard a server sends, read back whole by a client.</summary>
public sealed class ScoreboardTests
{
    [Fact]
    public void Every_line_reaches_the_client_with_its_entity_and_ping()
    {
        Scoreboard sent = new(
            2,
            3,
            1,
            [
                new ScoreboardLine(ServerSession.HostSlot, "Jones", 0, 0, 4, 1, true, Entity: 0, Ping: 0),
                new ScoreboardLine(3, "Anya", 0, 0, 2, 2, false, Entity: 4, Ping: 87),
            ]);
        BitWriter writer = new(256);
        sent.Write(writer);
        BitReader reader = new(writer.AsSpan().ToArray());

        Scoreboard? read = Scoreboard.Read(ref reader);

        Assert.NotNull(read);
        Assert.Equal(sent.Lines, read.Lines);
        Assert.Equal((2, 3, 1), (read.Round, read.GoodScore, read.BadScore));
    }
}