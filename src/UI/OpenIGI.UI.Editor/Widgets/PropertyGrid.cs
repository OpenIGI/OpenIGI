using OpenIGI.Input;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>
/// The inspector's body: labelled rows of editors grouped in collapsible sections, with a draggable divider
/// between the labels and the values.
/// </summary>
/// <remarks>
/// <para>
/// Rows are bound to getters and setters rather than to objects, so the grid works for anything — a task's
/// parameters, a light, the editor's own preferences. Each frame every row whose editor is not being used
/// pulls its value again, so an undo or a gizmo drag shows up in the grid at once.
/// </para>
/// <para>
/// Setters receive a <c>final</c> flag: false for the live steps of a scrub, true when the gesture ends, which
/// is when the editor closes the undo step.
/// </para>
/// </remarks>
public sealed class PropertyGrid : EditorElement
{
    private readonly List<Entry> _entries = [];
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly ScrollModel _scroll = new();
    private float _labelFraction = 0.38f;
    private bool _dividerHeld;

    /// <summary>Initializes a new instance of the <see cref="PropertyGrid"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public PropertyGrid(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Gets or sets the text shown when the grid has no rows.</summary>
    public string EmptyText { get; set; } = "Nothing selected";

    /// <summary>Gets the number of rows, sections included.</summary>
    public int Count => _entries.Count;

    /// <inheritdoc/>
    public override EditorCursor Cursor =>
        _dividerHeld || NearDivider(Root?.PointerPosition ?? default) ? EditorCursor.ResizeHorizontal : EditorCursor.Arrow;

    private float Row => Theme.RowHeight + Theme.Px(4f);

    /// <summary>Removes every row.</summary>
    public void Clear()
    {
        _entries.Clear();
        ClearChildren();
    }

    /// <summary>Starts a collapsible section; the rows added after it belong to it.</summary>
    /// <param name="title">The section title.</param>
    public void Section(string title) => _entries.Add(new Entry(title, null, null, IsSection: true));

    /// <summary>Adds a text row.</summary>
    /// <param name="label">The label.</param>
    /// <param name="get">Reads the value.</param>
    /// <param name="set">Writes it, or <see langword="null"/> for a read-only row.</param>
    /// <param name="tooltip">The label's tooltip.</param>
    /// <returns>The field.</returns>
    public TextField Text(string label, Func<string> get, Action<string>? set, string? tooltip = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        TextField field = new(Theme) { Text = get(), IsEnabled = set is not null };
        if (set is not null)
        {
            field.Committed += set;
        }

        Add(label, field, () => field.Text = get(), tooltip);
        return field;
    }

    /// <summary>Adds a number row.</summary>
    /// <param name="label">The label.</param>
    /// <param name="get">Reads the value.</param>
    /// <param name="set">Writes it, with whether the change is final.</param>
    /// <param name="tooltip">The label's tooltip.</param>
    /// <returns>The field, to set its step, decimals and range.</returns>
    public NumberField Number(string label, Func<double> get, Action<double, bool>? set, string? tooltip = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        NumberField field = new(Theme) { Value = get(), IsEnabled = set is not null };
        if (set is not null)
        {
            field.ValueChanged += set;
        }

        Add(label, field, () => field.Value = get(), tooltip);
        return field;
    }

    /// <summary>Adds a vector row.</summary>
    /// <param name="label">The label.</param>
    /// <param name="get">Reads the components.</param>
    /// <param name="set">Writes all components, with whether the change is final.</param>
    /// <param name="labels">The component labels, or <see langword="null"/> for X, Y, Z, W.</param>
    /// <param name="tooltip">The label's tooltip.</param>
    /// <returns>The field.</returns>
    public VectorField Vector(string label, Func<IReadOnlyList<double>> get, Action<double[], bool>? set, IReadOnlyList<string>? labels = null, string? tooltip = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        IReadOnlyList<double> initial = get();
        VectorField field = new(Theme, initial.Count, labels) { Values = initial, IsEnabled = set is not null };
        if (set is not null)
        {
            field.ValueChanged += (axis, value, final) =>
            {
                double[] values = [.. get()];
                values[axis] = value;
                set(values, final);
            };
        }

        Add(label, field, () => field.Values = get(), tooltip);
        return field;
    }

