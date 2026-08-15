using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// A scrolling list of uniform-height rows that measures, arranges and draws only the rows actually on
/// screen.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <see cref="ScrollView"/>.</b> That viewport measures and arranges a single child at
/// unbounded height. Giving it a stack of four thousand labels would measure and arrange four thousand
/// elements every frame to show twenty of them — in a tool whose whole purpose is to report the frame
/// budget it was quietly eating.
/// </para>
/// <para>
/// The trade is that rows are not elements. A row draws through a callback, so it has no identity, no
/// focus and no animation of its own. For a log or a variable table that is the right trade: the rows
/// are data, they are numerous, and they are replaced wholesale. Anything that needs per-row state
/// belongs in a real widget in a <see cref="ScrollView"/>.
/// </para>
/// <para>
/// The scroll offset is carried by a <see cref="Spring"/>, so a page jump glides in the same way the
/// rest of the interface does, while the row range is computed from the settled target rather than the
/// animated value — otherwise rows would pop in and out during the glide.
/// </para>
/// </remarks>
public class VirtualListView : UiElement
{
    private readonly Spring _scroll = new(0f, smoothTime: 0.12f);
    private readonly UiTheme _theme;

    /// <summary>Initializes a new instance of the <see cref="VirtualListView"/> class.</summary>
    /// <param name="theme">The theme the scrollbar draws from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public VirtualListView(UiTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
    }

    /// <summary>Gets or sets how many rows the list holds.</summary>
    public int RowCount { get; set; }

    /// <summary>Gets or sets a row's height in pixels.</summary>
    public float RowHeight { get; set; } = 18f;

    /// <summary>Gets or sets the callback that draws one row into a rectangle.</summary>
    /// <remarks>
    /// Called only for rows inside the viewport. The rectangle is already in back-buffer pixels and the
    /// painter is already clipped to the viewport, so a row implementation needs to know nothing about
    /// scrolling.
    /// </remarks>
    public Action<UiPainter, int, UiRect>? DrawRow { get; set; }

    /// <summary>Gets or sets a value indicating whether the scrollbar is drawn when the content overflows.</summary>
    public bool ShowScrollBar { get; set; } = true;

    /// <summary>Gets or sets whether content shorter than the viewport sits at its bottom.</summary>
    /// <remarks>
    /// What a console wants and a table does not. With a few lines in a tall window, top-aligned text
    /// leaves a band of nothing between the newest line and the prompt, and the reader's eye has to
    /// travel back up to find where the output ended. Growing upwards from the prompt keeps the newest
    /// line where the next one is about to appear.
    /// </remarks>
    public bool AnchorToBottom { get; set; }

    /// <summary>Gets or sets whether the list sticks to the end as rows are appended.</summary>
    /// <remarks>
    /// What a log wants: new output should arrive in view, unless the reader has scrolled up to read
    /// something, in which case moving the view out from under them is the rudest thing the list could
    /// do. Callers turn this off the moment the reader scrolls back.
    /// </remarks>
    public bool StickToEnd { get; set; } = true;

    /// <summary>Gets the total height every row would occupy, in pixels.</summary>
    public float ContentHeight => RowCount * RowHeight;

    /// <summary>Gets the largest scroll offset, in pixels.</summary>
    public float MaxScroll => MathF.Max(0f, ContentHeight - Bounds.Height);

    /// <summary>Gets or sets the scroll offset in pixels, clamped to the content.</summary>
    public float ScrollOffset
    {
        get => _scroll.Target;
        set => _scroll.Target = Math.Clamp(value, 0f, MaxScroll);
    }

    /// <summary>Gets the index of the first row inside the viewport.</summary>
    public int FirstVisibleRow => RowHeight <= 0f ? 0 : Math.Clamp((int)(ScrollOffset / RowHeight), 0, Math.Max(0, RowCount - 1));

    /// <summary>Gets how many rows the viewport can show at once.</summary>
    public int VisibleRowCount => RowHeight <= 0f ? 0 : (int)MathF.Ceiling(Bounds.Height / RowHeight) + 1;

    /// <summary>Scrolls by a number of pixels.</summary>
    /// <param name="deltaPixels">How far to scroll; positive moves towards the end.</param>
    public void ScrollBy(float deltaPixels) => ScrollOffset += deltaPixels;

    /// <summary>Scrolls by a number of rows.</summary>
    /// <param name="rows">How many rows to move; positive moves towards the end.</param>
    public void ScrollByRows(float rows) => ScrollOffset += rows * RowHeight;

    /// <summary>Scrolls to the end.</summary>
    public void ScrollToEnd() => ScrollOffset = MaxScroll;

    /// <summary>Puts the view at the end immediately, with no glide.</summary>
    public void SnapToEnd() => SnapTo(MaxScroll);

    /// <summary>Jumps to an offset with no glide.</summary>
    /// <param name="offset">The offset in pixels, clamped to the content.</param>
    /// <remarks>
    /// For the moves that are not the reader scrolling: restoring a saved position when a panel opens,
    /// or jumping to a search hit. Gliding there would animate a distance the reader never travelled.
    /// </remarks>
    public void SnapTo(float offset)
    {
        float clamped = Math.Clamp(offset, 0f, MaxScroll);
        _scroll.Target = clamped;
        _scroll.SnapTo(clamped);
    }

    /// <summary>Returns the row under a point, or -1 when the point misses every row.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    /// <returns>The row index, or -1.</returns>
    public int RowAt(UiVec2 point)
    {
        if (!Bounds.Contains(point) || RowHeight <= 0f)
        {
            return -1;
        }

        float shift = AnchorToBottom ? MathF.Max(0f, Bounds.Height - ContentHeight) : 0f;
        int row = (int)((point.Y - Bounds.Y - shift + _scroll.Value) / RowHeight);
        return row >= 0 && row < RowCount ? row : -1;
    }

    /// <summary>Scrolls a row into view by the shortest move that reveals it.</summary>
    /// <param name="row">The row to reveal.</param>
    public void EnsureVisible(int row)
    {
        if (row < 0 || RowHeight <= 0f)
        {
            return;
        }

        float top = row * RowHeight;
        float bottom = top + RowHeight;

        if (top < ScrollOffset)
        {
            ScrollOffset = top;
        }
        else if (bottom > ScrollOffset + Bounds.Height)
        {
            ScrollOffset = bottom - Bounds.Height;
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override bool ClipsChildren => true;

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (StickToEnd)
        {
            _scroll.Target = MaxScroll;
        }
        else
        {
            _scroll.Target = Math.Clamp(_scroll.Target, 0f, MaxScroll);
        }

        _scroll.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (DrawRow is not { } draw || RowCount <= 0 || RowHeight <= 0f || Bounds.IsEmpty)
        {
            return;
        }

        painter.PushClip(Bounds);

        // The row window comes from the animated offset, so a glide reveals rows smoothly rather than
        // holding the old set until it settles.
        float offset = _scroll.Value;

        // Short content is pushed down to meet the bottom edge when the list asks for it. This can
        // only ever apply while everything fits, so it never fights the scroll offset.
        float shift = AnchorToBottom ? MathF.Max(0f, Bounds.Height - ContentHeight) : 0f;

        int first = Math.Max(0, (int)(offset / RowHeight));
        int last = Math.Min(RowCount - 1, (int)((offset + Bounds.Height) / RowHeight));

        for (int row = first; row <= last; row++)
        {
            var rect = new UiRect(
                Bounds.X,
                Bounds.Y + shift + (row * RowHeight) - offset,
                Bounds.Width,
                RowHeight);

            draw(painter, row, rect);
        }

        painter.PopClip();
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        if (!ShowScrollBar || ContentHeight <= Bounds.Height || Bounds.IsEmpty)
        {
            return;
        }

        const float Width = 4f;
        float trackHeight = Bounds.Height;
        float thumbHeight = MathF.Max(24f, trackHeight * (trackHeight / ContentHeight));
        float travel = trackHeight - thumbHeight;
        float progress = MaxScroll <= 0f ? 0f : Math.Clamp(_scroll.Value / MaxScroll, 0f, 1f);

        painter.FillRect(
            new UiRect(Bounds.Right - Width - 2f, Bounds.Y + (travel * progress), Width, thumbHeight),
            _theme.TextMuted.WithAlpha(90));
    }
}
