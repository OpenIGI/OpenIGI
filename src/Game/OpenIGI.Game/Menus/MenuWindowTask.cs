using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Menus;

/// <summary>
/// The base every menu type derives from: a rectangle, a line-up rule and the arrange pass that
/// applies it.
/// </summary>
/// <remarks>
/// <para>
/// The original registers this as <c>MenuWindow</c> with an instance size of 84 (<c>0x423FC0</c>), and
/// it owns the layout: <see cref="Arrange"/> is <c>0x4244B0</c>, <see cref="Measure"/> is the slot
/// whose base implementation is <c>0x424400</c>, and <see cref="Place"/> is <c>0x424420</c>.
/// </para>
/// <para>
/// Field offsets in the original, for anyone checking this against the binary: <c>+32</c> X,
/// <c>+36</c> Y, <c>+40</c> width, <c>+44</c> height, <c>+48</c> X-spacing, <c>+52</c> Y-spacing,
/// <c>+56</c> line-up mode, <c>+60</c> centre-horizontally, <c>+64</c> centre-vertically.
/// </para>
/// </remarks>
public abstract class MenuWindowTask : ContainerTask
{
    /// <summary>The spacing a menu window starts with on both axes.</summary>
    /// <remarks><c>MenuWindow</c>'s constructor (<c>0x4241C0</c>) writes 4 to both; everything else it touches starts at zero.</remarks>
    public const int DefaultSpacing = 4;

    /// <summary>Initializes a new instance of the <see cref="MenuWindowTask"/> class.</summary>
    protected MenuWindowTask()
    {
        LayoutSpacingX = DefaultSpacing;
        LayoutSpacingY = DefaultSpacing;
    }

    /// <summary>Gets or sets the left edge in authored pixels.</summary>
    public int LayoutX { get; set; }

    /// <summary>Gets or sets the top edge in authored pixels.</summary>
    public int LayoutY { get; set; }

    /// <summary>Gets or sets the width in authored pixels.</summary>
    public int LayoutWidth { get; set; }

    /// <summary>Gets or sets the height in authored pixels.</summary>
    public int LayoutHeight { get; set; }

    /// <summary>Gets or sets the horizontal gap between children.</summary>
    public int LayoutSpacingX { get; set; }

    /// <summary>Gets or sets the vertical gap between children.</summary>
    public int LayoutSpacingY { get; set; }

