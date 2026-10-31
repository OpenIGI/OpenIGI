using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>Writes a client's newest commands into a packet and reads them back, small enough to send several every time.</summary>
/// <remarks>
/// <para>
/// Each packet carries the newest few commands, consecutive ticks ending at the newest, so a lost packet costs nothing
/// as long as one of the next few arrives (Source's <c>cl_cmdbackup</c>). Each is written against the one before it,
/// the first against an empty frame: a channel that did not change costs one bit. A changed channel holding 0 or 1, which
/// is what nearly every button channel holds, costs three; anything else costs its exact 32 bits, because the server must
/// run the very value the client predicted with.
/// </para>
/// </remarks>
public sealed class UserCommandCodec
{
    /// <summary>The most commands one packet carries.</summary>
    public const int MaxCommandsPerPacket = 8;

    // The furthest behind its tick a command's view tick is carried; a further one is carried as this (Source caps at a second).
    private const int MaxViewLag = 255;

    private const uint TagZero = 0;
    private const uint TagOne = 1;
    private const uint TagExact = 2;

    /// <summary>Initializes a new instance of the <see cref="UserCommandCodec"/> class.</summary>
    /// <param name="channelCount">The title's channel frame length.</param>
    public UserCommandCodec(int channelCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channelCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channelCount, 1024);
        ChannelCount = channelCount;
    }

    /// <summary>Gets the title's channel frame length.</summary>
    public int ChannelCount { get; }

    /// <summary>Writes consecutive commands, oldest first.</summary>
    /// <param name="writer">Where to.</param>
    /// <param name="commands">One to <see cref="MaxCommandsPerPacket"/> commands on consecutive ticks, oldest first.</param>
    /// <exception cref="ArgumentException">The commands are not consecutive, too many, or of the wrong length.</exception>
    public void Write(BitWriter writer, ReadOnlySpan<UserCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (commands.Length is < 1 or > MaxCommandsPerPacket)
        {
            throw new ArgumentException($"A packet carries 1 to {MaxCommandsPerPacket} commands.", nameof(commands));
        }

        long newest = commands[^1].Tick;
        writer.WriteRanged(commands.Length, 1, MaxCommandsPerPacket);
        writer.WriteBits((uint)newest, 32);
        writer.WriteBits((uint)(newest >> 32), 32);

        float[]? previous = null;
        for (int i = 0; i < commands.Length; i++)
        {
            UserCommand command = commands[i];
            if (command.Tick != newest - (commands.Length - 1 - i))
            {
                throw new ArgumentException("The commands must be on consecutive ticks, oldest first.", nameof(commands));
            }

            if (command.Channels.Length != ChannelCount)
            {
                throw new ArgumentException($"A command has {ChannelCount} channels.", nameof(commands));
            }

            // How far behind its own tick the client drew the world: nought for unknown, else one more than the gap.
            writer.WriteVarUInt(command.ViewTick == long.MinValue
                ? 0u
                : (uint)Math.Clamp(command.Tick - command.ViewTick + 1, 1, MaxViewLag + 1));

            for (int c = 0; c < ChannelCount; c++)
            {
                float value = command.Channels[c];
                float before = previous?[c] ?? 0f;
                bool changed = BitConverter.SingleToUInt32Bits(value) != BitConverter.SingleToUInt32Bits(before);
                writer.WriteBool(changed);
                if (!changed)
                {
                    continue;
                }

                if (BitConverter.SingleToUInt32Bits(value) == 0)
                {
                    writer.WriteBits(TagZero, 2);
                }
                else if (value == 1f)
                {
                    writer.WriteBits(TagOne, 2);
                }
                else
                {
                    writer.WriteBits(TagExact, 2);
                    writer.WriteSingle(value);
                }
            }

            previous = command.Channels;
        }
    }

    /// <summary>Reads what <see cref="Write"/> wrote.</summary>
    /// <param name="reader">Where from.</param>
    /// <param name="commands">Where the commands go, oldest first.</param>
    /// <returns><see langword="false"/> when the bytes are malformed; nothing is added then.</returns>
    public bool Read(ref BitReader reader, List<UserCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        int count = reader.ReadRanged(1, MaxCommandsPerPacket);
        long newest = reader.ReadBits(32) | ((long)reader.ReadBits(32) << 32);
        if (reader.IsOverflowed)
        {
            return false;
        }

        UserCommand[] read = new UserCommand[count];
        float[]? previous = null;
        for (int i = 0; i < count; i++)
        {
            long tick = newest - (count - 1 - i);
            uint viewLag = reader.ReadVarUInt();
            if (viewLag > MaxViewLag + 1)
            {
                return false;
            }

            long viewTick = viewLag == 0 ? long.MinValue : tick - (viewLag - 1);
            float[] channels = previous is null ? new float[ChannelCount] : (float[])previous.Clone();
            for (int c = 0; c < ChannelCount; c++)
            {
                if (!reader.ReadBool())
                {
                    continue;
                }

                uint tag = reader.ReadBits(2);
                channels[c] = tag switch
                {
                    TagZero => 0f,
                    TagOne => 1f,
                    TagExact => reader.ReadSingle(),
                    _ => float.NaN,
                };
                if (tag > TagExact)
                {
                    return false;
                }
            }

            if (reader.IsOverflowed)
            {
                return false;
            }

            read[i] = new UserCommand(tick, channels, viewTick);
            previous = channels;
        }

        commands.AddRange(read);
        return true;
    }
}
