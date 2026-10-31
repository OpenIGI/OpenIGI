using OpenIGI.Core.Numerics;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>
/// One thing a player feels, as the server's handlers raised it, sent for that player's own game to feel: a title's player
/// senses, recorded on the server for a partner and made again on its client.
/// </summary>
/// <param name="Entity">The player that feels it.</param>
/// <param name="Kind">Which of the title's senses it is.</param>
/// <param name="Vector">A direction it carries, or zero.</param>
/// <param name="A">A first figure it carries, or zero.</param>
/// <param name="B">A second figure it carries, or zero.</param>
/// <param name="Ticks">A length it carries, or zero.</param>
/// <param name="Text">A name it carries, or empty.</param>
public readonly record struct ReplicatedSense(int Entity, byte Kind, Vector3f Vector = default, float A = 0f, float B = 0f, int Ticks = 0, string Text = "")
{
    /// <summary>The most a snapshot carries; past it, the newest are kept.</summary>
    public const int MaxPerSnapshot = 32;

    /// <summary>Writes one.</summary>
    /// <param name="writer">Where to.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteVarUInt((uint)Entity);
        writer.WriteBits(Kind, 4);
        writer.WriteSingle(Vector.X);
        writer.WriteSingle(Vector.Y);
        writer.WriteSingle(Vector.Z);
        writer.WriteSingle(A);
        writer.WriteSingle(B);
        writer.WriteVarUInt((uint)Math.Max(0, Ticks));
        writer.WriteString(Text, 32);
    }

    /// <summary>Reads one.</summary>
    /// <param name="reader">Where from.</param>
    /// <returns>It.</returns>
    public static ReplicatedSense Read(ref BitReader reader) => new(
        (int)reader.ReadVarUInt(),
        (byte)reader.ReadBits(4),
        new Vector3f(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
        reader.ReadSingle(),
        reader.ReadSingle(),
        (int)reader.ReadVarUInt(),
        reader.ReadString(32));
}