    /// <summary>Gets or sets how this window arranges its children.</summary>
    public MenuLineUpMode LineUp { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this item centres itself horizontally in its parent
    /// when the parent's line-up does not place it on that axis.
    /// </summary>
    /// <remarks>
    /// <c>MenuText</c>'s constructor sets both this and <see cref="CentreVertically"/>, which is why a
    /// column of menu entries comes out centred in its frame without anything saying so.
    /// </remarks>
    public bool CentreHorizontally { get; set; }

    /// <summary>Gets or sets a value indicating whether this item centres itself vertically in its parent.</summary>
    public bool CentreVertically { get; set; }

    /// <summary>Gets a value indicating whether this item takes part in pointer interaction at all.</summary>
    /// <remarks>
    /// Field <c>+76</c> in the original, and the reason a container does not swallow its children's
    /// clicks: <c>MenuFrame_Update</c> (<c>0x422A50</c>) runs its hit-test only when the flag is set,
    /// and <c>MenuText_Prepare</c> (<c>0x420F40</c>) sets it only for an entry that actually has an
    /// <c>On Click Script</c>. A frame gets it from its <c>Is Selectable</c> parameter.
    /// </remarks>
    public virtual bool IsInteractive => false;

    /// <summary>Gets or sets a value indicating whether this item carries the menu's selection.</summary>
    /// <remarks>
    /// Field <c>+77</c>, owned by the manager: <c>Menu_SetSelected</c> (<c>0x4186E0</c>) clears it on
    /// the item leaving the selection and sets it on the one arriving, so exactly one item has it.
    /// <c>MenuText_Render</c> tests it before drawing an entry lit and putting a glow behind it — so
    /// this, not the pointer-over state, is the visible highlight. A <c>MenuFrame</c> propagates its own
    /// value down to its children, which lights a whole group at once.
    /// </remarks>
    public bool IsHighlighted { get; set; }

    /// <summary>Gets a value indicating whether the pointer is over this item and it may be chosen.</summary>
    /// <remarks>Field <c>+73</c> in the original.</remarks>
    public bool IsActive { get; private set; }

    /// <summary>Gets a value indicating whether this item is latched under a held button.</summary>
    /// <remarks>Field <c>+74</c>. Pressing inside latches, so dragging off and back keeps the item armed.</remarks>
    public bool IsHeld { get; private set; }

    /// <summary>Gets a value indicating whether this item completed a click on the tick just gone.</summary>
    /// <remarks>Field <c>+75</c>. A click completes on <em>release</em> inside a latched item, not on press.</remarks>
    public bool WasClicked { get; private set; }

    /// <summary>Sets or clears the completed-click flag, for tests that have no pointer.</summary>
    /// <param name="clicked">What to set it to.</param>
    /// <remarks>
    /// The flag is normally owned by <see cref="HitTest"/>, which recomputes it every tick. A control's
    /// own update is what reads it, and testing that path needs a way to set it without driving a whole
    /// pointer through the manager — including clearing it, which the hit-test would do for real.
    /// </remarks>
    public void SetClickedForTest(bool clicked) => WasClicked = clicked;

    /// <summary>Gets or sets the key the manager has posted for this window to act on.</summary>
    /// <remarks>
    /// Field <c>+80</c>. See <see cref="MenuWindowCommand"/> — the manager posts, the control consumes.
    /// Note that only <c>MenuText_Update</c> (<c>0x4207E0</c>) clears it afterwards; a slider and a
    /// toggle leave it set, and rely on their own guards to make a repeat harmless.
    /// </remarks>
    public MenuWindowCommand PendingCommand { get; set; }

    /// <summary>Gets the manager this window belongs to, by walking up the tree.</summary>
    /// <remarks>
    /// Field <c>+68</c> in the original, which the base constructor threads down from the parent. Here
    /// the tree already has the parent links, so it is a walk rather than a stored copy.
    /// </remarks>
    public MenuManagerTask? Manager
    {
        get
        {
            for (GameTask? task = Parent; task is not null; task = task.Parent)
            {
                if (task is MenuManagerTask manager)
                {
                    return manager;
                }
            }

            return null;
        }
    }

    /// <summary>Seeds this window from its <c>Get Data Script</c>, then does the same for its children.</summary>
    /// <remarks>
    /// The <c>0x424870</c> class slot. The manager runs it over a screen on the way in, so each control
    /// reads its current value out of the configuration before it is first drawn. The base does nothing
    /// but recurse; a control with data overrides <see cref="OnLoadData"/>.
    /// </remarks>
    public void LoadData()
    {
        OnLoadData();
        foreach (MenuWindowTask child in Children.OfType<MenuWindowTask>())
        {
            child.LoadData();
        }
    }

    /// <summary>Resolves this window's own resources, then its children's.</summary>
    /// <remarks>
    /// The <c>sub_4F1A70</c> class slot — <c>SlideBar_Prepare</c> (<c>0x41F740</c>) and
    /// <c>ToggleBox_Prepare</c> (<c>0x41DF30</c>) are the two that do anything. It runs before the
    /// screen is measured, because what it resolves is what there is to measure.
    /// </remarks>
    public void PrepareSubtree()
    {
        OnPrepare();
        foreach (MenuWindowTask child in Children.OfType<MenuWindowTask>())
        {
            child.PrepareSubtree();
        }
    }

    /// <summary>Resolves this window's own resources. The base does nothing.</summary>
    protected virtual void OnPrepare()
    {
    }

    /// <summary>Writes this window back through its <c>Set Data Script</c>, then its children.</summary>
    /// <remarks>The <c>0x424880</c> class slot, the mirror of <see cref="LoadData"/>.</remarks>
    public void SaveData()
    {
        OnSaveData();
        foreach (MenuWindowTask child in Children.OfType<MenuWindowTask>())
        {
            child.SaveData();
        }
    }

    /// <summary>Reads this window's value from the script. The base does nothing.</summary>
    protected virtual void OnLoadData()
    {
    }

    /// <summary>Writes this window's value back to the script. The base does nothing.</summary>
    protected virtual void OnSaveData()
    {
    }

    /// <summary>Runs one of this window's expressions with <c>this</c> bound to it.</summary>
    /// <param name="source">The expression source, which may be empty.</param>
    /// <remarks>The original's <c>Expression_Run(&amp;field, this)</c>, which every control uses.</remarks>
    protected void Run(string source) => Manager?.RunAction(source, this);

    /// <summary>Evaluates one of this window's expressions as a number, with <c>this</c> bound to it.</summary>
    /// <param name="source">The expression source.</param>
    /// <returns>The value, or zero when there is nothing to evaluate.</returns>
    /// <remarks>
    /// The original evaluates a control's data expressions through the <em>float</em> walk and truncates
    /// where it wants an integer — <c>SlideBar_LoadData</c> (<c>0x41FF30</c>) does exactly that — so a
    /// <c>Get Data Script</c> returning <c>0.75</c> keeps its fraction.
    /// </remarks>
    protected double Evaluate(string source) => Manager?.EvaluateReal(source, this) ?? 0.0;

    /// <summary>Tests this item's rectangle against the pointer and updates its interaction flags.</summary>
    /// <param name="pointer">The pointer state.</param>
    /// <returns><see langword="true"/> when the item completed a click.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pointer"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Transcribed from <c>MenuItem_HitTest</c> (<c>0x4248A0</c>). Two details are worth naming because
    /// they are what make the buttons feel right and are easy to simplify away:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// The containment test is strictly greater than on the left and top edges and strictly less than
    /// on the right and bottom, so a pointer exactly on any edge is outside.
    /// </description></item>
    /// <item><description>
    /// A click completes on the <em>release</em>, and only if the item was latched by the press and the
    /// pointer is still inside. Pressing an entry and dragging away cancels it.
    /// </description></item>
    /// </list>
    /// </remarks>
    public bool HitTest(MenuPointer pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);

        if (!IsInteractive)
        {
            IsActive = false;
            IsHeld = false;
            WasClicked = false;
            return false;
        }

        bool inside = pointer.X > LayoutX
            && pointer.Y > LayoutY
            && pointer.X < LayoutX + LayoutWidth
            && pointer.Y < LayoutY + LayoutHeight;

        bool held = (pointer.IsDown && IsHeld) || (pointer.Pressed && inside);
        bool clicked = IsHeld && pointer.Released && inside;

        IsHeld = held;
        IsActive = (!pointer.IsDown || held) && inside;
        WasClicked = clicked;
        OnPointer(pointer, inside);
        return clicked;
    }

