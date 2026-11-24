using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;

namespace OpenIGI.Game.Replays;

/// <summary>
/// A ghost's track as bytes: its ticks as one block of bits, each tick a bit for whether the runner's body changed since the tick
/// before and, where it did, the fields that changed (<see cref="ReplicatedHumanDelta"/>) -- a still runner costs a bit a tick;
/// then its events. Read strictly and
/// within limits, as the rest of a replay is.
/// </summary>
public static class GhostTrackCodec
{
    // The most bytes the ticks' block may be: far past a two-hour track's, short of anything a replay file could hold.
    private const int MaxBlockBytes = 32 * 1024 * 1024;

    // How many fields a tick's mask may name.
    private const uint MaskBits = 16;

    /// <summary>Writes a track.</summary>
    /// <param name="writer">Where it goes.</param>
    /// <param name="track">The track.</param>
    public static void Write(BinaryWriter writer, GhostTrack track)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(track);
        BitWriter bits = new(Math.Max(256, track.Length * 4));
        ReplicatedHuman before = default;
        for (int tick = 0; tick < track.Length; tick++)
        {
            ReplicatedHuman frame = track.At(tick)!.Value with { Id = 0 };
            uint mask = ReplicatedHumanDelta.Changed(before, frame);
            bits.WriteBool(mask != 0);
            if (mask != 0)
            {
                bits.WriteVarUInt(mask);
                ReplicatedHumanDelta.Write(bits, mask, before, frame);
            }

            before = frame;
        }

        byte[] block = bits.ToArray();
        writer.Write7BitEncodedInt(track.Length);
        writer.Write7BitEncodedInt(block.Length);
        writer.Write(block);

        writer.Write7BitEncodedInt(track.Events.Count);
        int last = 0;
        foreach (GhostEvent happened in track.Events)
        {
            writer.Write7BitEncodedInt(happened.Tick - last);
            writer.Write((byte)happened.Kind);
            writer.Write7BitEncodedInt(happened.Detail);
            last = happened.Tick;
        }
    }

    /// <summary>Reads a track.</summary>
    /// <param name="reader">Where it comes from.</param>
    /// <returns>The track.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a track, or one past the limits.</exception>
    public static GhostTrack Read(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        GhostTrack track = new();
        int length = Count(reader, GhostTrack.MaxTicks, "ticks");
        int bytes = Count(reader, MaxBlockBytes, "bytes of ticks");
        byte[] block = reader.ReadBytes(bytes);
        if (block.Length != bytes)
        {
            throw new EndOfStreamException();
        }

        BitReader bits = new(block);
        ReplicatedHuman frame = default;
        for (int tick = 0; tick < length; tick++)
        {
            uint mask = bits.ReadBool() ? bits.ReadVarUInt() : 0;
            if (mask >> (int)MaskBits != 0)
            {
                throw new InvalidDataException($"The ghost's tick {tick} names fields no body has.");
            }

            frame = ReplicatedHumanDelta.Read(ref bits, mask, frame);
            if (bits.IsOverflowed)
            {
                throw new InvalidDataException($"The ghost's ticks end part way through tick {tick}.");
            }

            track.Set(tick, frame);
        }

        int events = Count(reader, GhostTrack.MaxEvents, "events");
        int at = 0;
        for (int i = 0; i < events; i++)
        {
            at += Count(reader, Math.Max(0, length - at), "ticks between events");
            byte kind = reader.ReadByte();
            if (!Enum.IsDefined((GhostEventKind)kind))
            {
                throw new InvalidDataException($"The ghost's event {i} is of no kind this build knows.");
            }

            track.Event(at, (GhostEventKind)kind, Count(reader, int.MaxValue, "event detail"));
        }

        return track;
    }

    private static int Count(BinaryReader reader, int limit, string what)
    {
        int count = reader.Read7BitEncodedInt();
        return count < 0 || count > limit ? throw new InvalidDataException($"The ghost has {count} {what}, past its limit of {limit}.") : count;
    }
}
