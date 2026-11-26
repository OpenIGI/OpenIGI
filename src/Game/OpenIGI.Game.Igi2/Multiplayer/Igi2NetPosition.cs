using OpenIGI.Core.Numerics;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>
/// A place in the level as the session carries what it fires and what moves: to a quarter of a world unit -- a muzzle a whole unit
/// off would send its round off its line -- with room left for the widest level.
/// </summary>
internal static class Igi2NetPosition
{
    private const double Steps = 4.0;

    /// <summary>Writes a place.</summary>
    /// <param name="writer">Where to.</param>
    /// <param name="v">The place, world units.</param>
    public static void Write(BitWriter writer, Vector3d v)
    {
        writer.WriteVarInt((int)Math.Round(v.X * Steps));
        writer.WriteVarInt((int)Math.Round(v.Y * Steps));
        writer.WriteVarInt((int)Math.Round(v.Z * Steps));
    }

    /// <summary>Reads a place <see cref="Write"/> wrote.</summary>
    /// <param name="reader">Where from.</param>
    /// <returns>The place, world units.</returns>
    public static Vector3d Read(ref BitReader reader) =>
        new(reader.ReadVarInt() / Steps, reader.ReadVarInt() / Steps, reader.ReadVarInt() / Steps);
}