    /// <summary>Lets a control read the pointer itself, after the shared hit-test has run.</summary>
    /// <param name="pointer">The pointer state.</param>
    /// <param name="inside">Whether the pointer is within this window's rectangle.</param>
    /// <remarks>
    /// A <c>ScrollListBox</c> needs the pointer's position and not just "was clicked": it maps the
    /// vertical offset onto one of its rows and selects that row (<c>0x41BDD0</c>). The base does
    /// nothing, so controls that only care about clicks are unaffected.
    /// </remarks>
    protected virtual void OnPointer(MenuPointer pointer, bool inside)
    {
    }

    /// <summary>Gets the windows this one owns outright, which are not children in the task tree.</summary>
    /// <remarks>
    /// A <c>SlideBar</c>'s two arrows and a <c>ToggleBox</c>'s two halves are created with no parent
    /// (<c>Task_Create(0, class, 0)</c>) and held in the owner's own fields, and the owner's render and
    /// update reach them by hand rather than through a child walk. They still have to be hit-tested,
    /// which is the whole reason they are windows, so the hit-test visits them here.
    /// </remarks>
    public virtual IEnumerable<MenuWindowTask> OwnedWindows => [];

    /// <summary>Runs the hit-test over this window and every descendant.</summary>
    /// <param name="pointer">The pointer state.</param>
    /// <returns>The item that completed a click, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pointer"/> is <see langword="null"/>.</exception>
    public MenuWindowTask? HitTestSubtree(MenuPointer pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);

        MenuWindowTask? clicked = null;
        foreach (MenuWindowTask child in Children.OfType<MenuWindowTask>().Concat(OwnedWindows))
        {
            if (child.HitTest(pointer))
            {
                clicked = child;
            }

            clicked = child.HitTestSubtree(pointer) ?? clicked;
        }

