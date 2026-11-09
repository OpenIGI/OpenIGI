using OpenIGI.Core.Numerics;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>What a <see cref="ReplicatedImpact"/> is.</summary>
public enum ImpactKind : byte
{
    /// <summary>A round met a surface: its mark and dust.</summary>
    Surface,

    /// <summary>A round met a body: its blood.</summary>
    Blood,

    /// <summary>A weapon's blast went off: its fireball, smoke, scorch and report, but none of its harm.</summary>
    Blast,

    /// <summary>A grenade struck the ground or a wall: its knock.</summary>
    Bounce,

    /// <summary>A proximity mine felt a human come into its reach and armed: its click.</summary>
    MineArmed,
}

/// <summary>Which weapon's blast a <see cref="ImpactKind.Blast"/> is, for a client to show as that weapon's.</summary>
public enum ReplicatedBlastKind : byte
{
    /// <summary>A hand grenade.</summary>
    Grenade,

    /// <summary>A flashbang: its flash, and the whiteout of whoever's eye is near.</summary>
    Flashbang,

    /// <summary>A proximity mine.</summary>
    Mine,

    /// <summary>A rocket: the LAW's, or a vehicle's.</summary>
    Missile,

    /// <summary>A launcher round.</summary>
    M203,

    /// <summary>
    /// Search and destroy's bomb: a rocket's fire, smoke and report, and on top of them the remastered blast -- its shockwave,
    /// its fireball and column, its light, and what it shatters for the round's last seconds.
    /// </summary>
    Bomb,
}

/// <summary>One round landing, as the server sends it for every client to show: a hit on a wall, or a wound.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Source">The entity whose round it was; the client that fired it already showed it, and skips it.</param>
/// <param name="Position">Where it landed, in whole world units.</param>
/// <param name="Direction">The round's travel, a unit vector; for a blast, the up of what it scorches.</param>
/// <param name="Blast">For a blast, which weapon's it was.</param>
/// <param name="RadiusUnits">For a blast, its radius in whole world units, which sizes its fireballs and smoke.</param>
/// <param name="FalloffUnits">For a blast, its falloff in whole world units.</param>
/// <remarks>
/// Only where and which way: a client traces the short stretch through the point in its own copy of the level, and finds
/// the surface, its material, its mark and its dust there, as it would for a round of its own.
/// </remarks>
public readonly record struct ReplicatedImpact(
    ImpactKind Kind,
    int Source,
    Int3 Position,
    Vector3f Direction,
    ReplicatedBlastKind Blast = ReplicatedBlastKind.Grenade,
    int RadiusUnits = 0,
    int FalloffUnits = 0)
{
    /// <summary>The most a snapshot carries; past it, the newest are kept.</summary>
    public const int MaxPerSnapshot = 64;

    private const int DirectionBits = 12;

    /// <summary>Writes one.</summary>
    /// <param name="writer">Where to.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBits((uint)Kind, 3);
        writer.WriteVarUInt((uint)(Source + 1));
        writer.WriteVarInt(Position.X);
        writer.WriteVarInt(Position.Y);
        writer.WriteVarInt(Position.Z);
        writer.WriteQuantized(Direction.X, -1f, 1f, DirectionBits);
        writer.WriteQuantized(Direction.Y, -1f, 1f, DirectionBits);
        writer.WriteQuantized(Direction.Z, -1f, 1f, DirectionBits);
        if (Kind == ImpactKind.Blast)
        {
            writer.WriteBits((uint)Blast, 3);
            writer.WriteVarUInt((uint)Math.Max(0, RadiusUnits));
            writer.WriteVarUInt((uint)Math.Max(0, FalloffUnits));
        }
    }

    /// <summary>Reads one.</summary>
    /// <param name="reader">Where from.</param>
    /// <returns>It.</returns>
    public static ReplicatedImpact Read(ref BitReader reader)
    {
        ImpactKind kind = (ImpactKind)reader.ReadBits(3);
        int source = (int)reader.ReadVarUInt() - 1;
        Int3 position = new(reader.ReadVarInt(), reader.ReadVarInt(), reader.ReadVarInt());
        Vector3f direction = new(
            reader.ReadQuantized(-1f, 1f, DirectionBits), reader.ReadQuantized(-1f, 1f, DirectionBits), reader.ReadQuantized(-1f, 1f, DirectionBits));
        if (kind != ImpactKind.Blast)
        {
            return new ReplicatedImpact(kind, source, position, direction);
        }

        ReplicatedBlastKind blast = (ReplicatedBlastKind)reader.ReadBits(3);
        int radius = (int)Math.Min(reader.ReadVarUInt(), int.MaxValue);
        int falloff = (int)Math.Min(reader.ReadVarUInt(), int.MaxValue);
        return new ReplicatedImpact(kind, source, position, direction, blast, radius, falloff);
    }
}
