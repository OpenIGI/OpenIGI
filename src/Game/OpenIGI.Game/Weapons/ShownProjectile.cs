using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// Something a weapon put in the world, as it is drawn: a grenade in flight or at rest, a laid mine, a rocket, a launcher
/// round. Another game's -- a co-op server's word on its players' -- carried as no more than its model and where it is.
/// </summary>
/// <param name="Model">The model it is drawn with.</param>
/// <param name="PreviousPosition">Where it was the tick before, in world units.</param>
/// <param name="Position">Where it is, in world units.</param>
/// <param name="Orientation">Its frame.</param>
/// <param name="Owner">Whose it is, another game's entity, or -1 for this game's own.</param>
/// <param name="Ordinal">
/// Which of its owner's it is, in the order the server sent them: with the owner, what follows it from one snapshot to the next.
/// </param>
public readonly record struct ShownProjectile(
    string Model, Vector3d PreviousPosition, Vector3d Position, Matrix3f Orientation, int Owner = -1, int Ordinal = 0)
{
    /// <summary>A laid mine's model.</summary>
    public const string MineModel = "136_01_1";

    /// <summary>Gets the frame a rocket is drawn in: its nose along its flight, its up as near the world's as the flight allows.</summary>
    /// <param name="velocity">Its velocity.</param>
    /// <param name="frame">The frame.</param>
    /// <returns>Whether it is moving at all; one standing still has no frame.</returns>
    public static bool TryAlongFlight(Vector3f velocity, out Matrix3f frame)
    {
        float length = MathF.Sqrt((velocity.X * velocity.X) + (velocity.Y * velocity.Y) + (velocity.Z * velocity.Z));
        if (length <= float.Epsilon)
        {
            frame = Matrix3f.Identity;
            return false;
        }

        Vector3f direction = velocity * (1f / length);
        Vector3f referenceUp = MathF.Abs(direction.Z) < 0.95f ? Vector3f.UnitZ : Vector3f.UnitX;
        Vector3f right = Vector3f.Cross(referenceUp, direction);
        right *= 1f / MathF.Sqrt((right.X * right.X) + (right.Y * right.Y) + (right.Z * right.Z));
        Vector3f up = Vector3f.Cross(direction, right);
        frame = Matrix3f.FromRows(
            new Vector3f(right.X, direction.X, up.X),
            new Vector3f(right.Y, direction.Y, up.Y),
            new Vector3f(right.Z, direction.Z, up.Z));
        return true;
    }

    /// <summary>Adds everything one player's weapons have in the world, as this tick left it.</summary>
    /// <param name="grenades">Its grenades and mines -- and, for the mission's own player, the soldiers' lobs.</param>
    /// <param name="missiles">Its rockets.</param>
    /// <param name="rounds">Its launcher rounds.</param>
    /// <param name="into">Where to.</param>
    public static void Collect(GrenadeSimulation grenades, MissileSimulation missiles, M203Simulation rounds, List<ShownProjectile> into)
    {
        ArgumentNullException.ThrowIfNull(grenades);
        ArgumentNullException.ThrowIfNull(missiles);
        ArgumentNullException.ThrowIfNull(rounds);
        ArgumentNullException.ThrowIfNull(into);
        foreach (LiveGrenade grenade in grenades.Grenades)
        {
            into.Add(new ShownProjectile(
                GrenadePhysics.ModelOf(grenade.Type),
                grenade.PreviousPosition,
                grenade.Position,
                GrenadePhysics.PresentationOrientation(grenade, 1f)));
        }

        foreach (PlacedMine mine in grenades.Mines)
        {
            into.Add(new ShownProjectile(MineModel, mine.Position, mine.Position, mine.Orientation));
        }

        foreach (LiveMissile missile in missiles.Missiles)
        {
            if (TryAlongFlight(missile.Velocity, out Matrix3f frame))
            {
                into.Add(new ShownProjectile(missile.ModelName, missile.PreviousPosition, missile.Position, frame));
            }
        }

        foreach (M203ProjectileState round in rounds.Projectiles)
        {
            into.Add(new ShownProjectile(round.ModelName, round.PreviousPosition, round.Position, round.Orientation));
        }
    }
}
