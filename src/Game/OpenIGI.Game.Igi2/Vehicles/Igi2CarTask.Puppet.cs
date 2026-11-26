using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Vehicles;

/// <summary>A car as a co-op server has it: where it stands, which way, whether its door is open, whether it has blown up, and its gun.</summary>
/// <param name="Position">Where it stands, in world units.</param>
/// <param name="Orientation">Which way, nine floats row by row.</param>
/// <param name="IsDoorOpen">Whether its door is open.</param>
/// <param name="IsExploded">Whether it has blown up.</param>
/// <param name="Gun">Its gun's aim, or <see langword="null"/> for a car with none.</param>
public readonly record struct Igi2CarPose(Vector3d Position, float[] Orientation, bool IsDoorOpen, bool IsExploded, Igi2GunAim? Gun);

/// <content>A co-op client's copy of the car: stood where the server's stands, driving, killing and blowing up only as the server's does.</content>
public sealed partial class Igi2CarTask
{
    /// <summary>
    /// Gets or sets a value indicating whether it is a co-op client's copy of the server's: it does not drive, watch, kill or blow up
    /// of itself, but is stood where the server's stands (<see cref="Hold"/>); its gun points and fires as the server's.
    /// </summary>
    public bool IsPuppet { get; set; }

    /// <summary>Gets the car as it is now, as a server sends it.</summary>
    /// <returns>Its pose.</returns>
    public Igi2CarPose Pose() => new(CurrentPosition, [.. CurrentOrientation], IsDoorOpen, IsExploded, Gun?.Aim());

    /// <summary>Stands it as the server's stands, on a co-op client.</summary>
    /// <param name="pose">The server's car.</param>
    /// <remarks>Blown up there, it is taken out of the world here, its blast the server's (shown as the server's explosions are).</remarks>
    public void Hold(in Igi2CarPose pose)
    {
        IsPuppet = true;
        Gun?.IsPuppet = true;
        if (pose.IsExploded)
        {
            if (_isExploded == 0)
            {
                Remove();
                _isExploded = 1;
            }

            return;
        }

        IsDoorOpen = pose.IsDoorOpen;
        if (_body is { } body)
        {
            if (_movedTick != _runTick)
            {
                _movedTick = _runTick;
                _lastPosition = body.Position;
                _lastOrientation = [.. body.Orientation];
            }

            body.Position = pose.Position;
            Array.Copy(pose.Orientation, body.Orientation, 9);
            if (_solids.Count > 0 && Models is { } models)
            {
                Igi2Pose moved = Igi2Pose.FromMatrix(body.Position, Rows(body.Orientation));
                Igi2CollisionWorld.Repose(_solids, models, Model, moved, Magic);
                PlaceDoors(moved);
            }
        }

        MountGun();
        if (pose.Gun is { } aim)
        {
            Gun?.Hold(aim);
        }
    }
}
