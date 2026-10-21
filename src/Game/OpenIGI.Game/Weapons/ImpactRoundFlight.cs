using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// The flight of a launched grenade that goes off where it strikes, a tick at a time: Project I.G.I.'s M203 round
/// (<c>0x476340</c>) and I.G.I. 2's impact grenade (<c>ImpactGrenade_RunHandler</c>, <c>igi2 0x4CC2F0</c>), one engine
/// step compiled alike in both builds.
/// </summary>
/// <remarks>
/// The speed keeps <see cref="Drag"/> of itself, after <see cref="Gravity"/> more of fall, and the round moves by it. The
/// first build adds the x speed to the position straight from the register (<c>fst</c>, not <c>fstp</c>: the product
/// before it is stored) and the other two as stored; at the sequel's 24 bits the two are the same.
/// </remarks>
public static class ImpactRoundFlight
{
    /// <summary>What the speed keeps each tick, the float <c>0x3F7EB852</c>.</summary>
    public const float Drag = 0.995f;

    /// <summary>What the round falls by each tick, the float <c>0x420EB90C</c>.</summary>
    public const float Gravity = 35.68070983886719f;

    /// <summary>Moves a round a tick.</summary>
    /// <typeparam name="TFpu">The precision: <see cref="X87"/> for Project I.G.I., <see cref="X87Single"/> for I.G.I. 2.</typeparam>
    /// <param name="speed">Its speed, units a tick; dragged and dropped.</param>
    /// <param name="position">Where it is; moved by the new speed.</param>
    public static void Step<TFpu>(ref Vector3f speed, ref Vector3d position)
        where TFpu : struct, IX87<TFpu>
    {
        TFpu x = (TFpu)speed.X * Drag;
        float y = ((TFpu)speed.Y * Drag).F;
        float z = (((TFpu)speed.Z - Gravity) * Drag).F;
        speed = new Vector3f(x.F, y, z);
        position = new Vector3d((x + position.X).Value, ((TFpu)y + position.Y).Value, ((TFpu)z + position.Z).Value);
    }
}
