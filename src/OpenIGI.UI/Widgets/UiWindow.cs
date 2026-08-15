using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// A floating panel with a title bar that can be dragged, edges that can be resized, and a single
/// child filling what is left.
/// </summary>
/// <remarks>
/// <para>
/// Generic on purpose. The developer console, the variable browser, the watch list, the statistics
/// panel and the entity inspector are the same object with different contents, and writing the drag,
/// the resize, the snap and the chrome five times would mean five subtly different feels.
/// </para>
/// <para>
/// <b>It positions itself in absolute pixels</b> rather than through the usual measure/arrange
/// negotiation, because a window the reader has dragged somewhere must stay exactly where they put it.
/// <see cref="WindowLayer"/> arranges each window into its own <see cref="Frame"/>; the layout pass
/// below that point is entirely ordinary, so any existing widget works as content.
/// </para>
/// <para>
/// The window holds no drag state. Which window is being dragged, and from where, belongs to the layer
/// — only one drag can be in flight at a time, and the layer is the only thing that knows about all of
/// them.
/// </para>
/// </remarks>
public class UiWindow : UiElement
{
    private readonly Spring _focus = new(0f, smoothTime: 0.10f);
    private readonly Spring _hoverEdge = new(0f, smoothTime: 0.12f);
    private UiElement? _content;
    private UiVec2 _pointer;

