using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Interaction;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>
/// A soldier the player may kill silently: an enemy, alive and active, with the player's eyes behind it and near its line, and
/// nothing between (<c>HumanSoldier_EvaluateActivateHandler</c>, <c>igi2 0x4EBF10</c>).
/// </content>
public sealed partial class Igi2HumanSoldier : IIgi2Activatable
{
    /// <summary>How far to either side of a soldier's line the player's eyes may be, <c>3072</c>.</summary>
    public const double SilentKillHalfWidth = 3072.0;

    /// <summary>The groups the line from the eyes to the soldier is traced through: the trees and <c>0x8000000</c>.</summary>
    public const uint SilentKillSightGroups = Igi2CollisionWorld.ForestGroup | 0x8000000u;

    /// <inheritdoc/>
    Vector3d IIgi2Activatable.Centre => Position;

    /// <inheritdoc/>
    /// <remarks>Its sphere in the world's tree: its root's distance from its origin and its model's radius.</remarks>
    float IIgi2Activatable.Radius
    {
        get
        {
            Vector3f r = RootTranslation;
            return (Fpu.Sqrt((((Fpu)r.X * r.X) + ((Fpu)r.Y * r.Y)) + ((Fpu)r.Z * r.Z)) + ModelRadius).F;
        }
    }

    /// <inheritdoc/>
    /// <remarks><c>HumanSoldier_GetActivateHandler</c>: a silent kill.</remarks>
    Igi2ActivateKind IIgi2Activatable.Kind => Igi2ActivateKind.SilentKill;

    /// <inheritdoc/>
    /// <remarks>
    /// An active, living soldier of team one is offered at its root when the view is behind it along its frame's Y, less than
    /// <see cref="SilentKillHalfWidth"/> to either side along its X, and the line between meets nothing but the two of them.
    /// </remarks>
    void IIgi2Activatable.EvaluateActivate(Igi2EvaluateActivate context)
    {
        if (!IsActive || IsDead || Team != 1)
        {
            return;
        }

        Vector3d root = HeldRootPosition();
        double dx = context.ViewPosition.X - root.X, dy = context.ViewPosition.Y - root.Y, dz = context.ViewPosition.Z - root.Z;
        Matrix3f o = Orientation;
        double across = ((double)o.M31 * dz) + ((double)o.M21 * dy) + ((double)o.M11 * dx);
        double ahead = ((double)o.M32 * dz) + ((double)o.M22 * dy) + ((double)o.M12 * dx);
        if (ahead >= 0.0 || Math.Abs(across) >= SilentKillHalfWidth)
        {
            return;
        }

        object? killer = context.Activator;
        if ((Mind as Ai.Runtime.Igi2HumanAi)?.World.Detection is { } sight
            && !sight.LineOfSight(
                context.ViewPosition,
                root,
                SilentKillSightGroups,
                owner => ReferenceEquals(owner, this) || ReferenceEquals(owner, killer),
                out _))
        {
            return;
        }

        context.Resolve(root, this);
    }

    /// <summary>
    /// The player's silent kill takes the soldier, <c>HumanPlayer_InitStateSilentKill</c>'s part in it: the soldier goes to its silent
    /// death, turned to the killer's heading and put where the killer stands, so the two clips play as one.
    /// </summary>
    /// <param name="gamma">The killer's heading.</param>
    /// <param name="position">Where the killer stands.</param>
    public void KillSilently(float gamma, Vector3d position)
    {
        Locomotion.Enter(Igi2SoldierState.SilentKill, this);
        PopOrientation(gamma);
        PopPosition(position);
    }
}
