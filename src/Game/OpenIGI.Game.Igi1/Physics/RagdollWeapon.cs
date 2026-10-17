using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi1.Physics;

/// <summary>
/// The rigid body a held weapon becomes once the hand holding it is dead.
/// </summary>
/// <remarks>
/// <para>
/// A weapon is drawn rigidly at a hand bone, which is right while the hand is animated and wrong the
/// moment it is not: the rifle passes through whatever the corpse landed on, because nothing was ever
/// testing it. Giving it a body of its own — a mass, a capsule and a joint back to the hand — is what
/// lets it rest on the floor beside the body, or under it, the way a dropped weapon does.
/// </para>
/// <para>
/// It is one extra body in the same solver rather than a special case beside it. The ragdoll's arrays
/// simply grow by one, its joint list by one, and every pass — integration, contacts, friction,
/// self-collision — picks the weapon up without knowing it is a weapon.
/// </para>
/// </remarks>
public static class RagdollWeapon
{
    /// <summary>What a held weapon is taken to weigh, in kilograms.</summary>
    /// <remarks>
    /// Four. Service rifles run from about three to five kilograms loaded, and the port has no weight in
    /// its own weapon data to read — the original never needed one, because nothing it models is ever
    /// pushed by a weapon's mass. Recorded as the invented number it is; what it has to be is heavy
    /// enough to swing the hand a little and light enough not to drag the arm.
    /// </remarks>
    public const double MassInKilograms = 4.0;

    /// <summary>The thinnest a weapon's capsule may be, in engine units — 3 cm.</summary>
    public const float MinimumRadiusInUnits = 123.0f;

    /// <summary>How far a weapon may turn from the hand holding it, in radians.</summary>
    /// <remarks>
    /// Thirty degrees. A dead hand does not open, so the weapon stays roughly where it was gripped — but
    /// it is not welded either, and the little it gives is what stops the rifle levering the whole arm
    /// about when it lands on something.
    /// </remarks>
    public const float GripAllowance = 30f * MathF.PI / 180f;

    /// <summary>Builds the body a weapon's own geometry describes.</summary>
    /// <param name="minimum">The lowest corner of its bounds, in engine units, in the hand's frame.</param>
    /// <param name="maximum">The highest corner.</param>
    /// <returns>The body, whose capsule runs along the weapon's longest axis.</returns>
    /// <remarks>
    /// The bounds come from the weapon's own mesh, so a pistol gets a pistol's capsule and a rifle a
    /// rifle's, with nothing named and no table written. The capsule runs along whichever axis the model
    /// is longest in — for every weapon this game ships, the barrel.
    /// </remarks>
    public static RagdollBone FromBounds(Vector3f minimum, Vector3f maximum)
    {
        Vector3f extent = maximum - minimum;
        Vector3f centre = (minimum + maximum) * 0.5f;

        // The longest axis is the barrel; the other two decide how thick it is.
        Vector3f aim;
        float length;
        float across;
        if (extent.X >= extent.Y && extent.X >= extent.Z)
        {
            aim = Vector3f.UnitX;
            length = extent.X;
            across = MathF.Max(extent.Y, extent.Z);
        }
        else if (extent.Y >= extent.Z)
        {
            aim = Vector3f.UnitY;
            length = extent.Y;
            across = MathF.Max(extent.X, extent.Z);
        }
        else
        {
            aim = Vector3f.UnitZ;
            length = extent.Z;
            across = MathF.Max(extent.X, extent.Y);
        }

        float radius = MathF.Max(across * 0.5f, MinimumRadiusInUnits);
        float half = MathF.Max((length * 0.5f) - radius, 0f);

        double along = 0.5 * MassInKilograms * radius * radius;
        double transverse =
            MassInKilograms * ((3.0 * radius * radius) + (length * length)) / 12.0;

        Matrix3f frame = RagdollSkeleton.FrameAlignedTo(aim, RagdollSkeleton.Perpendicular(aim));
        Matrix3f inverse = new(
            (float)(1.0 / transverse), 0f, 0f,
            0f, (float)(1.0 / transverse), 0f,
            0f, 0f, (float)(1.0 / along));

        return new RagdollBone(
            centre,
            aim,
            RagdollSkeleton.Perpendicular(aim),
            half,
            radius,
            (float)(1.0 / MassInKilograms),
            frame * inverse * frame.Transposed());
    }
}
