using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>Where a soldier's weapon fires from.</content>
public sealed partial class Igi2HumanSoldier
{
    /// <inheritdoc/>
    /// <remarks>A soldier fires from its mind's frame, so only one with a mind can.</remarks>
    public override bool HasFireFrame => Mind is not null;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <c>HumanSoldier_WeaponGetParentParametersHandler</c> (<c>igi2 0x4E9B60</c>): three draws of the run stream go unused first. The
    /// shot leaves from the mind's fire position (<c>HumanAI+0x60</c>) toward its aim point (<c>+0x7530</c>): the forward column is the
    /// unit direction, the side column that direction crossed with straight up (not normalised, so shorter the steeper the aim), and
    /// the up column the side crossed with forward, normalised.
    /// </para>
    /// <para>
    /// With the aim point on the fire position, or straight above or below it, the mind's own orientation (<c>+0xA0</c>) is used. The
    /// inaccuracy flag is the player's: a soldier's frame takes none.
    /// </para>
    /// </remarks>
    public override Igi2FireFrame GetFireFrame(bool addInaccuracy)
    {
        IIgi2SoldierMind mind = Mind ?? throw new InvalidOperationException("A soldier without a mind has no fire frame.");
        Random.NextRaw();
        Random.NextRaw();
        Random.NextRaw();
        Vector3d s = mind.FirePosition, t = mind.AimTarget;
        return new Igi2FireFrame(s, AimAlong(s, t) ?? [.. mind.FireOrientation]);
    }

    /// <summary>The frame aimed from one point to another, or <see langword="null"/> when the aim has no direction or no side.</summary>
    private static float[]? AimAlong(Vector3d from, Vector3d to)
    {
        Fpu dx = (Fpu)to.X - from.X;
        float dyStored = ((Fpu)to.Y - from.Y).F;
        Fpu dz = (Fpu)to.Z - from.Z;
        Fpu length = Fpu.Sqrt(((dx * dx) + ((Fpu)dyStored * dyStored)) + (dz * dz));
        if (length.Value <= 0.0)
        {
            return null;
        }

        Fpu inverse = (Fpu)1f / length;
        float fx = (inverse * dx).F;
        float fy = ((Fpu)dyStored * inverse).F;
        Fpu fz = dz * inverse;

        // The side: forward crossed with straight up.
        Fpu zeroZ = (Fpu)0f * fz;
        float sx = ((Fpu)fy - zeroZ).F;
        float sy = (-((Fpu)fx - zeroZ)).F;
        Fpu sz = ((Fpu)fx * 0f) - ((Fpu)fy * 0f);
        Fpu side = Fpu.Sqrt((((Fpu)sx * sx) + ((Fpu)sy * sy)) + (sz * sz));
        if (side.Value <= 0.0)
        {
            return null;
        }

        // Up: the side crossed with forward.
        float ux = (((Fpu)sy * fz) - (sz * fy)).F;
        float uy = (-(((Fpu)sx * fz) - (sz * fx))).F;
        float uz = (((Fpu)sx * fy) - ((Fpu)sy * fx)).F;
        if (ux != 0f || uy != 0f || uz != 0f)
        {
            Fpu up = Fpu.Sqrt((((Fpu)ux * ux) + ((Fpu)uy * uy)) + ((Fpu)uz * uz));
            Fpu upInverse = (Fpu)1.0 / up;
            ux = ((Fpu)ux * upInverse).F;
            uy = ((Fpu)uy * upInverse).F;
            uz = (upInverse * uz).F;
        }

        return [sx, fx, ux, sy, fy, uy, sz.F, fz.F, uz];
    }
}
