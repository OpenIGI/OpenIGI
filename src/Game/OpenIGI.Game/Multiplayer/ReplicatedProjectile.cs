using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>One thing a player's weapon has in the world as the server has it, for every other game to draw.</summary>
/// <param name="Owner">The entity whose weapon it is; the soldiers' grenades are the host's.</param>
/// <param name="Position">Where it is, in whole world units.</param>
/// <param name="Orientation">Its frame.</param>
/// <param name="Model">The model it is drawn with.</param>
/// <remarks>
/// A grenade, a mine, a rocket or a launcher round flies, rests and goes off on the server; another game draws it where the
/// server has it and shows its blast when the server's goes off. The game whose weapon it is draws its own copy.
/// </remarks>
public readonly record struct ReplicatedProjectile(int Owner, Int3 Position, Matrix3f Orientation, string Model)
{
    /// <summary>The most a snapshot carries.</summary>
    public const int MaxPerSnapshot = 64;

    private const int FrameBits = 12;
    private const int MaxModelLength = 16;

    /// <summary>Gets one from what is drawn.</summary>
    /// <param name="owner">Whose it is.</param>
    /// <param name="shown">It.</param>
    /// <returns>The record.</returns>
    public static ReplicatedProjectile Of(int owner, in ShownProjectile shown) =>
        new(owner, Int3.From(shown.Position), shown.Orientation, shown.Model);

    /// <summary>Writes one.</summary>
    /// <param name="writer">Where to.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteVarUInt((uint)(Owner + 1));
        writer.WriteVarInt(Position.X);
        writer.WriteVarInt(Position.Y);
        writer.WriteVarInt(Position.Z);
        Matrix3f o = Orientation;
        foreach (float f in (ReadOnlySpan<float>)[o.M11, o.M12, o.M13, o.M21, o.M22, o.M23, o.M31, o.M32, o.M33])
        {
            writer.WriteQuantized(f, -1f, 1f, FrameBits);
        }

        writer.WriteString(Model, MaxModelLength);
    }

    /// <summary>Reads one.</summary>
    /// <param name="reader">Where from.</param>
    /// <returns>It.</returns>
    public static ReplicatedProjectile Read(ref BitReader reader)
    {
        int owner = (int)reader.ReadVarUInt() - 1;
        Int3 position = new(reader.ReadVarInt(), reader.ReadVarInt(), reader.ReadVarInt());
        Span<float> f = stackalloc float[9];
        for (int i = 0; i < f.Length; i++)
        {
            f[i] = reader.ReadQuantized(-1f, 1f, FrameBits);
        }

        string model = reader.ReadString(MaxModelLength);
        return new ReplicatedProjectile(
            owner,
            position,
            Matrix3f.FromRows(new Vector3f(f[0], f[1], f[2]), new Vector3f(f[3], f[4], f[5]), new Vector3f(f[6], f[7], f[8])),
            model);
    }
}
