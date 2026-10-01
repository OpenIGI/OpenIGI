namespace OpenIGI.UI.Layout;

/// <summary>
/// A two-dimensional point or size in the interface's coordinate space.
/// </summary>
/// <remarks>
/// The interface works entirely in <see cref="float"/> pixels with the origin at the top-left and Y
/// growing downwards, the same convention every renderer's 2D path uses, so a laid-out rectangle can be
/// handed to a renderer without a flip. A dedicated two-component type is worth
/// its keep because <c>OpenIGI.Core.Numerics</c> has only three- and four-component vectors — layout
/// never needs a Z.
/// </remarks>
/// <param name="X">The horizontal component.</param>
/// <param name="Y">The vertical component.</param>
public readonly record struct UiVec2(float X, float Y)
{
    /// <summary>Gets the zero vector.</summary>
    public static UiVec2 Zero => default;

    /// <summary>Gets the vector whose components are both one.</summary>
    public static UiVec2 One => new(1f, 1f);

    /// <summary>Adds two vectors componentwise.</summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The sum.</returns>
    public static UiVec2 operator +(UiVec2 a, UiVec2 b) => new(a.X + b.X, a.Y + b.Y);

    /// <summary>Subtracts one vector from another componentwise.</summary>
    /// <param name="a">The vector to subtract from.</param>
    /// <param name="b">The vector to subtract.</param>
    /// <returns>The difference.</returns>
    public static UiVec2 operator -(UiVec2 a, UiVec2 b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>Scales a vector by a scalar.</summary>
    /// <param name="v">The vector.</param>
    /// <param name="s">The scalar.</param>
    /// <returns>The scaled vector.</returns>
    public static UiVec2 operator *(UiVec2 v, float s) => new(v.X * s, v.Y * s);
}
