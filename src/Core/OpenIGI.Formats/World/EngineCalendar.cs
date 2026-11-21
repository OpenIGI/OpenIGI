using System.Buffers.Binary;
using System.Globalization;

namespace OpenIGI.Formats.World;

/// <summary>A date and time as the engine stamps its files: <c>QCalendar_s</c>, seven little-endian ints.</summary>
/// <remarks>
/// A mesh carries the moment it was exported at <c>Mesh3D_Mesh_t.tTime</c>, and a baked lightmap the moment it
/// was baked; the loaders compare the two to decide whether a bake is still of the mesh it lights.
/// </remarks>
/// <param name="Year">The year.</param>
/// <param name="Month">The month, from one.</param>
/// <param name="Day">The day of the month.</param>
/// <param name="Hour">The hour.</param>
/// <param name="Minute">The minute.</param>
/// <param name="Second">The second.</param>
/// <param name="Milliseconds">The millisecond.</param>
public readonly record struct EngineCalendar(int Year, int Month, int Day, int Hour, int Minute, int Second, int Milliseconds)
    : IComparable<EngineCalendar>
{
    /// <summary>The size in bytes of a stamp.</summary>
    public const int Size = 28;

    /// <summary>Reads a stamp.</summary>
    /// <param name="span">The bytes, starting at the stamp.</param>
    /// <returns>The stamp.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="span"/> is shorter than <see cref="Size"/>.</exception>
    public static EngineCalendar Read(ReadOnlySpan<byte> span)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(span.Length, Size);

        return new EngineCalendar(
            BinaryPrimitives.ReadInt32LittleEndian(span),
            BinaryPrimitives.ReadInt32LittleEndian(span[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(span[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(span[12..]),
            BinaryPrimitives.ReadInt32LittleEndian(span[16..]),
            BinaryPrimitives.ReadInt32LittleEndian(span[20..]),
            BinaryPrimitives.ReadInt32LittleEndian(span[24..]));
    }

    /// <summary>Orders two stamps as <c>QCalendar_Compare</c> does.</summary>
    /// <param name="other">The stamp to compare with.</param>
    /// <returns>Negative when this stamp is the earlier, zero when they are the same, positive when it is the later.</returns>
    /// <remarks>
    /// The original prints each stamp as <c>%04d-%02d-%02d-%02d-%02d-%02d-%04d</c> and compares the strings, so
    /// this does too: for a field out of its printed width the string order is not the numeric one, and the
    /// string order is the one that decides.
    /// </remarks>
    public int CompareTo(EngineCalendar other) => string.CompareOrdinal(Print(), other.Print());

    /// <summary>Whether <paramref name="left"/> is the earlier stamp, as <see cref="CompareTo"/> orders them.</summary>
    /// <param name="left">The first stamp.</param>
    /// <param name="right">The second stamp.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> orders first.</returns>
    public static bool operator <(EngineCalendar left, EngineCalendar right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> is the later stamp, as <see cref="CompareTo"/> orders them.</summary>
    /// <param name="left">The first stamp.</param>
    /// <param name="right">The second stamp.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> orders last.</returns>
    public static bool operator >(EngineCalendar left, EngineCalendar right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> orders no later than <paramref name="right"/>.</summary>
    /// <param name="left">The first stamp.</param>
    /// <param name="right">The second stamp.</param>
    /// <returns><see langword="true"/> unless <paramref name="left"/> orders last.</returns>
    public static bool operator <=(EngineCalendar left, EngineCalendar right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> orders no earlier than <paramref name="right"/>.</summary>
    /// <param name="left">The first stamp.</param>
    /// <param name="right">The second stamp.</param>
    /// <returns><see langword="true"/> unless <paramref name="left"/> orders first.</returns>
    public static bool operator >=(EngineCalendar left, EngineCalendar right) => left.CompareTo(right) >= 0;

    private string Print() =>
        $"{Field(Year, 4)}-{Field(Month, 2)}-{Field(Day, 2)}-{Field(Hour, 2)}-{Field(Minute, 2)}-{Field(Second, 2)}-{Field(Milliseconds, 4)}";

    // C's %0Nd: the width counts the sign, and the zeros go between the sign and the digits.
    private static string Field(int value, int width) =>
        value < 0
            ? "-" + (-(long)value).ToString(CultureInfo.InvariantCulture).PadLeft(width - 1, '0')
            : value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
}
