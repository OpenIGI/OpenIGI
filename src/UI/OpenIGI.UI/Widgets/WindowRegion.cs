namespace OpenIGI.UI.Widgets;

/// <summary>
/// The part of a floating window a pointer is over, which decides what a press there begins.
/// </summary>
/// <remarks>
/// <para>
/// Combinable, because a corner is genuinely two edges at once: the bottom-right grip is
/// <c>Right | Bottom</c> and resizes both axes from one drag. Expressing it that way means the resize
/// arithmetic is written once per axis instead of once per handle, and the four corners cost nothing
/// extra over the four edges.
/// </para>
/// <para>
/// Named <c>WindowRegion</c> rather than anything ending in <c>Flags</c>, which the analyzers forbid.
/// </para>
/// </remarks>
[Flags]
public enum WindowRegion
{
    /// <summary>Not over the window at all.</summary>
    None = 0,

    /// <summary>Over the window's content.</summary>
    Client = 1 << 0,

    /// <summary>Over the title bar, which drags the whole window.</summary>
    Title = 1 << 1,

    /// <summary>Over the left edge.</summary>
    Left = 1 << 2,

    /// <summary>Over the right edge.</summary>
    Right = 1 << 3,

    /// <summary>Over the top edge.</summary>
    Top = 1 << 4,

    /// <summary>Over the bottom edge.</summary>
    Bottom = 1 << 5,

    /// <summary>Over any edge or corner, and therefore resizing.</summary>
    AnyEdge = Left | Right | Top | Bottom,
}