    /// <summary>Adds a tick-box row.</summary>
    /// <param name="label">The label.</param>
    /// <param name="get">Reads the value.</param>
    /// <param name="set">Writes it.</param>
    /// <param name="tooltip">The label's tooltip.</param>
    /// <returns>The box.</returns>
    public CheckBox Bool(string label, Func<bool> get, Action<bool>? set, string? tooltip = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        CheckBox box = new(Theme) { IsChecked = get(), IsEnabled = set is not null };
        if (set is not null)
        {
            box.CheckedChanged += set;
        }

        Add(label, box, () => box.IsChecked = get(), tooltip);
        return box;
    }

    /// <summary>Adds a choice row.</summary>
    /// <param name="label">The label.</param>
    /// <param name="options">The options.</param>
    /// <param name="get">Reads the chosen index.</param>
    /// <param name="set">Writes it.</param>
    /// <param name="tooltip">The label's tooltip.</param>
    /// <returns>The dropdown.</returns>
    public Dropdown Choice(string label, IReadOnlyList<string> options, Func<int> get, Action<int>? set, string? tooltip = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        Dropdown dropdown = new(Theme) { Options = options, SelectedIndex = get(), IsEnabled = set is not null };
        if (set is not null)
        {
            dropdown.SelectionChanged += set;
        }

        Add(label, dropdown, () => dropdown.SelectedIndex = get(), tooltip);
        return dropdown;
    }

    /// <summary>Adds a row holding any editor.</summary>
    /// <param name="label">The label.</param>
    /// <param name="editor">The editor.</param>
    /// <param name="refresh">Pulls the current value into it each frame, or <see langword="null"/>.</param>
    /// <param name="tooltip">The label's tooltip.</param>
    public void Add(string label, EditorElement editor, Action? refresh = null, string? tooltip = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        editor.Tooltip ??= tooltip;
        _entries.Add(new Entry(label, editor, refresh, IsSection: false) { Tooltip = tooltip });
        AddChild(editor);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float width = float.IsFinite(available.X) ? available.X : Theme.Px(320f);
        foreach (Entry entry in _entries)
        {
            entry.Editor?.Measure(context, new UiVec2(width * (1f - _labelFraction), Theme.ControlHeight));
        }

        return new UiVec2(width, float.IsFinite(available.Y) ? available.Y : VisibleRows() * Row);
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        _scroll.Measure((VisibleRows() * Row) + Theme.Padding, contentRect.Height);
        float divider = DividerX(contentRect);
        float right = contentRect.Right - Theme.Px(12f);
        foreach ((Entry entry, float y, bool hidden) in Walk(contentRect.Y))
        {
            if (entry.Editor is not { } editor)
            {
                continue;
            }

            if (hidden)
            {
                editor.Arrange(UiRect.Empty);
                editor.Visibility = Visibility.Hidden;
                continue;
            }

            editor.Visibility = y + Row < contentRect.Y || y > contentRect.Bottom ? Visibility.Hidden : Visibility.Visible;
            float top = MathF.Round(y + ((Row - Theme.ControlHeight) * 0.5f));
            editor.Arrange(new UiRect(divider + Theme.Px(6f), top, MathF.Max(0f, right - divider - Theme.Px(6f)), Theme.ControlHeight));
        }
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        foreach (Entry entry in _entries)
        {
            // A row being typed into or scrubbed keeps what the user is doing; every other row follows the data.
            if (entry.Editor is { } editor && !editor.IsFocusWithin && !editor.IsCapturing && !AnyChildCapturing(editor))
            {
                entry.Refresh?.Invoke();
            }
        }
    }

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        if (_scroll.TryGrab(Theme, Bounds, e.Position))
        {
            return true;
        }

        if (NearDivider(e.Position))
        {
            _dividerHeld = true;
            return true;
        }

