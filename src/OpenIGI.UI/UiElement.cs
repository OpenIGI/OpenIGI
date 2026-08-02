using OpenIGI.UI.Layout;

namespace OpenIGI.UI;

/// <summary>How an element takes part in layout and drawing.</summary>
public enum Visibility
{
    /// <summary>Laid out and drawn.</summary>
    Visible,

    /// <summary>Laid out — it still takes space — but not drawn.</summary>
    Hidden,

    /// <summary>Neither laid out nor drawn, as if it were not in the tree.</summary>
    Collapsed,
}

/// <summary>
/// The base of the widget tree: a node with a stable identity that measures its desired size, is
/// arranged into a rectangle by its parent, and draws itself and its children.
/// </summary>
/// <remarks>
/// <para>
/// The layout contract is the two-pass measure/arrange the retail menu tree already uses
/// (<c>MenuWindowTask</c>) and that WPF codified: <see cref="Measure"/> asks each element how big it
/// wants to be given the space on offer, then <see cref="Arrange"/> hands it the space it actually got.
/// Sizes are authored in design units and turned into pixels through the <see cref="LayoutContext"/>;
/// positions come out in real back-buffer pixels. Keeping the two passes apart is what lets a container
/// know its children's desired sizes before it decides how to distribute space between them.
/// </para>
/// <para>
/// The tree is <b>retained</b>: elements live across frames, so identity is the object itself and
/// animation state, focus and scroll position survive a relayout. <see cref="Id"/> names an element for
/// lookup and for tests; it does not have to be unique, but <see cref="FindById"/> returns the first
/// match in tree order.
/// </para>
/// </remarks>
public abstract class UiElement
{
    private readonly List<UiElement> _children = [];

    /// <summary>Gets or sets a name used to find this element; need not be unique.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the space reserved around the element, in design units.</summary>
    public Thickness Margin { get; set; }

    /// <summary>Gets or sets the element's width in design units, or <see cref="float.NaN"/> to size to content.</summary>
    public float Width { get; set; } = float.NaN;

    /// <summary>Gets or sets the element's height in design units, or <see cref="float.NaN"/> to size to content.</summary>
    public float Height { get; set; } = float.NaN;

    /// <summary>Gets or sets the smallest width the element may take, in design units.</summary>
    public float MinWidth { get; set; }

    /// <summary>Gets or sets the smallest height the element may take, in design units.</summary>
    public float MinHeight { get; set; }

    /// <summary>Gets or sets the largest width the element may take, in design units.</summary>
    public float MaxWidth { get; set; } = float.PositiveInfinity;

    /// <summary>Gets or sets the largest height the element may take, in design units.</summary>
    public float MaxHeight { get; set; } = float.PositiveInfinity;

    /// <summary>Gets or sets how the element is placed horizontally within the space it is given.</summary>
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Stretch;

    /// <summary>Gets or sets how the element is placed vertically within the space it is given.</summary>
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Stretch;

    /// <summary>Gets or sets how the element takes part in layout and drawing.</summary>
    public Visibility Visibility { get; set; } = Visibility.Visible;

    /// <summary>Gets or sets a value indicating whether the element and its subtree can be hit by a pointer.</summary>
    public bool IsHitTestVisible { get; set; } = true;

    /// <summary>Gets the parent element, or <see langword="null"/> for the root.</summary>
    public UiElement? Parent { get; private set; }

    /// <summary>Gets the child elements in draw order.</summary>
    public IReadOnlyList<UiElement> Children => _children;

    /// <summary>Gets the element's desired size in pixels, valid after <see cref="Measure"/>.</summary>
    public UiVec2 DesiredSize { get; private set; }

    /// <summary>Gets the rectangle the element occupies in pixels, valid after <see cref="Arrange"/>.</summary>
    public UiRect Bounds { get; private set; }

    /// <summary>Gets the layout context from the most recent <see cref="Measure"/>.</summary>
    protected LayoutContext? Context { get; private set; }

    /// <summary>Adds a child to the end of the child list.</summary>
    /// <param name="child">The child to add.</param>
    /// <returns>The child, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="child"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="child"/> already has a parent.</exception>
    public UiElement AddChild(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null)
        {
            throw new InvalidOperationException("The element is already in a tree; remove it first.");
        }

