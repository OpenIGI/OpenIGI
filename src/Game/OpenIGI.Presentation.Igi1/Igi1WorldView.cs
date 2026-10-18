using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>What Project I.G.I.'s player has put in the world: its grenades, rockets and launcher rounds, the vehicles' rockets, and its marks.</summary>
/// <param name="gameplay">The first game's mission.</param>
internal sealed class Igi1WorldView(Igi1Gameplay gameplay) : IMissionWorldView
{
    /// <inheritdoc/>
    public BallisticSimulation? Ballistics => gameplay.World.Player?.Ballistics;

    /// <inheritdoc/>
    public void DrawProjectiles(IGrenadePresenter? grenades, IMissilePresenter? missiles, in Camera3D eye, in Matrix4f projection, float tickBlend)
    {
        MissionWorld mission = gameplay.World;
        if (mission.Player is not { } player)
        {
            return;
        }

        grenades?.Draw(player.Grenades, eye, projection, tickBlend);
        if (missiles is not null)
        {
            missiles.Draw(player.Missiles, eye, projection, tickBlend);
            missiles.Draw(mission.VehicleMissiles, eye, projection, tickBlend);
            missiles.Draw(player.M203, eye, projection, tickBlend);
        }
    }

    /// <inheritdoc/>
    public void DrawMarks(IBulletMarkPresenter marks, in Camera3D eye, in Matrix4f projection, PlacementPose? poses)
    {
        ArgumentNullException.ThrowIfNull(marks);
        if (gameplay.World.Player is { } player)
        {
            marks.Draw(player.Marks, eye, projection, poses);
        }
    }
}
