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
                new Bgra32(11, 12, 13, 14),
                new Bgra32(15, 16, 17, 18)),
        ];

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes<Vertex3D>(one);
        Assert.Equal(Vertex3D.Size, bytes.Length);

        ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(bytes[..40]);
        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f], floats.ToArray());

        // Colour, in the engine's own byte order, which is what the shader swizzles back.
        Assert.Equal(40, Vertex3D.ColourOffset);
        Assert.Equal<byte[]>([11, 12, 13, 14], bytes[40..44].ToArray());

        // The surface normal a mesh carries when its normal channel is spoken for.
        Assert.Equal(44, Vertex3D.SurfaceNormalOffset);
        Assert.Equal<byte[]>([15, 16, 17, 18], bytes[44..].ToArray());
    }

    [Fact]
    public void AVertexThatCarriesNoSurfaceNormalSaysSo()
    {
        Vertex3D bare = new(new Vector3f(0f, 0f, 0f), new Vector3f(0f, 0f, 1f), 0f, 0f);

        // All zero, including the fourth channel that marks one as present. Every mesh built before this
        // channel existed goes on saying nothing, and the shader falls back to the normal channel.
        Assert.Equal(default, bare.SurfaceNormal);
    }

    [Fact]
    public void APackedSurfaceNormalRoundTripsThroughTheShadersDecode()
    {
        Vector3f[] directions =
        [
            new(0f, 0f, 1f),
            new(1f, 0f, 0f),
            new(0f, -1f, 0f),
            new(0.577f, 0.577f, 0.577f),
        ];

        foreach (Vector3f direction in directions)
        {
            Bgra32 packed = Vertex3D.PackSurfaceNormal(direction);

            // Present, and decoded the way the vertex stage does it: the bytes in memory order, each
            // mapped from zero-to-one back to minus-one-to-one.
            Assert.Equal(255, packed.A);
            float x = (packed.B / 255f * 2f) - 1f;
            float y = (packed.G / 255f * 2f) - 1f;
            float z = (packed.R / 255f * 2f) - 1f;

            Assert.Equal(direction.X, x, 0.01);
            Assert.Equal(direction.Y, y, 0.01);
            Assert.Equal(direction.Z, z, 0.01);
        }
    }

    [Fact]
    public void ADegenerateSurfaceNormalIsTreatedAsAbsent()
    {
        Assert.Equal(default, Vertex3D.PackSurfaceNormal(new Vector3f(0f, 0f, 0f)));
    }
}