        child.Parent = this;
        _children.Add(child);
        return child;
    }

    /// <summary>Removes a child.</summary>
    /// <param name="child">The child to remove.</param>
    /// <returns><see langword="true"/> when the child was present.</returns>
    public bool RemoveChild(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (_children.Remove(child))
        {
            child.Parent = null;
            return true;
        }

        return false;
    }

    /// <summary>Removes every child.</summary>
    public void ClearChildren()
    {
        foreach (UiElement child in _children)
        {
            child.Parent = null;
        }

        _children.Clear();
    }

    /// <summary>Finds the first element in this subtree with a given id.</summary>
    /// <param name="id">The id to match.</param>
    /// <returns>The element, or <see langword="null"/> when none matches.</returns>
    public UiElement? FindById(string id)
    {
        if (Id == id)
        {
            return this;
        }

        foreach (UiElement child in _children)
        {
            if (child.FindById(id) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Enumerates this element and its whole subtree in tree order.</summary>
    /// <returns>The element followed by each descendant.</returns>
    public IEnumerable<UiElement> Descendants()
    {
        yield return this;
        foreach (UiElement child in _children)
        {
            foreach (UiElement descendant in child.Descendants())
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Measures the element against the space on offer.</summary>
    /// <param name="context">The layout context.</param>
    /// <param name="available">The space available in pixels, including the element's own margin.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public void Measure(LayoutContext context, UiVec2 available)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;

        if (Visibility == Visibility.Collapsed)
        {
            DesiredSize = UiVec2.Zero;
            return;
        }

        Thickness margin = context.Px(Margin);
        float minW = context.Px(MinWidth);
        float minH = context.Px(MinHeight);
        float maxW = float.IsPositiveInfinity(MaxWidth) ? float.PositiveInfinity : context.Px(MaxWidth);
        float maxH = float.IsPositiveInfinity(MaxHeight) ? float.PositiveInfinity : context.Px(MaxHeight);

        // The content box the element gets to fill, after its margin and any explicit or maximum size.
        float availW = MathF.Max(0f, available.X - margin.Horizontal);
        float availH = MathF.Max(0f, available.Y - margin.Vertical);
        float contentAvailW = float.IsNaN(Width) ? MathF.Min(availW, maxW) : Math.Clamp(context.Px(Width), minW, maxW);
        float contentAvailH = float.IsNaN(Height) ? MathF.Min(availH, maxH) : Math.Clamp(context.Px(Height), minH, maxH);

        UiVec2 content = MeasureContent(context, new UiVec2(contentAvailW, contentAvailH));

        float desiredW = float.IsNaN(Width) ? content.X : context.Px(Width);
        float desiredH = float.IsNaN(Height) ? content.Y : context.Px(Height);
        desiredW = Math.Clamp(desiredW, minW, maxW);
        desiredH = Math.Clamp(desiredH, minH, maxH);

        DesiredSize = new UiVec2(desiredW + margin.Horizontal, desiredH + margin.Vertical);
    }

    /// <summary>Arranges the element into the rectangle its parent gives it.</summary>
    /// <param name="finalRect">The rectangle in pixels, including the element's own margin.</param>
    public void Arrange(UiRect finalRect)
    {
        if (Visibility == Visibility.Collapsed)
        {
            Bounds = UiRect.Empty;
            return;
        }

        LayoutContext context = Context ?? throw new InvalidOperationException("Arrange was called before Measure.");
        Thickness margin = context.Px(Margin);
        UiRect inner = finalRect.Deflate(margin);

        float desiredW = DesiredSize.X - margin.Horizontal;
        float desiredH = DesiredSize.Y - margin.Vertical;

        // An explicit size wins over Stretch: a 200-unit-wide button stays 200 wide in a stretch slot,
        // and only its alignment decides where the leftover space goes. Without a set size, Stretch fills
        // and everything else sits at its desired extent.
        bool fixedWidth = !float.IsNaN(Width);
        bool fixedHeight = !float.IsNaN(Height);
        float width = fixedWidth ? MathF.Min(inner.Width, desiredW)
            : HorizontalAlignment == HorizontalAlignment.Stretch ? inner.Width
            : MathF.Min(inner.Width, desiredW);
        float height = fixedHeight ? MathF.Min(inner.Height, desiredH)
            : VerticalAlignment == VerticalAlignment.Stretch ? inner.Height
            : MathF.Min(inner.Height, desiredH);

        float x = HorizontalAlignment switch
        {
            HorizontalAlignment.Left or HorizontalAlignment.Stretch => inner.X,
            HorizontalAlignment.Center => inner.X + ((inner.Width - width) * 0.5f),
            HorizontalAlignment.Right => inner.Right - width,
            _ => inner.X,
        };

        float y = VerticalAlignment switch
        {
            VerticalAlignment.Top or VerticalAlignment.Stretch => inner.Y,
            VerticalAlignment.Center => inner.Y + ((inner.Height - height) * 0.5f),
            VerticalAlignment.Bottom => inner.Bottom - height,
            _ => inner.Y,
        };

        Bounds = new UiRect(x, y, width, height);
        ArrangeContent(context, Bounds);
    }

    /// <summary>Draws the element and then its children.</summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <exception cref="ArgumentNullException"><paramref name="painter"/> is <see langword="null"/>.</exception>
    public void Draw(UiPainter painter)
    {
        ArgumentNullException.ThrowIfNull(painter);
        if (Visibility != Visibility.Visible)
        {
            return;
        }

        OnDraw(painter);

        bool clip = ClipsChildren;
        if (clip)
        {
            painter.PushClip(ClipRect);
        }

        foreach (UiElement child in _children)
        {
            child.Draw(painter);
        }

        if (clip)
        {
            painter.PopClip();
        }

        OnDrawOverlay(painter);
    }

    /// <summary>Advances any per-frame animation the element runs, then its children.</summary>
    /// <param name="elapsedSeconds">The real seconds since the last update.</param>
    public void Update(float elapsedSeconds)
    {
        OnUpdate(elapsedSeconds);
        foreach (UiElement child in _children)
        {
            child.Update(elapsedSeconds);
        }
    }

    /// <summary>Finds the topmost hit-testable element at a point.</summary>
    /// <param name="point">The point in pixels.</param>
    /// <returns>The element, or <see langword="null"/> when the point hits nothing.</returns>
    public UiElement? HitTest(UiVec2 point)
    {
        if (Visibility != Visibility.Visible || !IsHitTestVisible || !Bounds.Contains(point))
        {
            return null;
        }

        for (int i = _children.Count - 1; i >= 0; i--)
        {
            if (_children[i].HitTest(point) is { } hit)
            {
                return hit;
            }
        }

        return this;
    }

    /// <summary>Measures the element's content, excluding its margin, in pixels.</summary>
    /// <param name="context">The layout context.</param>
    /// <param name="available">The content space available in pixels.</param>
    /// <returns>The content's desired size in pixels. The default is empty; leaves and containers override it.</returns>
    protected virtual UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => UiVec2.Zero;

    /// <summary>Arranges the element's content within its bounds.</summary>
    /// <param name="context">The layout context.</param>
    /// <param name="contentRect">The element's bounds in pixels.</param>
    /// <remarks>The default arranges nothing; containers override it to place their children.</remarks>
    protected virtual void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
    }

    /// <summary>Draws the element itself, before its children.</summary>
    /// <param name="painter">The painter to draw into.</param>
    protected virtual void OnDraw(UiPainter painter)
    {
    }

    /// <summary>Draws over the element's children, after they have been drawn — e.g. a scrollbar.</summary>
    /// <param name="painter">The painter to draw into.</param>
    protected virtual void OnDrawOverlay(UiPainter painter)
    {
    }

    /// <summary>Gets a value indicating whether children are clipped to <see cref="ClipRect"/> when drawn.</summary>
    protected virtual bool ClipsChildren => false;

    /// <summary>Gets the rectangle children are clipped to when <see cref="ClipsChildren"/> is set.</summary>
    protected virtual UiRect ClipRect => Bounds;

    /// <summary>Advances the element's own animation.</summary>
    /// <param name="elapsedSeconds">The real seconds since the last update.</param>
    protected virtual void OnUpdate(float elapsedSeconds)
    {
    }
}
