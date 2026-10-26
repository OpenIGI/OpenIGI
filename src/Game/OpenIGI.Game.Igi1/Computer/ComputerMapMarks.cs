using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi1.Computer;

/// <summary>What one of the map's marks is.</summary>
public enum ComputerMarkKind
{
    /// <summary>An outline of one of the marker shapes, turned to the object's heading (<c>0x467F30</c>).</summary>
    Marker,

    /// <summary>A sprite of the computer archive (<c>0x467DF0</c>, <c>0x442C60</c>).</summary>
    Sprite,

    /// <summary>A label (<c>0x4680B0</c>).</summary>
    Label,
}

/// <summary>One mark the map puts over its picture, in the order they are drawn.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Position">Where it is in the world, world units.</param>
public readonly record struct ComputerMark(ComputerMarkKind Kind, Vector3d Position)
{
    /// <summary>Gets a marker's shape, an index of <see cref="ComputerMarkShapes.Shapes"/>.</summary>
    public int Shape { get; init; }

    /// <summary>Gets a marker's colour, an index of <see cref="ComputerColors.Markers"/>.</summary>
    public int Color { get; init; }

    /// <summary>Gets a marker's heading.</summary>
    public float Yaw { get; init; }

    /// <summary>Gets a marker's size, world units: the object's radius, never under 3072.</summary>
    public float Size { get; init; }

    /// <summary>
    /// Gets a value indicating whether the mark is dropped when it lies outside the view it is drawn in, as a soldier's is
    /// (<c>0x4D03D0</c> under the current view).
    /// </summary>
    public bool Culled { get; init; }

    /// <summary>Gets a sprite's name in the computer archive, or a label's text as it is shown.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Gets a sprite's frame.</summary>
    public int Frame { get; init; }

    /// <summary>Gets a value indicating whether a sprite is moved a further ten pixels left, as an object's sprite is but a lens's is not.</summary>
    public bool Nudged { get; init; }
}

/// <summary>The marker shapes, outlines in their own units (<c>0x53F5F0</c>).</summary>
public static class ComputerMarkShapes
{
    /// <summary>A box, a triangle and a big triangle (<c>MARKER_BOX</c> is the first).</summary>
    public static readonly (float X, float Y)[][] Shapes =
    [
        [(-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f), (-1f, -1f)],
        [(0f, -2.5f), (1.5f, 1f), (-1.5f, 1f), (0f, -2.5f)],
        [(0f, -7.5f), (4.5f, 3f), (-4.5f, 3f), (0f, -7.5f)],
    ];

    /// <summary>The smallest a marker is drawn, world units.</summary>
    public const float MinimumSize = 3072f;
}

/// <summary>A selected camera's view, the fan the map draws over the ground it watches (<c>0x442DD0</c>).</summary>
/// <param name="Origin">The lens, world units.</param>
/// <param name="Ends">Where each of its twelve rays stops.</param>
public readonly record struct ComputerViewCone(Vector3d Origin, Vector3d[] Ends)
{
    /// <summary>The rays.</summary>
    public const int Rays = 12;

    /// <summary>The fan's colour and opacity.</summary>
    public static readonly (float R, float G, float B, float A) Fill = (1f, 0f, 0f, 0.35f);
}
