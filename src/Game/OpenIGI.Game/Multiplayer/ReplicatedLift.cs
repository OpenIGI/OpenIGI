using OpenIGI.Core.Numerics;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>One of the level's lifts as the server has it: where its car is and where it is going.</summary>
/// <param name="Position">The car's position, in whole world units.</param>
/// <param name="Floor">The floor it last stood at or passed.</param>
/// <param name="Target">The floor it travels to, or -1 when it stands.</param>
/// <param name="Speed">Its speed, in world units a second.</param>
/// <remarks>
/// A lift moves anyone standing in it, so a client whose copy of a lift stands while the server's rises has its player
/// put back, tick after tick, where the server carried it. The lifts ride the snapshots for that, in the level's own order.
/// </remarks>
public readonly record struct ReplicatedLift(Int3 Position, int Floor, int Target, float Speed)
{
    /// <summary>The most a snapshot carries.</summary>
    public const int MaxPerSnapshot = 64;

    /// <summary>Writes one.</summary>
    /// <param name="writer">Where to.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteVarInt(Position.X);
        writer.WriteVarInt(Position.Y);
        writer.WriteVarInt(Position.Z);
        writer.WriteVarUInt((uint)Floor);
        writer.WriteVarUInt((uint)(Target + 1));
        writer.WriteSingle(Speed);
    }

    /// <summary>Reads one.</summary>
    /// <param name="reader">Where from.</param>
    /// <returns>It.</returns>
    public static ReplicatedLift Read(ref BitReader reader) => new(
        new Int3(reader.ReadVarInt(), reader.ReadVarInt(), reader.ReadVarInt()),
        (int)reader.ReadVarUInt(),
        (int)reader.ReadVarUInt() - 1,
        reader.ReadSingle());
}
