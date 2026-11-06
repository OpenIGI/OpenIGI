using OpenIGI.Core.Numerics;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>
/// One hit of the session as the two in it learn of it: the shooter, for its hit marker, and the one hit, for the mark of where
/// it came from. A hit on one of the level's soldiers has no victim to tell.
/// </summary>
/// <param name="Attacker">The shooter's entity, or -1 for the level (a guard, a blast nobody threw).</param>
/// <param name="Victim">The entity hit, or -1 for one of the level's soldiers.</param>
/// <param name="Damage">How much of the victim's health it took, in <c>[0,1]</c>; 0 where it is not known.</param>
/// <param name="Killed">Whether it killed.</param>
/// <param name="Head">Whether it was to the head.</param>
/// <param name="From">Where the shot came from, in world units.</param>
public readonly record struct CombatNote(int Attacker, int Victim, float Damage, bool Killed, bool Head, Vector3d From)
{
    /// <summary>Writes the note, after its message's id.</summary>
    /// <param name="writer">Where to.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteRanged(Attacker + 1, 0, 1023);
        writer.WriteRanged(Victim + 1, 0, 1023);
        writer.WriteRanged((int)Math.Round(Math.Clamp(Damage, 0f, 1f) * 255), 0, 255);
        writer.WriteBool(Killed);
        writer.WriteBool(Head);
        writer.WriteBits((uint)(int)From.X, 32);
        writer.WriteBits((uint)(int)From.Y, 32);
        writer.WriteBits((uint)(int)From.Z, 32);
    }

    /// <summary>Reads a note <see cref="Write"/> wrote.</summary>
    /// <param name="reader">Where from.</param>
    /// <returns>The note, or <see langword="null"/> for one cut short.</returns>
    public static CombatNote? Read(ref BitReader reader)
    {
        int attacker = reader.ReadRanged(0, 1023) - 1;
        int victim = reader.ReadRanged(0, 1023) - 1;
        float damage = reader.ReadRanged(0, 255) / 255f;
        bool killed = reader.ReadBool();
        bool head = reader.ReadBool();
        Vector3d from = new((int)reader.ReadBits(32), (int)reader.ReadBits(32), (int)reader.ReadBits(32));
        return reader.IsOverflowed ? null : new CombatNote(attacker, victim, damage, killed, head, from);
    }
}
