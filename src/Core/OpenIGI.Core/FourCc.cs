using System.Buffers.Binary;

namespace OpenIGI.Core;

/// <summary>
/// A four-character container tag as used by the ILFF, MTP and asset headers of the original game.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="FourCc"/> stores its four characters in <em>file order</em> — the order the bytes
/// appear on disk. <see cref="Value"/> exposes the same four bytes read as a little-endian
/// <see cref="uint"/>, which is how the original's dword constants read (for example the ILFF chunk id
/// <c>NAME</c> is the constant <c>0x454D414E</c>).
/// </para>
/// <para>
/// MEF chunk ids appear byte-reversed on disk relative to their logical names, because the original
/// source used MSVC multi-character literals: <c>'MESH'</c> is the constant <c>0x4D455348</c>, which
/// stored little-endian is the byte sequence <c>HSEM</c>. The raw dword therefore matches the code
/// constant while the character order is flipped; use <see cref="Reversed"/> to move between the
/// on-disk and logical spellings.
/// </para>
/// </remarks>
public readonly struct FourCc : IEquatable<FourCc>
{
    /// <summary>The number of bytes in a <see cref="FourCc"/>.</summary>
    public const int Size = 4;

    private readonly uint _value;

    /// <summary>Initializes a new instance of the <see cref="FourCc"/> struct from its little-endian dword value.</summary>
    /// <param name="value">The four bytes packed as a little-endian <see cref="uint"/>.</param>
    public FourCc(uint value) => _value = value;

    /// <summary>Initializes a new instance of the <see cref="FourCc"/> struct from four characters in file order.</summary>
    /// <param name="a">The first character.</param>
    /// <param name="b">The second character.</param>
    /// <param name="c">The third character.</param>
    /// <param name="d">The fourth character.</param>
    /// <exception cref="ArgumentOutOfRangeException">Any character is outside the single-byte range.</exception>
    public FourCc(char a, char b, char c, char d)
    {
        _value = (uint)(ToByte(a, nameof(a))
            | (ToByte(b, nameof(b)) << 8)
            | (ToByte(c, nameof(c)) << 16)
            | (ToByte(d, nameof(d)) << 24));
    }

    /// <summary>Gets the four bytes packed as a little-endian <see cref="uint"/>.</summary>
    /// <remarks>This matches the dword constants that appear in the original binary.</remarks>
    public uint Value => _value;

    /// <summary>Determines whether two tags are equal.</summary>
    /// <param name="left">The first tag.</param>
    /// <param name="right">The second tag.</param>
    /// <returns><see langword="true"/> if the tags are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(FourCc left, FourCc right) => left.Equals(right);

    /// <summary>Determines whether two tags differ.</summary>
    /// <param name="left">The first tag.</param>
    /// <param name="right">The second tag.</param>
    /// <returns><see langword="true"/> if the tags differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(FourCc left, FourCc right) => !left.Equals(right);

    /// <summary>Creates a tag from a four-character string in file order.</summary>
    /// <param name="text">The four characters, for example <c>"ILFF"</c>.</param>
    /// <returns>The parsed tag.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is not exactly four characters long.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A character is outside the single-byte range.</exception>
    public static FourCc FromString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length != Size)
        {
            throw new ArgumentException("A FourCC is exactly four characters long.", nameof(text));
        }

        return new FourCc(text[0], text[1], text[2], text[3]);
    }

    /// <summary>Reads a tag from the first four bytes of <paramref name="source"/>.</summary>
    /// <param name="source">The bytes to read, in file order.</param>
    /// <returns>The tag.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> is shorter than <see cref="Size"/>.</exception>
    public static FourCc FromBytes(ReadOnlySpan<byte> source)
    {
        if (source.Length < Size)
        {
            throw new ArgumentException("A FourCC needs four bytes.", nameof(source));
        }

        return new FourCc(BinaryPrimitives.ReadUInt32LittleEndian(source));
    }

    /// <summary>Returns this tag with its four characters in the opposite order.</summary>
    /// <returns>The byte-reversed tag.</returns>
    /// <remarks>
    /// MEF chunk ids are stored reversed on disk: <c>FourCc.FromString("HSEM").Reversed()</c> is
    /// <c>MESH</c>.
    /// </remarks>
    public FourCc Reversed() => new(BinaryPrimitives.ReverseEndianness(_value));

    /// <summary>Writes the four bytes of this tag into <paramref name="destination"/> in file order.</summary>
    /// <param name="destination">The span to write to.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <see cref="Size"/>.</exception>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException("A FourCC needs four bytes.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination, _value);
    }

    /// <inheritdoc/>
    public bool Equals(FourCc other) => _value == other._value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FourCc other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value.GetHashCode();

    /// <summary>Returns the four characters in file order.</summary>
    /// <returns>The tag as a string; non-printable bytes are rendered as <c>'.'</c>.</returns>
    public override string ToString()
    {
        Span<char> chars = stackalloc char[Size];
        for (int i = 0; i < Size; i++)
        {
            byte b = (byte)(_value >> (8 * i));
            chars[i] = b is >= 0x20 and < 0x7F ? (char)b : '.';
        }

        return new string(chars);
    }

    private static uint ToByte(char value, string parameterName)
    {
        if (value > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A FourCC character must fit in one byte.");
        }

        return value;
    }
}
