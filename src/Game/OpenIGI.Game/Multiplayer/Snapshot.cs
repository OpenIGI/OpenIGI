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
/// <param name="Senses">What players felt since the server's last snapshot, for each one's own game to feel.</param>
/// <param name="Lifts">The level's lifts as the server has them, in the level's own order, or <see langword="null"/> where the world sends none.</param>
/// <param name="Cameras">
/// The level's security cameras as the server has them, in the level's own order, or <see langword="null"/> where the world
/// sends none.
/// </param>
/// <param name="Wrecks">
/// What the server's level has struck for good -- broken, blown up, shot out -- one bit each in the world's own order, or
/// <see langword="null"/> where the world sends none.
/// </param>
/// <param name="Projectiles">
/// What the players' weapons have in the world -- grenades, mines, rockets, launcher rounds -- or <see langword="null"/>
/// for none.
/// </param>
/// <param name="Tasks">
/// What the server's switches, terminals, TBAs, cabinets, locks, fences, generators and radios are, as the world writes them,
/// or <see langword="null"/> where it sends none.
/// </param>
public sealed record Snapshot(
    long Tick,
    ReplicatedHuman[] Humans,
    byte[]? Doors = null,
    ReplicatedImpact[]? Impacts = null,
    ReplicatedSense[]? Senses = null,
    ReplicatedLift[]? Lifts = null,
    ReplicatedCamera[]? Cameras = null,
    byte[]? Wrecks = null,
    ReplicatedProjectile[]? Projectiles = null,
    byte[]? Tasks = null)
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
    private const int FieldCount = 15;

    // A level's doors, eight to a byte: the largest of the original's levels has some hundreds.
    private const int MaxDoorBytes = 512;

    // The level's wrecks, eight to a byte: its panes, barrels, cars, speakers, cameras and switches.
    private const int MaxWreckBytes = 1024;

    // The level's tasks' state: a few bits each, and a plant's place.
    private const int MaxTaskBytes = 4096;
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

        // The rest of the world -- doors, lifts, cameras, wrecks, projectiles, tasks: one bit for all of it while it stands as
        // the baseline had it, which is most ticks; else one bit each.
        bool worldSame = baseline is not null && SameWorld(snapshot, baseline);
        writer.WriteBool(worldSame);

        // The doors: one bit when they stand as the baseline had them, which is nearly always.
        byte[] doors = snapshot.Doors ?? [];
        bool same = baseline is not null && doors.AsSpan().SequenceEqual(baseline.Doors ?? []);
        if (!worldSame)
        {
            writer.WriteBool(same);
        }

        if (!same)
        {
            writer.WriteVarUInt((uint)doors.Length);
            foreach (byte b in doors)
            {
                writer.WriteBits(b, 8);
            }
        }

        // The lifts: one bit when they stand as the baseline had them, which is all but while one moves.
        ReplicatedLift[] lifts = snapshot.Lifts ?? [];
        bool liftsSame = baseline is not null && lifts.AsSpan().SequenceEqual(baseline.Lifts ?? []);
        if (!worldSame)
        {
            writer.WriteBool(liftsSame);
        }

        if (!liftsSame)
        {
            int count = Math.Min(lifts.Length, ReplicatedLift.MaxPerSnapshot);
            writer.WriteVarUInt((uint)count);
            for (int i = 0; i < count; i++)
            {
                lifts[i].Write(writer);
            }
        }

        // The cameras: one bit when they stand as the baseline had them, which is while none sweeps or sees anyone.
        ReplicatedCamera[] cameras = snapshot.Cameras ?? [];
        bool camerasSame = baseline is not null && cameras.AsSpan().SequenceEqual(baseline.Cameras ?? []);
        if (!worldSame)
        {
            writer.WriteBool(camerasSame);
        }

        if (!camerasSame)
        {
            int count = Math.Min(cameras.Length, ReplicatedCamera.MaxPerSnapshot);
            writer.WriteVarUInt((uint)count);
            for (int i = 0; i < count; i++)
            {
                cameras[i].Write(writer);
            }
        }

        // The wrecks: one bit when they stand as the baseline had them, which is all but the tick something breaks.
        byte[] wrecks = snapshot.Wrecks ?? [];
        bool wrecksSame = baseline is not null && wrecks.AsSpan().SequenceEqual(baseline.Wrecks ?? []);
        if (!worldSame)
        {
            writer.WriteBool(wrecksSame);
        }

        if (!wrecksSame)
        {
            int length = Math.Min(wrecks.Length, MaxWreckBytes);
            writer.WriteVarUInt((uint)length);
            for (int i = 0; i < length; i++)
            {
                writer.WriteBits(wrecks[i], 8);
            }
        }

        // The players' grenades, mines, rockets and launcher rounds: one bit while they lie as the baseline had them.
        ReplicatedProjectile[] projectiles = snapshot.Projectiles ?? [];
        bool projectilesSame = baseline is not null && projectiles.AsSpan().SequenceEqual(baseline.Projectiles ?? []);
        if (!worldSame)
        {
            writer.WriteBool(projectilesSame);
        }

        if (!projectilesSame)
        {
            int count = Math.Min(projectiles.Length, ReplicatedProjectile.MaxPerSnapshot);
            writer.WriteVarUInt((uint)count);
            for (int i = 0; i < count; i++)
            {
                projectiles[i].Write(writer);
            }
        }

        // The tasks: one bit while they stand as the baseline had them.
        if (!worldSame)
        {
            WriteBlob(writer, snapshot.Tasks, baseline is null ? null : baseline.Tasks ?? [], MaxTaskBytes);
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

        // What players felt: events too, written whole.
        ReplicatedSense[] senses = snapshot.Senses ?? [];
        int firstSense = Math.Max(0, senses.Length - ReplicatedSense.MaxPerSnapshot);
        writer.WriteBool(senses.Length > firstSense);
        if (senses.Length > firstSense)
        {
            writer.WriteVarUInt((uint)(senses.Length - firstSense));
            for (int i = firstSense; i < senses.Length; i++)
            {
                senses[i].Write(writer);
            }
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

        bool worldSame = reader.ReadBool();
        if (worldSame && baseline is null)
        {
            return false;
        }

        byte[]? doors = baseline?.Doors;
        if (!worldSame && !reader.ReadBool())
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

        ReplicatedLift[]? lifts = baseline?.Lifts;
        if (!worldSame && !reader.ReadBool())
        {
            uint length = reader.ReadVarUInt();
            if (reader.IsOverflowed || length > ReplicatedLift.MaxPerSnapshot)
            {
                return false;
            }

            lifts = length == 0 ? null : new ReplicatedLift[length];
            for (int i = 0; i < length; i++)
            {
                lifts![i] = ReplicatedLift.Read(ref reader);
            }
        }
        else if (baseline is null)
        {
            return false;
        }

        ReplicatedCamera[]? cameras = baseline?.Cameras;
        if (!worldSame && !reader.ReadBool())
        {
            uint length = reader.ReadVarUInt();
            if (reader.IsOverflowed || length > ReplicatedCamera.MaxPerSnapshot)
            {
                return false;
            }

            cameras = length == 0 ? null : new ReplicatedCamera[length];
            for (int i = 0; i < length; i++)
            {
                cameras![i] = ReplicatedCamera.Read(ref reader);
            }
        }
        else if (baseline is null)
        {
            return false;
        }

        byte[]? wreckBits = baseline?.Wrecks;
        if (!worldSame && !reader.ReadBool())
        {
            uint length = reader.ReadVarUInt();
            if (reader.IsOverflowed || length > MaxWreckBytes)
            {
                return false;
            }

            wreckBits = length == 0 ? null : new byte[length];
            for (int i = 0; i < length; i++)
            {
                wreckBits![i] = (byte)reader.ReadBits(8);
            }
        }
        else if (baseline is null)
        {
            return false;
        }

        ReplicatedProjectile[]? projectiles = baseline?.Projectiles;
        if (!worldSame && !reader.ReadBool())
        {
            uint length = reader.ReadVarUInt();
            if (reader.IsOverflowed || length > ReplicatedProjectile.MaxPerSnapshot)
            {
                return false;
            }

            projectiles = length == 0 ? null : new ReplicatedProjectile[length];
            for (int i = 0; i < length; i++)
            {
                projectiles![i] = ReplicatedProjectile.Read(ref reader);
            }
        }
        else if (baseline is null)
        {
            return false;
        }

        byte[]? tasks = baseline?.Tasks;
        if (!worldSame && !ReadBlob(ref reader, baseline, baseline?.Tasks, MaxTaskBytes, out tasks))
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

        uint senseCount = reader.ReadBool() ? reader.ReadVarUInt() : 0;
        if (reader.IsOverflowed || senseCount > ReplicatedSense.MaxPerSnapshot)
        {
            return false;
        }

        ReplicatedSense[]? senses = senseCount == 0 ? null : new ReplicatedSense[senseCount];
        for (int i = 0; i < senseCount; i++)
        {
            senses![i] = ReplicatedSense.Read(ref reader);
        }

        if (reader.IsOverflowed)
        {
            return false;
        }

        snapshot = new Snapshot(tick, humans, doors, impacts, senses, lifts, cameras, wreckBits, projectiles, tasks);
        return true;
    }

    private static bool SameWorld(Snapshot a, Snapshot b) =>
        (a.Doors ?? []).AsSpan().SequenceEqual(b.Doors ?? [])
        && (a.Lifts ?? []).AsSpan().SequenceEqual(b.Lifts ?? [])
        && (a.Cameras ?? []).AsSpan().SequenceEqual(b.Cameras ?? [])
        && (a.Wrecks ?? []).AsSpan().SequenceEqual(b.Wrecks ?? [])
        && (a.Projectiles ?? []).AsSpan().SequenceEqual(b.Projectiles ?? [])
        && (a.Tasks ?? []).AsSpan().SequenceEqual(b.Tasks ?? []);

    // A blob of the world's: one bit when it is as the baseline had it, else its bytes.
    private static void WriteBlob(BitWriter writer, byte[]? blob, byte[]? baseline, int maxBytes)
    {
        byte[] bytes = blob ?? [];
        bool same = baseline is not null && bytes.AsSpan().SequenceEqual(baseline);
        writer.WriteBool(same);
        if (!same)
        {
            int length = Math.Min(bytes.Length, maxBytes);
            writer.WriteVarUInt((uint)length);
            for (int i = 0; i < length; i++)
            {
                writer.WriteBits(bytes[i], 8);
            }
        }
    }

    private static bool ReadBlob(ref BitReader reader, Snapshot? baseline, byte[]? kept, int maxBytes, out byte[]? blob)
    {
        blob = kept;
        if (reader.ReadBool())
        {
            return baseline is not null;
        }

        uint length = reader.ReadVarUInt();
        if (reader.IsOverflowed || length > maxBytes)
        {
            return false;
        }

        blob = length == 0 ? null : new byte[length];
        for (int i = 0; i < length; i++)
        {
            blob![i] = (byte)reader.ReadBits(8);
        }

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
        if (a.Source != b.Source) { mask |= 1u << 13; }
        if (a.Platform != b.Platform) { mask |= 1u << 14; }
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
        if ((mask & (1u << 13)) != 0) { writer.WriteVarUInt(human.Source); }
        if ((mask & (1u << 14)) != 0) { writer.WriteBits(human.Platform, 8); }
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
        if ((mask & (1u << 13)) != 0) { human = human with { Source = (ushort)reader.ReadVarUInt() }; }
        if ((mask & (1u << 14)) != 0) { human = human with { Platform = (byte)reader.ReadBits(8) }; }
        return human;
    }
}
