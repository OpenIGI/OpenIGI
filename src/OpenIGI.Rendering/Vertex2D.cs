using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering;

/// <summary>
/// A screen-space vertex: a pixel position, a texture coordinate and a colour.
/// </summary>
/// <remarks>
/// Positions are in <em>pixels</em> of the current 2D projection, with the origin at the top-left and
/// Y growing downwards — the coordinate system the original's UI code works in. The device is
/// responsible for turning that into whatever its clip space is.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex2D
{
    /// <summary>The size of one vertex in bytes.</summary>
    public const int Size = 20;

    /// <summary>Initializes a new instance of the <see cref="Vertex2D"/> struct.</summary>
    /// <param name="x">The horizontal pixel position.</param>
    /// <param name="y">The vertical pixel position.</param>
    /// <param name="u">The horizontal texture coordinate.</param>
    /// <param name="v">The vertical texture coordinate.</param>
    /// <param name="color">The vertex colour, modulated with the texture sample.</param>
    public Vertex2D(float x, float y, float u, float v, Bgra32 color)
    {
        X = x;
        Y = y;
        U = u;
        V = v;
        Color = color;
    }

    /// <summary>Gets the horizontal pixel position.</summary>
    public float X { get; }

    /// <summary>Gets the vertical pixel position.</summary>
    public float Y { get; }

    /// <summary>Gets the horizontal texture coordinate.</summary>
    public float U { get; }

    /// <summary>Gets the vertical texture coordinate.</summary>
    public float V { get; }

    /// <summary>Gets the vertex colour, modulated with the texture sample.</summary>
    public Bgra32 Color { get; }
}
