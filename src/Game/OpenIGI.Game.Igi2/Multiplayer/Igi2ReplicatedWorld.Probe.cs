using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <content>A debugging tool's moves on the server's level (<see cref="Game.Multiplayer.NetProbe"/>). <b>Temporary.</b></content>
public sealed partial class Igi2ReplicatedWorld
{
    /// <inheritdoc/>
    /// <remarks>
    /// On the side of the soldier the player stood on, at the soldier's height, turned to face it; the client's own player is put
    /// right to it by the snapshots that follow.
    /// </remarks>
    public string? TeleportBeforeSoldier(int slot, int soldier, double metres)
    {
        if (PlayerOf(slot)?.Human is not { } moved)
        {
            return "that player is not in the level";
        }

        if (SoldierOf(soldier) is not { } guard)
        {
            return "no such soldier";
        }

        Vector3d away = moved.Position - guard.Position;
        away = new Vector3d(away.X, away.Y, 0);
        double length = away.Length;
        Vector3d along = length > 1 ? away / length : new Vector3d(1, 0, 0);
        Vector3d spot = guard.Position + (along * metres * WorldUnits.PerMetre);
        moved.PopPositionDroppingGround(spot);
        float gamma = Igi2TitlePlayer.GammaToward(spot, guard.Position);
        moved.Gamma = gamma;
        moved.IdealGamma = gamma;
        return null;
    }

    /// <inheritdoc/>
    public string? HarmSoldier(int soldier, float share)
    {
        if (SoldierOf(soldier) is not { } guard)
        {
            return "no such soldier";
        }

        if (guard.IsDead)
        {
            return "that soldier is dead";
        }

        guard.HarmFromConsole(share);
        return null;
    }

    private Igi2HumanSoldier? SoldierOf(int entity) =>
        SoldierIndex(entity) is { } index && _level.Soldiers is { } soldiers && (uint)index < (uint)soldiers.Bodies.Count
            ? soldiers.Bodies[index]
            : null;
}