        return clicked;
    }

    /// <summary>Reports the size this item occupies in a line-up.</summary>
    /// <param name="width">Receives the width.</param>
    /// <param name="height">Receives the height.</param>
    /// <returns><see langword="true"/> when the item takes part in the line-up.</returns>
    /// <remarks>The base reports the current rectangle, which is <c>0x424400</c> exactly.</remarks>
    public virtual bool Measure(out int width, out int height)
    {
        width = LayoutWidth;
        height = LayoutHeight;
        return true;
    }

    /// <summary>Arranges this window's children, then recurses into them.</summary>
    /// <remarks>
    /// <para>
    /// Transcribed from <c>MenuWindow_LineUp</c> (<c>0x4244B0</c>). Three passes: measure every child
    /// and total their sizes, compute a start cursor and a step from the line-up mode, then walk the
    /// children again placing each and advancing the cursor. Finally recurse, so a frame inside a
    /// frame lays out after its parent has positioned it.
    /// </para>
    /// <para>
    /// All of the arithmetic is integer, and the divisions truncate toward zero, so the leftover pixels
    /// of an uneven split land at the end rather than being distributed.
    /// </para>
    /// </remarks>
    public void Arrange()
    {
        MenuWindowTask[] children = [.. Children.OfType<MenuWindowTask>()];

        bool horizontal = false;
        bool vertical = false;
        int xCursor = 0;
        int yCursor = 0;
        int xStep = 0;
        int yStep = 0;

        if (LineUp != MenuLineUpMode.None)
        {
            int totalWidth = 0;
            int totalHeight = 0;
            int count = 0;

            foreach (MenuWindowTask child in children)
            {
                if (child.Measure(out int w, out int h))
                {
                    totalWidth += w;
                    totalHeight += h;
                    count++;
                }
            }

            switch (LineUp)
            {
                case MenuLineUpMode.VerticalTop:
                    yStep = LayoutSpacingY;
                    yCursor = LayoutY + yStep;
                    vertical = true;
                    break;

                case MenuLineUpMode.VerticalCentre:
                    yStep = (LayoutHeight - totalHeight) / (count + 1);
                    yCursor = LayoutY + yStep;
                    vertical = true;
                    break;

                case MenuLineUpMode.VerticalBottom:
                    yStep = LayoutSpacingY;
                    yCursor = LayoutY + LayoutHeight - (count * yStep) - totalHeight;
                    vertical = true;
                    break;

                case MenuLineUpMode.HorizontalLeft:
                    xStep = LayoutSpacingX;
                    xCursor = LayoutX + xStep;
                    horizontal = true;
                    break;

                case MenuLineUpMode.HorizontalCentre:
                    xStep = (LayoutWidth - totalWidth) / (count + 1);
                    xCursor = LayoutX + xStep;
                    horizontal = true;
                    break;

                case MenuLineUpMode.HorizontalRight:
                    xStep = LayoutSpacingX;
                    xCursor = LayoutX + LayoutWidth - (count * xStep) - totalWidth;
                    horizontal = true;
                    break;

                case MenuLineUpMode.VerticalFillOut:
                    yCursor = LayoutY + LayoutSpacingY;
                    yStep = (LayoutHeight - (2 * LayoutSpacingY) - totalHeight) / Math.Max(1, count - 1);
                    vertical = true;
                    break;

                case MenuLineUpMode.HorizontalFillOut:
                    xCursor = LayoutX + LayoutSpacingX;
                    xStep = (LayoutWidth - (2 * LayoutSpacingX) - totalWidth) / Math.Max(1, count - 1);
                    horizontal = true;
                    break;

                default:
                    break;
            }
        }

        foreach (MenuWindowTask child in children)
        {
            child.Place(this, horizontal, vertical, ref xCursor, ref yCursor, xStep, yStep);
        }

        foreach (MenuWindowTask child in children)
        {
            child.Arrange();
        }
    }

    /// <summary>Places this item against a running cursor, advancing it.</summary>
    /// <param name="parent">The window laying this item out.</param>
    /// <param name="horizontal">Whether the parent is placing along the horizontal axis.</param>
    /// <param name="vertical">Whether the parent is placing along the vertical axis.</param>
    /// <param name="xCursor">The horizontal cursor, advanced when <paramref name="horizontal"/> is set.</param>
    /// <param name="yCursor">The vertical cursor, advanced when <paramref name="vertical"/> is set.</param>
    /// <param name="xStep">The horizontal gap added after this item.</param>
    /// <param name="yStep">The vertical gap added after this item.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Transcribed from <c>0x424420</c>. On an axis the parent is laying out, the item takes the cursor
    /// and the cursor advances by the step <em>plus the item's own size</em>. On an axis it is not, the
    /// item centres itself in the parent if its centre flag is set, and is otherwise left alone — which
    /// is what makes a vertical column of entries come out horizontally centred.
    /// </remarks>
    public void Place(
        MenuWindowTask parent,
        bool horizontal,
        bool vertical,
        ref int xCursor,
        ref int yCursor,
        int xStep,
        int yStep)
    {
        ArgumentNullException.ThrowIfNull(parent);

        if (horizontal)
        {
            LayoutX = xCursor;
            xCursor += xStep + LayoutWidth;
        }
        else if (CentreHorizontally)
        {
            LayoutX = parent.LayoutX + ((parent.LayoutWidth - LayoutWidth) >> 1);
        }

        if (vertical)
        {
            LayoutY = yCursor;
            yCursor += yStep + LayoutHeight;
        }
        else if (CentreVertically)
        {
            LayoutY = parent.LayoutY + ((parent.LayoutHeight - LayoutHeight) >> 1);
        }
    }
}
