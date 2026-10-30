using OpenIGI.Core.Numerics;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>The world as the server had it at the end of a tick: every human it sends, by id.</summary>
/// <param name="Tick">The server tick.</param>
/// <param name="Humans">The humans, in ascending id order.</param>
/// <param name="Doors">
/// The level's doors as the server has them, one bit each in the level's own order (set: commanded open), or
/// <see langword="null"/> where the world sends none.
/// </param>
/// <param name="Impacts">The rounds that landed since the server's last snapshot, for every client to show.</param>
public sealed record Snapshot(long Tick, ReplicatedHuman[] Humans, byte[]? Doors = null, ReplicatedImpact[]? Impacts = null)
{
    /// <summary>Finds a human by id.</summary>
    /// <param name="id">The id.</param>
    /// <param name="human">The human, when present.</param>
    /// <returns><see langword="true"/> when the snapshot holds it.</returns>
    public bool TryGet(int id, out ReplicatedHuman human)
    {
        int lo = 0;
        int hi = Humans.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int at = Humans[mid].Id;
            if (at == id)
            {
                human = Humans[mid];
                return true;
            }

            if (at < id)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        human = default;
        return false;
    }
}

/// <summary>Writes a snapshot as its difference from one the client is known to hold, and reads it back.</summary>
/// <remarks>
/// <para>
/// Quake 3's scheme, which Source keeps: the server writes each client's snapshot against the newest one that client has
/// acknowledged, so a lost snapshot costs nothing but a bigger next one. Every human present is listed, by id (one bit when it
/// follows the last); one the baseline also held carries one bit when nothing changed and otherwise only the fields that did,
/// behind a bit each; one it did not carries every field. A human in the baseline but not the list has gone. An idle
/// player costs three bits.
/// </para>
/// <para>
/// Positions are written as their change in whole world units, which for a human walking is a byte or two an axis; the
/// rest exactly, so what a client draws is bit for bit what the server sent.
/// </para>
/// </remarks>
public static class SnapshotCodec
{
    private const int FieldCount = 13;

    // A level's doors, eight to a byte: the largest of the original's levels has some hundreds.
    private const int MaxDoorBytes = 512;
    private const uint AllFields = (1u << FieldCount) - 1;

    /// <summary>Writes a snapshot.</summary>
    /// <param name="writer">Where to.</param>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="baseline">One the receiver holds, or <see langword="null"/> to write it whole.</param>
    public static void Write(BitWriter writer, Snapshot snapshot, Snapshot? baseline)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(snapshot);
        writer.WriteBits((uint)snapshot.Tick, 32);
        writer.WriteBits((uint)(snapshot.Tick >> 32), 32);
        writer.WriteBool(baseline is not null);
        if (baseline is not null)
        {
            writer.WriteVarUInt((uint)(snapshot.Tick - baseline.Tick));
        }

        writer.WriteVarUInt((uint)snapshot.Humans.Length);
        int previousId = -1;
        foreach (ReplicatedHuman human in snapshot.Humans)
        {
            bool follows = human.Id == previousId + 1;
            writer.WriteBool(follows);
            if (!follows)
            {
                writer.WriteVarUInt((uint)(human.Id - previousId - 2));
            }

            previousId = human.Id;
            ReplicatedHuman before = default;
            bool known = baseline is not null && baseline.TryGet(human.Id, out before);
            writer.WriteBool(known);
            uint mask = AllFields;
            if (known)
            {
                mask = Changed(before, human);
                writer.WriteBool(mask != 0);
                if (mask != 0)
                {
                    writer.WriteBits(mask, FieldCount);
                }
            }

            WriteFields(writer, mask, before, human);
        }

        // The doors: one bit when they stand as the baseline had them, which is nearly always.
        byte[] doors = snapshot.Doors ?? [];
        bool same = baseline is not null && doors.AsSpan().SequenceEqual(baseline.Doors ?? []);
        writer.WriteBool(same);
        if (!same)
        {
            writer.WriteVarUInt((uint)doors.Length);
            foreach (byte b in doors)
            {
                writer.WriteBits(b, 8);
            }
        }

