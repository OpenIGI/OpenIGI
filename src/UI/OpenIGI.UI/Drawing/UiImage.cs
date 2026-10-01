namespace OpenIGI.UI.Drawing;

/// <summary>An opaque reference to an image a renderer can sample.</summary>
/// <remarks>
/// <para>
/// The identifier is assigned by whichever <see cref="IUiImageFactory"/> created the image and means
/// nothing to the framework: the game's renderer stores its GPU texture handle in it, the launcher's
/// Skia renderer an index into its image table. Zero is reserved for <see cref="None"/>, which every
/// renderer draws as solid white so a vertex colour alone fills the shape.
/// </para>
/// <para>
/// A value type on purpose: a sprite draw must not allocate, and a handle wrapping an integer is the
/// cheapest thing that keeps the framework free of any backend's texture type.
/// </para>
/// </remarks>
/// <param name="Id">The renderer-assigned identifier; zero means <see cref="None"/>.</param>
public readonly record struct UiImage(uint Id)
{
    /// <summary>Gets the null image, drawn as solid white.</summary>
    public static UiImage None => default;

    /// <summary>Gets a value indicating whether this refers to an image.</summary>
    public bool IsValid => Id != 0;
}
