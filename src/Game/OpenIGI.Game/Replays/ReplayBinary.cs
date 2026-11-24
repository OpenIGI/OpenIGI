using System.Text;

namespace OpenIGI.Game.Replays;

/// <summary>Texts in a replay's bytes: UTF-8 with a length before them, read back no longer than the field allows.</summary>
/// <remarks>
/// A replay is read from anywhere -- a player's own folder, the server's upload -- so a length is never believed before it is
/// checked: <see cref="BinaryReader.ReadString"/> would build whatever a length says.
/// </remarks>
internal static class ReplayBinary
{
    /// <summary>Writes a text, cut to a length.</summary>
    /// <param name="writer">Where it goes.</param>
    /// <param name="text">The text.</param>
    /// <param name="maxChars">The most characters it keeps.</param>
    public static void WriteText(BinaryWriter writer, string? text, int maxChars)
    {
        string value = text ?? string.Empty;
        if (value.Length > maxChars)
        {
            value = value[..maxChars];
        }

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write7BitEncodedInt(bytes.Length);
        writer.Write(bytes);
    }

    /// <summary>Reads a text written by <see cref="WriteText"/>.</summary>
    /// <param name="reader">Where it comes from.</param>
    /// <param name="maxChars">The most characters the field holds.</param>
    /// <returns>The text.</returns>
    /// <exception cref="InvalidDataException">It is longer than the field holds, or not UTF-8.</exception>
    public static string ReadText(BinaryReader reader, int maxChars)
    {
        int length = reader.Read7BitEncodedInt();
        if (length < 0 || length > maxChars * 4)
        {
            throw new InvalidDataException($"A text of {length} bytes, where at most {maxChars} characters belong.");
        }

        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new InvalidDataException("A text runs past the end.");
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("A text is not UTF-8.", ex);
        }

        return text.Length > maxChars ? throw new InvalidDataException($"A text longer than its {maxChars} characters.") : text;
    }
}