        if (SectionAt(e.Position) is { } section)
        {
            if (!_collapsed.Remove(section))
            {
                _collapsed.Add(section);
            }

            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    protected internal override void OnPointerMove(in PointerEvent e)
    {
        if (_scroll.FollowGrab(Theme, Bounds, e.Position))
        {
            return;
        }

        if (_dividerHeld && Bounds.Width > 0f)
        {
            _labelFraction = Math.Clamp((e.Position.X - Bounds.X) / Bounds.Width, 0.15f, 0.7f);
        }
    }

    /// <inheritdoc/>
    protected internal override void OnPointerUp(in PointerEvent e)
    {
        _dividerHeld = false;
        _scroll.ReleaseGrab();
    }

    /// <inheritdoc/>
    protected internal override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers) =>
        _scroll.Wheel(notches, Row * 3f);

    /// <inheritdoc/>
    protected override bool ClipsChildren => true;

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Panel);
        if (_entries.Count == 0)
        {
            float width = Theme.Font.MeasureWidth(EmptyText, Theme.TextSize);
            EditorDraw.Label(painter, Theme, EmptyText, new UiRect(Bounds.Center.X - (width * 0.5f), Bounds.Y + Theme.Px(24f), width + 1f, Row), Theme.TextDisabled);
            return;
        }

        painter.PushClip(Bounds);
        float divider = DividerX(Bounds);
        foreach ((Entry entry, float y, bool hidden) in Walk(Bounds.Y))
        {
            if (hidden || y + Row < Bounds.Y || y > Bounds.Bottom)
            {
                continue;
            }

            if (entry.IsSection)
            {
                bool collapsed = _collapsed.Contains(entry.Label);
                UiRect header = new(Bounds.X, y, Bounds.Width, Row);
                painter.FillRect(header.Deflate(new Thickness(0f, Theme.Px(1f))), Theme.Chrome);
                painter.DrawChevron(new UiVec2(header.X + Theme.Px(12f), header.Center.Y), Theme.Px(3.5f), collapsed ? UiDirection.Right : UiDirection.Down, Theme.Hairline * 1.5f, Theme.TextMuted);
                EditorDraw.Label(painter, Theme, entry.Label, new UiRect(header.X + Theme.Px(22f), header.Y, header.Width - Theme.Px(30f), header.Height), Theme.Text, strong: true);
                continue;
            }

            UiRect label = new(Bounds.X + Theme.Px(14f), y, divider - Bounds.X - Theme.Px(18f), Row);
            EditorDraw.Label(painter, Theme, entry.Label, label, entry.Editor!.IsEnabled ? Theme.TextMuted : Theme.TextDisabled);
        }

        painter.PopClip();
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        _scroll.DrawVertical(painter, Theme, Bounds, IsPointerOver ? Root?.PointerPosition : null);
    }

    private static bool AnyChildCapturing(UiElement element) =>
        element.Children.Any(static c => c is EditorElement { IsCapturing: true } || AnyChildCapturing(c));

    private float DividerX(UiRect rect) => MathF.Round(rect.X + (rect.Width * _labelFraction));

    private bool NearDivider(UiVec2 point) =>
        Bounds.Contains(point) && MathF.Abs(point.X - DividerX(Bounds)) <= Theme.Px(3f);

    private int VisibleRows() => Walk(0f).Count(static row => !row.Hidden);

    private string? SectionAt(UiVec2 point)
    {
        foreach ((Entry entry, float y, bool hidden) in Walk(Bounds.Y))
        {
            if (entry.IsSection && !hidden && point.Y >= y && point.Y < y + Row)
            {
                return entry.Label;
            }
        }

        return null;
    }

    // The one walk over the rows: each entry with the y it sits at, and whether a collapsed section hides it.
    // A hidden row takes no height.
    private IEnumerable<(Entry Entry, float Y, bool Hidden)> Walk(float top)
    {
        float y = top + Theme.Px(4f) - _scroll.Offset;
        bool collapsed = false;
        foreach (Entry entry in _entries)
        {
            if (entry.IsSection)
            {
                collapsed = _collapsed.Contains(entry.Label);
                yield return (entry, y, false);
                y += Row;
            }
            else if (collapsed)
            {
                yield return (entry, y, true);
            }
            else
            {
                yield return (entry, y, false);
                y += Row;
            }
        }
    }

    private sealed record Entry(string Label, EditorElement? Editor, Action? Refresh, bool IsSection)
    {
        public string? Tooltip { get; init; }
    }
}
