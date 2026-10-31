using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Input;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>I.G.I. missions played by two real games together: what one player does, the other's game shows.</summary>
[Collection(ParityGroup.Name)]
public sealed class Igi1CoopPairTests
{
    [RequiresGameDataFact]
    public void Both_games_come_up_the_joiner_is_welcomed_and_each_sees_the_other_walk()
    {
        using Igi1CoopPair pair = new();
        Assert.True(pair.RunUntil(() => pair.Joined && pair.Host!.World.NetworkPlayers.Count == 1, 3000) >= 0, "the joiner was never welcomed");

        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Core.Numerics.Vector3d from = partner.Player.Body.Position;
        Core.Numerics.Vector3d localFrom = pair.Joiner!.World.Player!.Body.Position;
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.W, true);
        pair.RunUntil(() => false, 300);
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.W, false);

        double walked = Core.Numerics.Vector3d.Distance(from, partner.Player.Body.Position);
        double localWalked = Core.Numerics.Vector3d.Distance(localFrom, pair.Joiner.World.Player!.Body.Position);
        Assert.True(walked > 2048, $"the joiner's walk moved its player on the host {walked} units, locally {localWalked}; missed {partner.Commands.Missed}");

        // The host's player stands in the joiner's level where it stands in the host's.
        Assert.NotNull(pair.Joiner!.World.PartnerBodies);
        Game.Igi1.Ai.PartnerBody host = Assert.Single(pair.Joiner.World.PartnerBodies!(), b => b.Key is int id && id == Igi1ReplicatedWorld.HostEntity);
        double apart = Core.Numerics.Vector3d.Distance(host.Feet, pair.Host.World.Player!.Body.Position);
        Assert.True(apart < 1024, $"the host's player is {apart} units from where the joiner shows it");
    }
}
