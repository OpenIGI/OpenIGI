using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Multiplayer;

/// <summary>Flags a <see cref="ReplicatedHuman"/> carries.</summary>
[Flags]
public enum ReplicatedHumanCondition : byte
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>It is alive.</summary>
    Alive = 1,

    /// <summary>It keeps low: crouched or lying.</summary>
    Low = 2,

    /// <summary>Its weapon fired this tick.</summary>
    Firing = 4,

    /// <summary>It stands on the ground.</summary>
    OnGround = 8,

    /// <summary>It is looking through a zoom.</summary>
    Zoomed = 16,

    /// <summary>On a ladder: <see cref="ReplicatedHuman.State"/> is the climb's clip and <see cref="ReplicatedHuman.ClipTime"/> where it is.</summary>
    Climbing = 32,
}

/// <summary>One human as a snapshot carries it: everything a client needs to draw another player and hit-test it.</summary>
/// <remarks>
/// <para>
/// The same shape for both games, since both draw a human from the same things: where it stands, which way it faces and
/// looks, what its body is doing (the title's own state and action numbers, which the title's proxy turns back into
/// clips), what it holds, and how it fares. A title maps its humans onto this and back; the snapshot code never knows
/// which game it carries.
/// </para>
/// <para>
/// Positions are whole world units, which both games already work in (4096 to the metre); a tick's movement is carried
/// in the velocity so a client extrapolating a late snapshot moves the body the way the server did.
/// </para>
/// </remarks>
/// <param name="Id">The entity id, unique in the session.</param>
/// <param name="Position">Where its feet are, in whole world units.</param>
/// <param name="Velocity">Its velocity, in world units a tick.</param>
/// <param name="Yaw">Its body's heading, as a 16-bit angle.</param>
/// <param name="Pitch">Its view's pitch, as a 16-bit angle.</param>
/// <param name="State">The title's body state number.</param>
/// <param name="Action">The title's upper-body action number.</param>
/// <param name="Weapon">The title's id of the weapon it holds, or zero.</param>
/// <param name="Health">Its health, 0 to 255 of full.</param>
/// <param name="Team">Its team.</param>
/// <param name="Flags">Its flags.</param>
/// <param name="Shots">Its shots so far, counted round in a byte: a change is a shot, whichever snapshot carries it.</param>
/// <param name="ActionSequence">How often its upper-body action has started, counted round in a byte: a change starts it again.</param>
/// <param name="ClipTime">Where a whole-body clip the others must pose exactly is, in sixteenths of its time; a climb's.</param>
public readonly record struct ReplicatedHuman(
    int Id,
    Int3 Position,
    Vector3f Velocity,
    ushort Yaw,
    ushort Pitch,
    ushort State,
    ushort Action,
    ushort Weapon,
    byte Health,
    byte Team,
    ReplicatedHumanCondition Flags,
    byte Shots = 0,
    byte ActionSequence = 0,
    ushort ClipTime = 0);

/// <summary>A point in whole world units.</summary>
/// <param name="X">X.</param>
/// <param name="Y">Y.</param>
/// <param name="Z">Z.</param>
public readonly record struct Int3(int X, int Y, int Z)
{
    /// <summary>Gets the point a world position falls in, truncated towards zero as the games truncate.</summary>
    /// <param name="position">The position.</param>
    /// <returns>The point.</returns>
    public static Int3 From(Vector3d position) => new((int)position.X, (int)position.Y, (int)position.Z);

    /// <summary>Gets the point as a world position.</summary>
    /// <returns>The position.</returns>
    public Vector3d ToVector() => new(X, Y, Z);
}
