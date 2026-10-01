using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenIGI.Core.Graphics;

/// <summary>
/// The engine's canonical 32-bit pixel: blue, green, red and alpha, in that memory order.
/// </summary>
/// <remarks>
/// <para>
/// The original stores and manipulates pixels as the little-endian dword <c>0xAARRGGBB</c>, which is
/// the byte sequence <c>B, G, R, A</c>. Every texture converter in <c>DownloadTexture</c>
/// (<c>0x50AD90</c>) writes blue to byte 0 and red to byte 2, and the 32-bit path is a plain dword
/// copy — so a 32-bit texture is stored in the file in exactly this layout. The same layout appears
/// again in <c>BlitPrimitive</c> (<c>0x494C50</c>).
/// </para>
/// <para>
/// Decoding to this one type at load time means the swizzle question is answered once, in
/// <see cref="OpenIGI.Core.Graphics"/>, rather than in every backend.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly struct Bgra32 : IEquatable<Bgra32>
{
    /// <summary>The number of bytes one pixel occupies.</summary>
    public const int Size = 4;

    /// <summary>Initializes a new instance of the <see cref="Bgra32"/> struct.</summary>
    /// <param name="b">The blue channel.</param>
    /// <param name="g">The green channel.</param>
    /// <param name="r">The red channel.</param>
    /// <param name="a">The alpha channel.</param>
    public Bgra32(byte b, byte g, byte r, byte a)
    {
        B = b;
        G = g;
        R = r;
        A = a;
    }

    /// <summary>Gets the blue channel, byte 0.</summary>
    public byte B { get; }

    /// <summary>Gets the green channel, byte 1.</summary>
    public byte G { get; }

    /// <summary>Gets the red channel, byte 2.</summary>
    public byte R { get; }

    /// <summary>Gets the alpha channel, byte 3.</summary>
    public byte A { get; }

    /// <summary>Gets opaque black.</summary>
    public static Bgra32 Black => new(0, 0, 0, 255);

    /// <summary>Gets opaque white.</summary>
    public static Bgra32 White => new(255, 255, 255, 255);

    /// <summary>Gets fully transparent black.</summary>
    public static Bgra32 Transparent => default;

    /// <summary>Creates a pixel from red, green and blue with full alpha.</summary>
    /// <param name="r">The red channel.</param>
    /// <param name="g">The green channel.</param>
    /// <param name="b">The blue channel.</param>
    /// <returns>The opaque pixel.</returns>
    public static Bgra32 FromRgb(byte r, byte g, byte b) => new(b, g, r, 255);

    /// <summary>Creates a pixel from red, green, blue and alpha.</summary>
    /// <param name="r">The red channel.</param>
    /// <param name="g">The green channel.</param>
    /// <param name="b">The blue channel.</param>
    /// <param name="a">The alpha channel.</param>
    /// <returns>The pixel.</returns>
    public static Bgra32 FromRgba(byte r, byte g, byte b, byte a) => new(b, g, r, a);

    /// <summary>Creates a pixel from the packed little-endian dword the original uses.</summary>
    /// <param name="value">The packed value, <c>0xAARRGGBB</c>.</param>
    /// <returns>The pixel.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgra32 FromPacked(uint value) =>
        new((byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24));

    /// <summary>Determines whether two pixels are equal.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if the pixels are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Bgra32 left, Bgra32 right) => left.Equals(right);

    /// <summary>Determines whether two pixels differ.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if the pixels differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Bgra32 left, Bgra32 right) => !left.Equals(right);

    /// <summary>Gets the packed little-endian dword form, <c>0xAARRGGBB</c>.</summary>
    /// <returns>The packed value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ToPacked() => B | ((uint)G << 8) | ((uint)R << 16) | ((uint)A << 24);

    /// <summary>Returns this pixel with a different alpha channel.</summary>
    /// <param name="alpha">The new alpha channel.</param>
    /// <returns>The recoloured pixel.</returns>
    public Bgra32 WithAlpha(byte alpha) => new(B, G, R, alpha);

    /// <inheritdoc/>
    public bool Equals(Bgra32 other) => ToPacked() == other.ToPacked();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Bgra32 other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (int)ToPacked();

    /// <inheritdoc/>
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}
