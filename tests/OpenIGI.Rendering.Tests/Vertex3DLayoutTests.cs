using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins the byte layout of <see cref="Vertex3D"/>, which a graphics backend uploads by raw size and
/// reads back with hard-coded attribute offsets.
/// </summary>
/// <remarks>
/// Nothing in the type system connects <see cref="Vertex3D.Size"/> to the struct's real size, and a
/// mismatch does not fail to compile or throw — it silently garbles every mesh in the world.
/// </remarks>
public sealed class Vertex3DLayoutTests
{
    [Fact]
    public void TheDeclaredSizeIsTheRealSize()
    {
        Assert.Equal(Vertex3D.Size, Unsafe.SizeOf<Vertex3D>());
        Assert.Equal(Vertex3D.Size, Marshal.SizeOf<Vertex3D>());
    }

    [Fact]
    public void TheFieldsSitWhereTheBackendReadsThem()
    {
        Vertex3D[] one =
        [
            new(
                new Vector3f(1f, 2f, 3f),
                new Vector3f(4f, 5f, 6f),
                7f,
                8f,
                9f,
                10f,
                new Bgra32(11, 12, 13, 14)),
        ];

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes<Vertex3D>(one);
        Assert.Equal(Vertex3D.Size, bytes.Length);

        ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(bytes[..40]);
        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f], floats.ToArray());

        // Colour is last, in the engine's own byte order, which is what the shader swizzles back.
        Assert.Equal(Vertex3D.ColourOffset, 40);
        Assert.Equal<byte[]>([11, 12, 13, 14], bytes[40..].ToArray());
    }
}
