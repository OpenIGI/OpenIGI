using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>One fragment an explosion throws, <c>Fragment</c>: a point that flies, turning, for the container's lifetime.</summary>
public sealed class Igi2Fragment
{
    /// <summary>Gets or sets where it is.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its velocity, units a tick.</summary>
    public Vector3f Velocity { get; set; }

    /// <summary>Gets its orientation, nine floats row by row.</summary>
    public float[] Orientation { get; init; } = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <summary>Gets its scale, the fragment's radius over 2048.</summary>
    public float Scale { get; init; }

    /// <summary>Gets or sets the ticks it has left, <c>nLifetime</c>.</summary>
    public int Lifetime { get; set; }
}
