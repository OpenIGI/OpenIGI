using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>A session's players as the humans a player's body cannot walk into (<c>0x462BA0</c>'s list, the living humans).</summary>
internal static class SessionHumans
{
    /// <summary>Makes a player's body meet every other living player of the session, wherever this game has them.</summary>
    /// <param name="world">The level: its players on a server, the ones it is shown on a client.</param>
    /// <param name="self">The player whose body is blocked.</param>
    /// <param name="missionPlayerCounts">Whether the mission's own player is one of the session's (not a dedicated server's nobody).</param>
    public static void Meet(MissionWorld world, PlayerCamera self, Func<bool> missionPlayerCounts) =>
        self.OtherHumans = () => Others(world, self, missionPlayerCounts());

    /// <summary>The feet of every living player but one.</summary>
    /// <param name="world">The level.</param>
    /// <param name="self">The player left out.</param>
    /// <param name="missionPlayerCounts">Whether the mission's own player is one of the session's.</param>
    /// <returns>Where they stand, in world units.</returns>
    public static IEnumerable<Vector3d> Others(MissionWorld world, PlayerCamera self, bool missionPlayerCounts)
    {
        if (missionPlayerCounts && world.Player is { } own && !ReferenceEquals(own, self) && !world.IsPlayerDead)
        {
            yield return own.Body.Position;
        }

        foreach (Igi1NetworkPlayer player in world.NetworkPlayers)
        {
            if (!ReferenceEquals(player.Player, self) && !player.IsDead)
            {
                yield return player.Player.Body.Position;
            }
        }

        foreach (Igi1NetworkPlayer player in world.ShownPartners)
        {
            if (!ReferenceEquals(player.Player, self) && !player.IsDead)
            {
                yield return player.Player.Body.Position;
            }
        }
    }
}
