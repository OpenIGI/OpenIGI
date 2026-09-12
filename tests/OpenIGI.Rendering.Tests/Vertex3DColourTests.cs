using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// A vertex keeps the colour it was given, including the one that is all zeroes.
/// </summary>
/// <remarks>
/// Transparent black is the zero value of <see cref="Bgra32"/>, so a constructor that reads "no colour
/// was passed" off a zero colour cannot tell the two apart — and it used to answer opaque white. Every
/// caller that fades a dark thing out hit that: the sky's cloud layers drew the one row of their strip
/// the fade had reached zero on as a bright opaque quad, which in the two missions whose layers are
/// authored black was a white box in the sky.
/// </remarks>
public sealed class Vertex3DColourTests
{
    /// <summary>A colour faded all the way out stays faded all the way out.</summary>
    [Fact]
    public void AColourFadedToNothingIsKept()
    {
        Vertex3D vertex = new(Vector3f.One, Vector3f.Zero, 0f, 0f, 0f, 0f, new Bgra32(0, 0, 0, 0));

        Assert.Equal(0, vertex.Colour.A);
        Assert.Equal(0, vertex.Colour.R);
        Assert.Equal(0, vertex.Colour.G);
        Assert.Equal(0, vertex.Colour.B);
    }

    /// <summary>Only a vertex that asks for no colour at all gets white.</summary>
    /// <remarks>Which is what a model vertex does: its texture is meant to come through unmodulated.</remarks>
    [Fact]
    public void AVertexWithNoColourIsWhite()
    {
        Assert.Equal(Bgra32.White, new Vertex3D(Vector3f.One, Vector3f.Zero, 0f, 0f).Colour);
        Assert.Equal(Bgra32.White, new Vertex3D(Vector3f.One, Vector3f.Zero, 0f, 0f, 0f, 0f, default).Colour);
    }

    /// <summary>A colour that is only transparent, or only black, is kept as asked.</summary>
    [Theory]
    [InlineData(255, 255, 255, 0)]
    [InlineData(0, 0, 0, 255)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(1, 0, 0, 0)]
    public void EveryOtherColourIsKeptExactly(byte b, byte g, byte r, byte a)
    {
        Bgra32 colour = new(b, g, r, a);

        Assert.Equal(colour, new Vertex3D(Vector3f.One, Vector3f.Zero, 0f, 0f, 0f, 0f, colour).Colour);
    }
}
