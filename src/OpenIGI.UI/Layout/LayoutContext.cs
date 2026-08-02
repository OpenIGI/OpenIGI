namespace OpenIGI.UI.Layout;

/// <summary>
/// The mapping from the interface's authored coordinate space to the back buffer it is drawn into.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a reference resolution.</b> Every screen is authored once against a 16:9 reference (1920×1080
/// by default): a button is "220 units wide", a title is "64 units tall", spacing is "24 units". Those
/// numbers never change. What changes per display is <see cref="Scale"/>, the number of back-buffer
/// pixels one authored unit becomes. This is the same idea as the retail UI's whole-number
/// <see cref="OpenIGI.Core.Graphics.UiScale"/>, generalised to a continuous factor because the modern
/// shell is vector-and-SDF rather than pixel art and does not need to snap to integers.
/// </para>
/// <para>
/// <b>Why it is correct from 4:3 to 32:9.</b> Two things vary independently. Authored <em>sizes</em>
/// (fonts, paddings, widget extents) are multiplied by <see cref="Scale"/>, so a button keeps the same
/// visual proportion of the screen at every aspect ratio. Authored <em>positions</em> are resolved by
/// anchoring against <see cref="SafeRect"/> — the whole usable viewport — so a full-bleed background
/// covers a 32:9 monitor and an edge-pinned HUD element stays pinned, while a centred menu column stays
/// centred. The aspect ratio only ever changes how much empty margin surrounds the centred content; it
/// never clips it or moves it off a safe edge. <see cref="Match"/> tunes whether <see cref="Scale"/>
/// follows the display's width or its height as the aspect changes; height-priority (the default) keeps
/// a vertically-listed menu filling the screen top-to-bottom, which is what a cinematic shell wants.
/// </para>
/// <para>
/// <b>DPI.</b> Rendering to a physical-pixel back buffer already makes the reference scaling
/// resolution-independent; <see cref="UserScale"/> is the extra multiplier an operating system's DPI
/// setting or an accessibility "larger UI" option feeds in on top.
/// </para>
/// </remarks>
public sealed class LayoutContext
{
    /// <summary>The default authored reference width, a 16:9 canvas.</summary>
    public const float DefaultReferenceWidth = 1920f;

    /// <summary>The default authored reference height, a 16:9 canvas.</summary>
    public const float DefaultReferenceHeight = 1080f;

    /// <summary>Initializes a new instance of the <see cref="LayoutContext"/> class.</summary>
    /// <param name="viewportWidth">The back buffer's width in pixels.</param>
    /// <param name="viewportHeight">The back buffer's height in pixels.</param>
    /// <param name="reference">The authored reference size, or <see langword="null"/> for 1920×1080.</param>
    /// <param name="safeArea">Insets kept clear of the screen edges, in back-buffer pixels.</param>
    /// <param name="match">
    /// How <see cref="Scale"/> follows the display between its width (0) and its height (1) as the aspect
    /// changes. Defaults to 1, height-priority.
    /// </param>
    /// <param name="userScale">An additional multiplier on <see cref="Scale"/> for DPI or accessibility.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension or the user scale is not positive.</exception>
    public LayoutContext(
        float viewportWidth,
        float viewportHeight,
        UiVec2? reference = null,
        Thickness safeArea = default,
        float match = 1f,
        float userScale = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userScale);

        Viewport = new UiVec2(viewportWidth, viewportHeight);
        Reference = reference ?? new UiVec2(DefaultReferenceWidth, DefaultReferenceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Reference.X);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Reference.Y);

        SafeArea = safeArea;
        Match = Math.Clamp(match, 0f, 1f);
        UserScale = userScale;

        SafeRect = new UiRect(0f, 0f, viewportWidth, viewportHeight).Deflate(safeArea);

        // The log-space blend between the width ratio and the height ratio is Unity's CanvasScaler rule,
        // and it is the right one: a linear blend of the two ratios biases towards the larger, while the
        // geometric blend keeps a half-and-half match visually centred between the two. Guard the safe
        // rect being degenerate (a window dragged to nothing) so Scale never becomes zero or NaN.
        float safeW = MathF.Max(1f, SafeRect.Width);
        float safeH = MathF.Max(1f, SafeRect.Height);
        float logW = MathF.Log2(safeW / Reference.X);
        float logH = MathF.Log2(safeH / Reference.Y);
        Scale = UserScale * MathF.Pow(2f, (logW * (1f - Match)) + (logH * Match));

        float designW = Reference.X * Scale;
        float designH = Reference.Y * Scale;
        DesignRect = new UiRect(
            SafeRect.X + ((SafeRect.Width - designW) * 0.5f),
            SafeRect.Y + ((SafeRect.Height - designH) * 0.5f),
            designW,
            designH);
    }

    /// <summary>Gets the back-buffer size in pixels.</summary>
    public UiVec2 Viewport { get; }

    /// <summary>Gets the authored reference size in design units.</summary>
    public UiVec2 Reference { get; }

    /// <summary>Gets the insets kept clear of the screen edges, in back-buffer pixels.</summary>
    public Thickness SafeArea { get; }

    /// <summary>Gets the width/height blend that drives <see cref="Scale"/>, 0 for width, 1 for height.</summary>
    public float Match { get; }

    /// <summary>Gets the DPI or accessibility multiplier applied on top of the reference scaling.</summary>
    public float UserScale { get; }

    /// <summary>Gets the back-buffer pixels one authored design unit becomes.</summary>
    public float Scale { get; }

    /// <summary>Gets the usable viewport, the back buffer minus the safe-area insets, in pixels.</summary>
    /// <remarks>Positions anchor against this so full-bleed content covers an ultrawide display.</remarks>
    public UiRect SafeRect { get; }

    /// <summary>Gets the reference box scaled and centred within <see cref="SafeRect"/>, in pixels.</summary>
    /// <remarks>Content that should stay a fixed proportion of a 16:9 frame is laid out relative to this.</remarks>
    public UiRect DesignRect { get; }

    /// <summary>Converts a length in design units to back-buffer pixels.</summary>
    /// <param name="designUnits">The length in authored units.</param>
    /// <returns>The length in pixels.</returns>
    public float Px(float designUnits) => designUnits * Scale;

    /// <summary>Converts a thickness in design units to back-buffer pixels.</summary>
    /// <param name="designUnits">The inset in authored units.</param>
    /// <returns>The inset in pixels.</returns>
    public Thickness Px(Thickness designUnits) => designUnits.Scale(Scale);
}