    /// <summary>Initializes a new instance of the <see cref="UiWindow"/> class.</summary>
    /// <param name="theme">The theme the chrome draws from.</param>
    /// <param name="title">The title shown in the bar.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public UiWindow(UiTheme theme, string title = "")
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
        Title = title;
    }

    /// <summary>Occurs when the close button is pressed.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Gets the theme the chrome draws from.</summary>
    public UiTheme Theme { get; }

    /// <summary>Gets or sets the title shown in the bar.</summary>
    public string Title { get; set; }

    /// <summary>Gets or sets where the window sits, in back-buffer pixels.</summary>
    public UiRect Frame { get; set; } = new(80f, 80f, 900f, 520f);

    /// <summary>Gets or sets the smallest the window may be dragged to, in pixels.</summary>
    public UiVec2 MinSize { get; set; } = new(320f, 140f);

    /// <summary>Gets or sets a value indicating whether the window's edges resize it.</summary>
    public bool IsResizable { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the window can be moved by its title bar.</summary>
    public bool IsMovable { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether only the title bar is shown.</summary>
    /// <remarks>Collapsing keeps <see cref="Frame"/> intact so expanding restores the size the reader chose.</remarks>
    public bool IsCollapsed { get; set; }

    /// <summary>Gets or sets a value indicating whether the window is shown at all.</summary>
    public bool IsOpen { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether a close button is drawn.</summary>
    public bool CanClose { get; set; } = true;

    /// <summary>Gets or sets the title-bar height in pixels.</summary>
    public float TitleHeight { get; set; } = 30f;

    /// <summary>Gets or sets how far inside an edge still counts as a resize grip, in pixels.</summary>
    public float GripSize { get; set; } = 6f;

    /// <summary>Gets or sets the corner radius in pixels.</summary>
    public float CornerRadius { get; set; } = 8f;

    /// <summary>Gets or sets the generated sprites used for the rounded corners and the shadow.</summary>
    /// <remarks>Left unset the window still draws, with square corners and no shadow — see <see cref="UiPainter.FillRoundedRect"/>.</remarks>
    public UiSurfaces? Surfaces { get; set; }

    /// <summary>Gets or sets a value indicating whether this is the window in front.</summary>
    /// <remarks>Drives the one visual difference between windows: a brighter border, a stronger shadow and an accented title.</remarks>
    public bool IsFocusedWindow { get; set; }

    /// <summary>Gets or sets the region the pointer is currently over, so an edge can light before it is grabbed.</summary>
    public WindowRegion HoveredRegion { get; set; }

    /// <summary>Gets or sets where the pointer is, so title-bar buttons can light under it.</summary>
    /// <remarks>Set by <see cref="WindowLayer"/> each frame; a window never reads a device itself.</remarks>
    public UiVec2 PointerPosition
    {
        get => _pointer;
        set => _pointer = value;
    }

    /// <summary>Gets or sets the single element filling the window below its title bar.</summary>
    public UiElement? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value))
            {
                return;
            }

            ClearChildren();
            _content = value;
            if (value is not null)
            {
                AddChild(value);
            }
        }
    }

    /// <summary>Gets the rectangle the content occupies, below the title bar.</summary>
    public UiRect ClientRect => IsCollapsed
        ? UiRect.Empty
        : UiRect.FromEdges(Frame.X, Frame.Y + TitleHeight, Frame.Right, Frame.Bottom);

    /// <summary>Gets the rectangle the title bar occupies.</summary>
    public UiRect TitleRect => new(Frame.X, Frame.Y, Frame.Width, MathF.Min(TitleHeight, Frame.Height));

    /// <summary>Gets the rectangle the window occupies on screen, which is only its bar when collapsed.</summary>
    public UiRect VisibleFrame => IsCollapsed ? TitleRect : Frame;

    /// <summary>Gets the close button's rectangle, empty when there is none.</summary>
    public UiRect CloseRect
    {
        get
        {
            if (!CanClose)
            {
                return UiRect.Empty;
            }

            float size = MathF.Min(TitleHeight, Frame.Height);
            return new UiRect(Frame.Right - size, Frame.Y, size, size);
        }
    }

    /// <summary>Decides what part of the window a point is over.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    /// <returns>The region, or <see cref="WindowRegion.None"/> when the point misses the window.</returns>
    /// <remarks>
    /// <para>
    /// Corners are tested before edges and edges before the bar, because the reader aiming at a corner
    /// means the corner: the grips overlap, and the most specific one has to win or the bottom-right
    /// corner would only ever move the window.
    /// </para>
    /// <para>
    /// The grip band reaches <em>inwards</em> from the frame rather than outwards, so a window's
    /// resize area never overlaps the window behind it.
    /// </para>
    /// </remarks>
    public WindowRegion RegionAt(UiVec2 point)
    {
        UiRect frame = VisibleFrame;
        if (!IsOpen || !frame.Contains(point))
        {
            return WindowRegion.None;
        }

        WindowRegion region = WindowRegion.None;

        if (IsResizable && !IsCollapsed)
        {
            float grip = MathF.Max(3f, GripSize);

            if (point.X - frame.X <= grip)
            {
                region |= WindowRegion.Left;
            }
            else if (frame.Right - point.X <= grip)
            {
                region |= WindowRegion.Right;
            }

            if (point.Y - frame.Y <= grip)
            {
                region |= WindowRegion.Top;
            }
            else if (frame.Bottom - point.Y <= grip)
            {
                region |= WindowRegion.Bottom;
            }

            if (region != WindowRegion.None)
            {
                return region;
            }
        }

        return TitleRect.Contains(point) ? WindowRegion.Title : WindowRegion.Client;
    }

    /// <summary>Moves the window by a delta, keeping it reachable on screen.</summary>
    /// <param name="delta">How far to move, in pixels.</param>
    /// <param name="viewport">The screen the window must stay reachable within.</param>
    public void MoveBy(UiVec2 delta, UiVec2 viewport)
    {
        Frame = Frame.Offset(delta.X, delta.Y);
        ClampIntoView(viewport);
    }

    /// <summary>
    /// Keeps enough of the title bar on screen to grab, after a move or a resolution change.
    /// </summary>
    /// <param name="viewport">The back buffer's size in pixels.</param>
    /// <remarks>
    /// Deliberately permissive: a window may hang off the right, the left and the bottom, because a
    /// reader parking one half-off screen is a legitimate thing to want. What is never allowed is the
    /// title bar leaving the top, or so little of it remaining that it cannot be grabbed back — that is
    /// a window lost for good, and it survives into the settings file to be lost again next launch.
    /// </remarks>
    public void ClampIntoView(UiVec2 viewport)
    {
        const float MustRemain = 80f;

        float maxX = MathF.Max(0f, viewport.X - MustRemain);
        float minX = MathF.Min(0f, MustRemain - Frame.Width);
        float maxY = MathF.Max(0f, viewport.Y - MathF.Max(MustRemain, TitleHeight));

        Frame = new UiRect(
            Math.Clamp(Frame.X, minX, maxX),
            Math.Clamp(Frame.Y, 0f, maxY),
            Frame.Width,
            Frame.Height);
    }

    /// <summary>Resizes the window by moving one or two of its edges.</summary>
    /// <param name="edges">Which edges are being dragged.</param>
    /// <param name="delta">How far the pointer has moved since the drag began.</param>
    /// <param name="start">The frame the drag began from.</param>
    /// <remarks>
    /// Resolved against the frame the drag <em>started</em> from rather than accumulated frame by
    /// frame, so a drag that pushes an edge past its minimum and back again returns to exactly where it
    /// was instead of drifting by however much was clamped away.
    /// </remarks>
    public void ResizeBy(WindowRegion edges, UiVec2 delta, UiRect start)
    {
        float left = start.X;
        float top = start.Y;
        float right = start.Right;
        float bottom = start.Bottom;

        if ((edges & WindowRegion.Left) != 0)
        {
            left = MathF.Min(start.X + delta.X, right - MinSize.X);
        }

        if ((edges & WindowRegion.Right) != 0)
        {
            right = MathF.Max(start.Right + delta.X, left + MinSize.X);
        }

        if ((edges & WindowRegion.Top) != 0)
        {
            top = MathF.Min(start.Y + delta.Y, bottom - MinSize.Y);
        }

        if ((edges & WindowRegion.Bottom) != 0)
        {
            bottom = MathF.Max(start.Bottom + delta.Y, top + MinSize.Y);
        }

        Frame = UiRect.FromEdges(left, top, right, bottom);
    }

    /// <summary>Snaps edges that have come close to the viewport's, so windows line up without fuss.</summary>
    /// <param name="viewport">The back buffer's size in pixels.</param>
    /// <param name="distance">How near an edge must come before it snaps, in pixels. Zero disables snapping.</param>
    public void SnapToEdges(UiVec2 viewport, float distance)
    {
        if (distance <= 0f)
        {
            return;
        }

        float x = Frame.X;
        float y = Frame.Y;

        if (MathF.Abs(x) <= distance)
        {
            x = 0f;
        }
        else if (MathF.Abs(viewport.X - Frame.Right) <= distance)
        {
            x = viewport.X - Frame.Width;
        }

        if (MathF.Abs(y) <= distance)
        {
            y = 0f;
        }
        else if (MathF.Abs(viewport.Y - Frame.Bottom) <= distance)
        {
            y = viewport.Y - Frame.Height;
        }

        Frame = new UiRect(x, y, Frame.Width, Frame.Height);
    }

    /// <summary>Rounds the frame to whole pixels, so a hairline border never lands on a half pixel.</summary>
    public void SnapToPixelGrid() => Frame = new UiRect(
        MathF.Round(Frame.X),
        MathF.Round(Frame.Y),
        MathF.Round(Frame.Width),
        MathF.Round(Frame.Height));

    /// <summary>Raises the close event, as the close button does.</summary>
    public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        _focus.Target = IsFocusedWindow ? 1f : 0f;
        _focus.Update(elapsedSeconds);

        _hoverEdge.Target = (HoveredRegion & WindowRegion.AnyEdge) != 0 ? 1f : 0f;
        _hoverEdge.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        _content?.Measure(context, new UiVec2(Frame.Width, MathF.Max(0f, Frame.Height - TitleHeight)));
        return available;
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        if (_content is null)
        {
            return;
        }

        if (IsCollapsed || !IsOpen)
        {
            // Collapsing must empty the subtree's bounds, or rows nobody can see keep answering clicks.
            _content.Visibility = Visibility.Collapsed;
            return;
        }

        _content.Visibility = Visibility.Visible;
        _content.Arrange(ClientRect);
    }

    /// <inheritdoc/>
    protected override bool ClipsChildren => true;

    /// <inheritdoc/>
    protected override UiRect ClipRect => ClientRect;

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (!IsOpen)
        {
            return;
        }

        UiRect frame = VisibleFrame;
        float focus = _focus.Value;
        NineSlice rounded = Surfaces?.Rounded ?? default;
        NineSlice shadow = Surfaces?.Shadow ?? default;

        // The shadow is the only depth cue available -- there is no blur to fake one with -- so the
        // focused window casts a deeper one and that difference alone says which window is in front.
        painter.DrawShadow(
            frame,
            shadow,
            Theme.Shadow.WithAlpha((byte)(90f + (60f * focus))),
            spread: 14f + (6f * focus),
            offset: new UiVec2(0f, 5f));

        painter.FillRoundedRect(frame, rounded, Theme.Panel, CornerRadius);

        DrawTitleBar(painter, focus);

        if (!IsCollapsed)
        {
            DrawGlassSheen(painter);
        }

        // A hairline must be a whole pixel or the CPU clip half-covers it and it reads as a smear.
        float hairline = MathF.Max(1f, MathF.Round(painter.Context.Scale));
        Bgra32 border = Interpolate.Lerp(
            Theme.Border,
            Theme.Border.WithAlpha(200),
            MathF.Max(focus, _hoverEdge.Value));
        painter.StrokeRect(frame, hairline, border);

        if (!IsCollapsed && IsResizable)
        {
            DrawResizeGrip(painter, hairline);
        }
    }

    private void DrawTitleBar(UiPainter painter, float focus)
    {
        UiRect bar = TitleRect;
        NineSlice rounded = Surfaces?.Rounded ?? default;

        // Only the bar's top corners are round; its bottom meets the client area square. Drawing it
        // rounded and then squaring the lower half is cheaper than a second sprite and looks the same.
        painter.FillRoundedRect(bar, rounded, Theme.Surface, CornerRadius);
        if (!IsCollapsed && bar.Height > CornerRadius)
        {
            painter.FillRect(
                new UiRect(bar.X, bar.Bottom - CornerRadius, bar.Width, CornerRadius),
                Theme.Surface);
        }

        UiFont font = Theme.Mono;
        float size = MathF.Max(9f, TitleHeight * 0.42f);
        float tracking = size * 0.08f;
        float textY = bar.Y + ((bar.Height - font.LineHeight(size)) * 0.5f);

        painter.DrawTextTracked(
            font,
            Title.ToUpperInvariant(),
            new UiVec2(bar.X + 12f, textY),
            size,
            Interpolate.Lerp(Theme.TextMuted, Theme.Text, focus),
            tracking);

        // The accent underline appears only on the window in front. It springs in rather than
        // switching, so flicking between windows reads as one thing moving forward.
        if (focus > 0.01f)
        {
            float width = bar.Width * focus;
            painter.FillRect(new UiRect(bar.X, bar.Bottom - 2f, width, 2f), Theme.Accent);
        }

        if (CanClose)
        {
            DrawCloseButton(painter, CloseRect);
        }
    }

    /// <summary>
    /// Draws the close affordance as a multiplication sign from the window's own font.
    /// </summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="rect">The button's square.</param>
    /// <remarks>
    /// A glyph rather than geometry. Two perpendicular bars are the obvious thing to reach for with no
    /// line primitive, and they draw a plus sign — which says "add", the opposite of what the button
    /// does. A real diagonal cross would have to be stepped and would read as ragged at this size, so
    /// the typeface draws it instead, hinted and centred, for one quad.
    /// </remarks>
    private void DrawCloseButton(UiPainter painter, UiRect rect)
    {
        bool hovered = (HoveredRegion & WindowRegion.Title) != 0 && rect.Contains(_pointer);

        if (hovered)
        {
            painter.FillRoundedRect(
                rect.Deflate(new Thickness(rect.Width * 0.18f)),
                Surfaces?.Rounded ?? default,
                Theme.Danger.WithAlpha(150),
                rect.Width * 0.16f);
        }

        float size = rect.Height * 0.5f;
        UiFont font = Theme.Mono;
        float width = font.MeasureWidth("×", size);

        painter.DrawText(
            font,
            "×",
            new UiVec2(
                rect.X + ((rect.Width - width) * 0.5f),
                rect.Y + ((rect.Height - font.LineHeight(size)) * 0.5f)),
            size,
            hovered ? Theme.Text : Theme.TextMuted);
    }

    /// <summary>
    /// Fakes the look of a translucent pane without a blur, which the painter does not have.
    /// </summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <remarks>
    /// Three cheap cues do the work a blur would: a faint wash that is lighter at the top, a bright
    /// inner line along the top edge and a dark one along the bottom. Together they read as a sheet of
    /// glass catching the light rather than as a flat rectangle of paint.
    /// </remarks>
    private void DrawGlassSheen(UiPainter painter)
    {
        UiRect client = ClientRect;
        if (client.IsEmpty)
        {
            return;
        }

        painter.FillRect(
            new UiRect(client.X, client.Y, client.Width, MathF.Min(client.Height, 48f)),
            Theme.Text.WithAlpha(8));

        painter.FillRect(new UiRect(client.X, client.Y, client.Width, 1f), Theme.Text.WithAlpha(18));
        painter.FillRect(new UiRect(client.X, client.Bottom - 1f, client.Width, 1f), Bgra32.FromRgba(0, 0, 0, 60));
    }

    /// <summary>
    /// Draws the stepped pips that say the bottom-right corner can be dragged.
    /// </summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="unit">One physical pixel at the current scale.</param>
    /// <remarks>
    /// The only resize handle drawn at rest. The other seven announce themselves by brightening their
    /// edge as the pointer nears, which is the modern reading: affordance on approach rather than
    /// permanent furniture around every window.
    /// </remarks>
    private void DrawResizeGrip(UiPainter painter, float unit)
    {
        UiRect frame = VisibleFrame;
        float pip = MathF.Max(2f, unit * 2f);
        float step = pip * 2f;
        Bgra32 colour = Theme.TextMuted.WithAlpha(120);

        for (int i = 1; i <= 3; i++)
        {
            float offset = step * i;
            painter.FillRect(
                new UiRect(frame.Right - offset - pip, frame.Bottom - pip - (step * 1f), pip, pip),
                colour);
            painter.FillRect(
                new UiRect(frame.Right - pip - (step * 1f), frame.Bottom - offset - pip, pip, pip),
                colour);
        }
    }
}