        // The rounds that landed: events, not state, so written whole and never against the baseline.
        ReplicatedImpact[] impacts = snapshot.Impacts ?? [];
        int first = Math.Max(0, impacts.Length - ReplicatedImpact.MaxPerSnapshot);
        writer.WriteBool(impacts.Length > first);
        if (impacts.Length > first)
        {
            writer.WriteVarUInt((uint)(impacts.Length - first));
        }

        for (int i = first; i < impacts.Length; i++)
        {
            impacts[i].Write(writer);
        }
    }

    /// <summary>Reads a snapshot.</summary>
    /// <param name="reader">Where from.</param>
    /// <param name="baselineByTick">The snapshots the reader holds, by tick, for the baseline the writer named.</param>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>
    /// <see langword="false"/> when the bytes are malformed or the baseline is no longer held; the snapshot is then dropped and
    /// the next, written against an older acknowledgement, is read instead.
    /// </returns>
    public static bool Read(ref BitReader reader, Func<long, Snapshot?> baselineByTick, out Snapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(baselineByTick);
        snapshot = null;
        long tick = reader.ReadBits(32) | ((long)reader.ReadBits(32) << 32);
        Snapshot? baseline = null;
        if (reader.ReadBool())
        {
            long baselineTick = tick - reader.ReadVarUInt();
            baseline = baselineByTick(baselineTick);
            if (baseline is null)
            {
                return false;
            }
        }

        uint count = reader.ReadVarUInt();
        if (reader.IsOverflowed || count > 1024)
        {
            return false;
        }

        ReplicatedHuman[] humans = new ReplicatedHuman[count];
        int previousId = -1;
        for (int i = 0; i < humans.Length; i++)
        {
            int id = reader.ReadBool() ? previousId + 1 : previousId + 2 + (int)reader.ReadVarUInt();
            previousId = id;
            bool known = reader.ReadBool();
            uint mask = AllFields;
            ReplicatedHuman before = default;
            if (known)
            {
                if (baseline is null || !baseline.TryGet(id, out before))
                {
                    return false;
                }

                mask = reader.ReadBool() ? reader.ReadBits(FieldCount) : 0;
            }

            humans[i] = ReadFields(ref reader, mask, before with { Id = id });
            if (reader.IsOverflowed)
            {
                return false;
            }
        }

        byte[]? doors = baseline?.Doors;
        if (!reader.ReadBool())
        {
            uint length = reader.ReadVarUInt();
            if (reader.IsOverflowed || length > MaxDoorBytes)
            {
                return false;
            }

            doors = length == 0 ? null : new byte[length];
            for (int i = 0; i < length; i++)
            {
                doors![i] = (byte)reader.ReadBits(8);
            }
        }
        else if (baseline is null)
        {
            return false;
        }

        uint impactCount = reader.ReadBool() ? reader.ReadVarUInt() : 0;
        if (reader.IsOverflowed || impactCount > ReplicatedImpact.MaxPerSnapshot)
        {
            return false;
        }

        ReplicatedImpact[]? impacts = impactCount == 0 ? null : new ReplicatedImpact[impactCount];
        for (int i = 0; i < impactCount; i++)
        {
            impacts![i] = ReplicatedImpact.Read(ref reader);
        }

        if (reader.IsOverflowed)
        {
            return false;
        }

        snapshot = new Snapshot(tick, humans, doors, impacts);
        return true;
    }

    private static uint Changed(in ReplicatedHuman a, in ReplicatedHuman b)
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
        return mask;
    }

    private static bool Same(Vector3f a, Vector3f b) =>
        BitConverter.SingleToUInt32Bits(a.X) == BitConverter.SingleToUInt32Bits(b.X)
        && BitConverter.SingleToUInt32Bits(a.Y) == BitConverter.SingleToUInt32Bits(b.Y)
        && BitConverter.SingleToUInt32Bits(a.Z) == BitConverter.SingleToUInt32Bits(b.Z);

    private static void WriteFields(BitWriter writer, uint mask, in ReplicatedHuman before, in ReplicatedHuman human)
    {
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
    }

    private static ReplicatedHuman ReadFields(ref BitReader reader, uint mask, ReplicatedHuman human)
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
        return human;
    }
}
