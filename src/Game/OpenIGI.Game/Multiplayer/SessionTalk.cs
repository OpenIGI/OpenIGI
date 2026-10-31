using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>A line of chat, and who said it, by entity.</summary>
/// <param name="Entity">The speaker's entity.</param>
/// <param name="Text">What was said.</param>
public readonly record struct ChatLine(int Entity, string Text);

/// <summary>A place one player marked for the others, and who marked it, by entity.</summary>
/// <param name="Entity">The marker's entity.</param>
/// <param name="Position">Where, in world units.</param>
public readonly record struct WorldMark(int Entity, Int3 Position);

/// <summary>A moment of one player's voice, encoded, and whose it is, by entity.</summary>
/// <param name="Entity">The speaker's entity.</param>
/// <param name="Packet">The encoded voice.</param>
public sealed record VoicePacket(int Entity, byte[] Packet);

/// <summary>How chat and marks are written to a session's messages, either way.</summary>
public static class SessionTalk
{
    /// <summary>The longest line said, in characters; a longer one is cut.</summary>
    public const int MaxChatLength = 120;

    /// <summary>Writes a line of chat.</summary>
    /// <param name="writer">Where to.</param>
    /// <param name="entity">The speaker, or -1 from a client, which the server fills in.</param>
    /// <param name="text">What was said.</param>
    public static void WriteChat(BitWriter writer, int entity, string text)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBits((uint)SessionMessage.Chat, 8);
        writer.WriteVarInt(entity);
        writer.WriteString(Trim(text), MaxChatLength);
    }

    /// <summary>Writes a mark.</summary>
    /// <param name="writer">Where to.</param>
    /// <param name="entity">The marker, or -1 from a client, which the server fills in.</param>
    /// <param name="position">Where, in world units.</param>
    public static void WriteMark(BitWriter writer, int entity, Int3 position)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBits((uint)SessionMessage.Mark, 8);
        writer.WriteVarInt(entity);
        writer.WriteVarInt(position.X);
        writer.WriteVarInt(position.Y);
        writer.WriteVarInt(position.Z);
    }

    /// <summary>The longest moment of voice sent, in bytes; a longer one is not sent.</summary>
    public const int MaxVoiceBytes = 400;

    /// <summary>Writes a moment of voice.</summary>
    /// <param name="writer">Where to.</param>
    /// <param name="entity">The speaker, or -1 from a client, which the server fills in.</param>
    /// <param name="packet">The encoded voice.</param>
    public static void WriteVoice(BitWriter writer, int entity, ReadOnlySpan<byte> packet)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBits((uint)SessionMessage.Voice, 8);
        writer.WriteVarInt(entity);
        writer.WriteVarUInt((uint)packet.Length);
        foreach (byte b in packet)
        {
            writer.WriteBits(b, 8);
        }
    }

    /// <summary>Reads the rest of a moment of voice, after its kind.</summary>
    /// <param name="reader">The message.</param>
    /// <returns>The voice, or <see langword="null"/> when it is malformed.</returns>
    public static VoicePacket? ReadVoice(ref BitReader reader)
    {
        int entity = reader.ReadVarInt();
        uint length = reader.ReadVarUInt();
        if (reader.IsOverflowed || length == 0 || length > MaxVoiceBytes)
        {
            return null;
        }

        byte[] packet = new byte[length];
        for (int i = 0; i < packet.Length; i++)
        {
            packet[i] = (byte)reader.ReadBits(8);
        }

        return reader.IsOverflowed ? null : new VoicePacket(entity, packet);
    }

    /// <summary>Reads the rest of a line of chat, after its kind.</summary>
    /// <param name="reader">The message.</param>
    /// <returns>The line, or <see langword="null"/> when it is malformed or empty.</returns>
    public static ChatLine? ReadChat(ref BitReader reader)
    {
        int entity = reader.ReadVarInt();
        string text = Trim(reader.ReadString(MaxChatLength));
        return reader.IsOverflowed || text.Length == 0 ? null : new ChatLine(entity, text);
    }

    /// <summary>Reads the rest of a mark, after its kind.</summary>
    /// <param name="reader">The message.</param>
    /// <returns>The mark, or <see langword="null"/> when it is malformed.</returns>
    public static WorldMark? ReadMark(ref BitReader reader)
    {
        int entity = reader.ReadVarInt();
        Int3 position = new(reader.ReadVarInt(), reader.ReadVarInt(), reader.ReadVarInt());
        return reader.IsOverflowed ? null : new WorldMark(entity, position);
    }

    // What a line can be: no control characters, no longer than the limit, no space either end.
    private static string Trim(string? text)
    {
        string clean = new([.. (text ?? string.Empty).Where(static c => !char.IsControl(c))]);
        clean = clean.Trim();
        return clean.Length > MaxChatLength ? clean[..MaxChatLength] : clean;
    }
}
