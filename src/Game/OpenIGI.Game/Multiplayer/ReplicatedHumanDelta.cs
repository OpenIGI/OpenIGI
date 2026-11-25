using OpenIGI.Core.Numerics;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>
/// A human's replicated state written as what changed from the state before it: a bit for each field that did, then those fields,
/// a position as how far it moved. What a snapshot carries of every human, and a ghost's track of its runner tick by tick.
/// </summary>
public static class ReplicatedHumanDelta
{
    /// <summary>Which of a human's fields changed from one state to the next, a bit each.</summary>
    /// <param name="a">The state before.</param>
    /// <param name="b">The state after.</param>
    /// <returns>The bits of the fields that differ.</returns>
    public static uint Changed(in ReplicatedHuman a, in ReplicatedHuman b)
    {
        uint mask = 0;
        if (a.Position != b.Position) { mask |= 1u << 0; }
        if (!Same(a.Velocity, b.Velocity)) { mask |= 1u << 1; }
        if (a.Yaw != b.Yaw) { mask |= 1u << 2; }
        if (a.Pitch != b.Pitch) { mask |= 1u << 3; }
        if (a.State != b.State) { mask |= 1u << 4; }
        if (a.Action != b.Action) { mask |= 1u << 5; }
        if (a.Weapon != b.Weapon) { mask |= 1u << 6; }
        if (a.Health != b.Health) { mask |= 1u << 7; }
        if (a.Team != b.Team) { mask |= 1u << 8; }
        if (a.Flags != b.Flags) { mask |= 1u << 9; }
        if (a.Shots != b.Shots) { mask |= 1u << 10; }
        if (a.ActionSequence != b.ActionSequence) { mask |= 1u << 11; }
        if (a.ClipTime != b.ClipTime) { mask |= 1u << 12; }
        if (a.Source != b.Source) { mask |= 1u << 13; }
        if (a.Platform != b.Platform) { mask |= 1u << 14; }
        if (a.Lean != b.Lean || a.Aim != b.Aim) { mask |= 1u << 15; }
        return mask;
    }

    // Whether two vectors are the same bits.
    private static bool Same(Vector3f a, Vector3f b) =>
        BitConverter.SingleToUInt32Bits(a.X) == BitConverter.SingleToUInt32Bits(b.X)
        && BitConverter.SingleToUInt32Bits(a.Y) == BitConverter.SingleToUInt32Bits(b.Y)
        && BitConverter.SingleToUInt32Bits(a.Z) == BitConverter.SingleToUInt32Bits(b.Z);

    /// <summary>Writes the fields a mask names, a position as its move from the state before.</summary>
    /// <param name="writer">Where they go.</param>
    /// <param name="mask">The fields, as <see cref="Changed"/> names them.</param>
    /// <param name="before">The state before.</param>
    /// <param name="human">The state.</param>
    public static void Write(BitWriter writer, uint mask, in ReplicatedHuman before, in ReplicatedHuman human)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if ((mask & (1u << 0)) != 0)
        {
            writer.WriteVarInt(unchecked(human.Position.X - before.Position.X));
            writer.WriteVarInt(unchecked(human.Position.Y - before.Position.Y));
            writer.WriteVarInt(unchecked(human.Position.Z - before.Position.Z));
        }

        if ((mask & (1u << 1)) != 0)
        {
            writer.WriteSingle(human.Velocity.X);
            writer.WriteSingle(human.Velocity.Y);
            writer.WriteSingle(human.Velocity.Z);
        }

        if ((mask & (1u << 2)) != 0) { writer.WriteBits(human.Yaw, 16); }
        if ((mask & (1u << 3)) != 0) { writer.WriteBits(human.Pitch, 16); }
        if ((mask & (1u << 4)) != 0) { writer.WriteVarUInt(human.State); }
        if ((mask & (1u << 5)) != 0) { writer.WriteVarUInt(human.Action); }
        if ((mask & (1u << 6)) != 0) { writer.WriteVarUInt(human.Weapon); }
        if ((mask & (1u << 7)) != 0) { writer.WriteBits(human.Health, 8); }
        if ((mask & (1u << 8)) != 0) { writer.WriteBits(human.Team, 8); }
        if ((mask & (1u << 9)) != 0) { writer.WriteBits((uint)human.Flags, 8); }
        if ((mask & (1u << 10)) != 0) { writer.WriteBits(human.Shots, 8); }
        if ((mask & (1u << 11)) != 0) { writer.WriteBits(human.ActionSequence, 8); }
        if ((mask & (1u << 12)) != 0) { writer.WriteBits(human.ClipTime, 16); }
        if ((mask & (1u << 13)) != 0) { writer.WriteVarUInt(human.Source); }
        if ((mask & (1u << 14)) != 0) { writer.WriteBits(human.Platform, 8); }
        if ((mask & (1u << 15)) != 0)
        {
            writer.WriteBits(unchecked((byte)human.Lean), 8);
            writer.WriteBits(human.Aim, 8);
        }
    }

    /// <summary>Reads the fields a mask names onto the state before.</summary>
    /// <param name="reader">Where they come from.</param>
    /// <param name="mask">The fields, as <see cref="Changed"/> names them.</param>
    /// <param name="human">The state before.</param>
    /// <returns>The state.</returns>
    public static ReplicatedHuman Read(ref BitReader reader, uint mask, ReplicatedHuman human)
    {
        if ((mask & (1u << 0)) != 0)
        {
            human = human with
            {
                Position = new Int3(
                    unchecked(human.Position.X + reader.ReadVarInt()),
                    unchecked(human.Position.Y + reader.ReadVarInt()),
                    unchecked(human.Position.Z + reader.ReadVarInt())),
            };
        }

        if ((mask & (1u << 1)) != 0)
        {
            human = human with { Velocity = new Vector3f(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()) };
        }

        if ((mask & (1u << 2)) != 0) { human = human with { Yaw = (ushort)reader.ReadBits(16) }; }
        if ((mask & (1u << 3)) != 0) { human = human with { Pitch = (ushort)reader.ReadBits(16) }; }
        if ((mask & (1u << 4)) != 0) { human = human with { State = (ushort)reader.ReadVarUInt() }; }
        if ((mask & (1u << 5)) != 0) { human = human with { Action = (ushort)reader.ReadVarUInt() }; }
        if ((mask & (1u << 6)) != 0) { human = human with { Weapon = (ushort)reader.ReadVarUInt() }; }
        if ((mask & (1u << 7)) != 0) { human = human with { Health = (byte)reader.ReadBits(8) }; }
        if ((mask & (1u << 8)) != 0) { human = human with { Team = (byte)reader.ReadBits(8) }; }
        if ((mask & (1u << 9)) != 0) { human = human with { Flags = (ReplicatedHumanCondition)reader.ReadBits(8) }; }
        if ((mask & (1u << 10)) != 0) { human = human with { Shots = (byte)reader.ReadBits(8) }; }
        if ((mask & (1u << 11)) != 0) { human = human with { ActionSequence = (byte)reader.ReadBits(8) }; }
        if ((mask & (1u << 12)) != 0) { human = human with { ClipTime = (ushort)reader.ReadBits(16) }; }
        if ((mask & (1u << 13)) != 0) { human = human with { Source = (ushort)reader.ReadVarUInt() }; }
        if ((mask & (1u << 14)) != 0) { human = human with { Platform = (byte)reader.ReadBits(8) }; }
        if ((mask & (1u << 15)) != 0)
        {
            human = human with { Lean = unchecked((sbyte)(byte)reader.ReadBits(8)), Aim = (byte)reader.ReadBits(8) };
        }

        return human;
    }
}
