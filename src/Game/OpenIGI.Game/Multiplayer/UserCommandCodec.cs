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

    // The furthest from the packet's base a command's view tick is carried, either way; a further one is carried as this.
    private const int MaxViewReach = 1 << 20;

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
    /// <param name="viewBase">The server tick the view ticks are counted from: the newest snapshot the client holds.</param>
    /// <exception cref="ArgumentException">The commands are not consecutive, too many, or of the wrong length.</exception>
    public void Write(BitWriter writer, ReadOnlySpan<UserCommand> commands, long viewBase = 0)
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

            // The tick the client drew the world at, from the packet's base -- the newest snapshot it holds, a tick of the
            // server's as the view is. Never from the command's own tick: the commands are numbered on from the first, one
            // for each tick the client ran, and a client that ran fewer than the server (a hitch its clock gave up catching
            // up on) has them fall behind the server's ticks for good, so a view counted from them was read as older than
            // what the player saw, and its targets put back too far.
            writer.WriteBool(command.ViewTick != long.MinValue);
            if (command.ViewTick != long.MinValue)
            {
                writer.WriteVarInt((int)Math.Clamp(command.ViewTick - viewBase, -MaxViewReach, MaxViewReach));
                // The fraction of a tick past it, in 256ths: a sixth of a centimetre at a run.
                writer.WriteBits((uint)Math.Clamp((int)MathF.Round(command.ViewFraction * FractionSteps), 0, FractionSteps - 1), 8);
            }

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

    // The steps a view tick's fraction is sent in.
    private const int FractionSteps = 256;

    /// <summary>Reads what <see cref="Write"/> wrote.</summary>
    /// <param name="reader">Where from.</param>
    /// <param name="commands">Where the commands go, oldest first.</param>
    /// <param name="viewBase">The tick the writer counted the view ticks from.</param>
    /// <returns><see langword="false"/> when the bytes are malformed; nothing is added then.</returns>
    public bool Read(ref BitReader reader, List<UserCommand> commands, long viewBase = 0)
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
            bool viewed = reader.ReadBool();
            int viewOffset = viewed ? reader.ReadVarInt() : 0;
            if (viewOffset is < -MaxViewReach or > MaxViewReach)
            {
                return false;
            }

            long viewTick = viewed ? viewBase + viewOffset : long.MinValue;
            float viewFraction = viewed ? reader.ReadBits(8) / (float)FractionSteps : 0f;
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

            read[i] = new UserCommand(tick, channels, viewTick, viewFraction);
            previous = channels;
        }

        commands.AddRange(read);
        return true;
    }
}
