using OpenIGI.Core.Numerics;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>One round's mark on the world, as the server sends it for every client to show: a hit on a wall, or a wound.</summary>
/// <param name="Blood">A wound's blood, rather than a hit on a surface.</param>
/// <param name="Source">The entity whose round it was; the client that fired it already showed it, and skips it.</param>
/// <param name="Position">Where it landed, in whole world units.</param>
/// <param name="Direction">The round's travel, a unit vector.</param>
/// <remarks>
/// Only where and which way: a client traces the short stretch through the point in its own copy of the level, and finds
/// the surface, its material, its mark and its dust there, as it would for a round of its own.
/// </remarks>
public readonly record struct ReplicatedImpact(bool Blood, int Source, Int3 Position, Vector3f Direction)
{
    /// <summary>The most a snapshot carries; past it, the newest are kept.</summary>
    public const int MaxPerSnapshot = 64;

    private const int DirectionBits = 12;

    /// <summary>Writes one.</summary>
    /// <param name="writer">Where to.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBool(Blood);
        writer.WriteVarUInt((uint)(Source + 1));
        writer.WriteVarInt(Position.X);
        writer.WriteVarInt(Position.Y);
        writer.WriteVarInt(Position.Z);
        writer.WriteQuantized(Direction.X, -1f, 1f, DirectionBits);
        writer.WriteQuantized(Direction.Y, -1f, 1f, DirectionBits);
        writer.WriteQuantized(Direction.Z, -1f, 1f, DirectionBits);
    }

    /// <summary>Reads one.</summary>
    /// <param name="reader">Where from.</param>
    /// <returns>It.</returns>
    public static ReplicatedImpact Read(ref BitReader reader)
    {
        bool blood = reader.ReadBool();
        int source = (int)reader.ReadVarUInt() - 1;
        Int3 position = new(reader.ReadVarInt(), reader.ReadVarInt(), reader.ReadVarInt());
        Vector3f direction = new(
            reader.ReadQuantized(-1f, 1f, DirectionBits), reader.ReadQuantized(-1f, 1f, DirectionBits), reader.ReadQuantized(-1f, 1f, DirectionBits));
        return new ReplicatedImpact(blood, source, position, direction);
    }
}
