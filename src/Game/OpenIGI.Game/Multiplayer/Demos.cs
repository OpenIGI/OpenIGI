using System.Text;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>What a demo is of: the protocol it was recorded with, the title, map and mode, and whose game recorded it.</summary>
/// <param name="Protocol">The <see cref="MultiplayerProtocol.Version"/> it was recorded with: only the same plays it back.</param>
/// <param name="Title">The title's id, as <c>igi1</c>.</param>
/// <param name="Map">The map the session began on.</param>
/// <param name="Mode">The mode it played.</param>
/// <param name="Player">The name of the player whose game recorded it.</param>
public sealed record DemoHeader(int Protocol, string Title, string Map, string Mode, string Player);

/// <summary>One message the server delivered to the recording client.</summary>
/// <param name="Seconds">When, in seconds from the session's first message.</param>
/// <param name="Channel">The channel it came on.</param>
/// <param name="Data">The message.</param>
public readonly record struct DemoMessage(double Seconds, NetChannel Channel, byte[] Data);

/// <summary>
/// A client's session as the server delivered it, every message from the first, kept in memory so the session can be saved as
/// a demo at any point: a snapshot is written against ones before it, so a demo is only whole from its session's start.
/// </summary>
/// <remarks>Capped at <see cref="MaxBytes"/>: past it the rest is not kept, and a demo saved holds the session up to there.</remarks>
public sealed class DemoRecording
{
    /// <summary>The most a recording holds: well over an hour of a busy co-op level, and many of a match.</summary>
    public const long MaxBytes = 128L * 1024 * 1024;

    private readonly List<DemoMessage> _messages = [];
    private double? _start;

    /// <summary>Gets the bytes of message held.</summary>
    public long Bytes { get; private set; }

    /// <summary>Gets a value indicating whether the cap was reached and later messages were not kept.</summary>
    public bool IsFull { get; private set; }

    /// <summary>Gets the messages held.</summary>
    public IReadOnlyList<DemoMessage> Messages => _messages;

    /// <summary>Gets the seconds the recording covers.</summary>
    public double Seconds => _messages.Count == 0 ? 0 : _messages[^1].Seconds;

    /// <summary>Keeps a message the server delivered.</summary>
    /// <param name="now">The session's clock, in seconds.</param>
    /// <param name="channel">The channel it came on.</param>
    /// <param name="data">The message.</param>
    public void Add(double now, NetChannel channel, ReadOnlySpan<byte> data)
    {
        if (IsFull)
        {
            return;
        }

        if (Bytes + data.Length > MaxBytes)
        {
            IsFull = true;
            return;
        }

        _start ??= now;
        _messages.Add(new DemoMessage(now - _start.Value, channel, data.ToArray()));
        Bytes += data.Length;
    }
}

/// <summary>A demo on disk: a header, then each message with its time and channel.</summary>
public static class DemoFile
{
    /// <summary>The extension demos are saved with.</summary>
    public const string Extension = ".oidemo";

    private static readonly byte[] Magic = "OIGIDEMO"u8.ToArray();
    private const int FormatVersion = 1;

    // A message larger than any the session sends: a file claiming one is not a demo.
    private const int MaxMessageBytes = 1 << 20;

    /// <summary>Saves a recording as a demo file, its folder made if need be.</summary>
    /// <param name="path">Where to.</param>
    /// <param name="header">What it is of.</param>
    /// <param name="recording">The recording.</param>
    /// <returns>A line saying what was saved, for the log.</returns>
    public static string Save(string path, DemoHeader header, DemoRecording recording)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(recording);
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        using (FileStream file = File.Create(path))
        {
            Write(file, header, recording.Messages);
        }

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Saved {recording.Seconds:0} s ({recording.Bytes / 1024} KiB) to {path}{(recording.IsFull ? "; the recording had filled, so it ends early" : string.Empty)}.");
    }

    /// <summary>Writes a demo.</summary>
    /// <param name="stream">Where to.</param>
    /// <param name="header">What it is of.</param>
    /// <param name="messages">The messages, in the order they came.</param>
    public static void Write(Stream stream, DemoHeader header, IReadOnlyList<DemoMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(messages);
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(header.Protocol);
        writer.Write(header.Title);
        writer.Write(header.Map);
        writer.Write(header.Mode);
        writer.Write(header.Player);
        writer.Write(messages.Count);
        foreach (DemoMessage message in messages)
        {
            writer.Write(message.Seconds);
            writer.Write((byte)message.Channel);
            writer.Write(message.Data.Length);
            writer.Write(message.Data);
        }
    }

    /// <summary>Reads a demo.</summary>
    /// <param name="stream">Where from.</param>
    /// <returns>Its header and messages.</returns>
    /// <exception cref="InvalidDataException">It is not a demo, or not one this game reads.</exception>
    public static (DemoHeader Header, IReadOnlyList<DemoMessage> Messages) Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
            {
                throw new InvalidDataException("not an OpenIGI demo");
            }

            if (reader.ReadInt32() is var format && format != FormatVersion)
            {
                throw new InvalidDataException($"demo format {format}, this game reads {FormatVersion}");
            }

            DemoHeader header = new(reader.ReadInt32(), reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadString());
            int count = reader.ReadInt32();
            if (count < 0)
            {
                throw new InvalidDataException("a negative message count");
            }

            List<DemoMessage> messages = new(Math.Min(count, 1 << 16));
            for (int i = 0; i < count; i++)
            {
                double seconds = reader.ReadDouble();
                NetChannel channel = (NetChannel)reader.ReadByte();
                int length = reader.ReadInt32();
                if (length is < 0 or > MaxMessageBytes)
                {
                    throw new InvalidDataException($"a message of {length} bytes");
                }

                byte[] data = reader.ReadBytes(length);
                if (data.Length != length)
                {
                    throw new InvalidDataException("the demo ends mid-message");
                }

                messages.Add(new DemoMessage(seconds, channel, data));
            }

            return (header, messages);
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("the demo ends early", e);
        }
    }
}
