namespace OpenIGI.UI.Layout;

/// <summary>
/// A per-edge inset — padding inside a container, margin around an element, or a safe-area border.
/// </summary>
/// <param name="Left">The left inset.</param>
/// <param name="Top">The top inset.</param>
/// <param name="Right">The right inset.</param>
/// <param name="Bottom">The bottom inset.</param>
public readonly record struct Thickness(float Left, float Top, float Right, float Bottom)
{
    /// <summary>Initializes a uniform thickness.</summary>
    /// <param name="all">The inset applied to every edge.</param>
    public Thickness(float all)
        : this(all, all, all, all)
    {
    }

    /// <summary>Initializes a symmetric thickness.</summary>
    /// <param name="horizontal">The left and right inset.</param>
    /// <param name="vertical">The top and bottom inset.</param>
    public Thickness(float horizontal, float vertical)
        : this(horizontal, vertical, horizontal, vertical)
    {
    }

    /// <summary>Gets the zero thickness.</summary>
    public static Thickness Zero => default;

    /// <summary>Gets the total horizontal inset, left plus right.</summary>
    public float Horizontal => Left + Right;

    /// <summary>Gets the total vertical inset, top plus bottom.</summary>
    public float Vertical => Top + Bottom;

    /// <summary>Returns this thickness with every edge scaled.</summary>
    /// <param name="factor">The scale factor.</param>
    /// <returns>The scaled thickness.</returns>
    public Thickness Scale(float factor) =>
        new(Left * factor, Top * factor, Right * factor, Bottom * factor);
}
