using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>A field that shows one choice and opens a menu of them.</summary>
public sealed class Dropdown : Pressable
{
    private IReadOnlyList<string> _options = [];

    /// <summary>Initializes a new instance of the <see cref="Dropdown"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public Dropdown(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Raised when the user picks an option, with its index.</summary>
    public event Action<int>? SelectionChanged;

    /// <summary>Gets or sets the options.</summary>
    public IReadOnlyList<string> Options
    {
        get => _options;
        set => _options = value ?? [];
    }

    /// <summary>Gets or sets the chosen index, or -1. Setting it does not raise <see cref="SelectionChanged"/>.</summary>
    public int SelectedIndex { get; set; } = -1;

    /// <summary>Gets or sets the text shown when nothing is chosen, or a value outside the options.</summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override bool Focusable => true;

    /// <inheritdoc/>
    protected override void OnClick()
    {
        if (Root is not { } root || _options.Count == 0)
        {
            return;
        }

        List<MenuItem> items = [];
        for (int i = 0; i < _options.Count; i++)
        {
            int index = i;
            items.Add(MenuItem.Command(_options[i], () =>
            {
                SelectedIndex = index;
                SelectionChanged?.Invoke(index);
            }));
        }

        root.CloseAllPopups();
        root.ShowPopup(new MenuPopup(Theme, items) { Anchor = Bounds, Placement = PopupPlacement.Below });
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(float.IsFinite(available.X) ? available.X : Theme.Px(140f), Theme.ControlHeight);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        Bgra32 border = IsFocused ? Theme.Accent : IsPointerOver ? Theme.BorderStrong : Theme.Border;
        EditorDraw.Frame(painter, Theme, Bounds, Theme.Button, border);
        DrawFeedback(painter, Bounds);
        string text = SelectedIndex >= 0 && SelectedIndex < _options.Count ? _options[SelectedIndex] : Placeholder;
        float arrow = Theme.Px(20f);
        EditorDraw.Label(painter, Theme, text, new UiRect(Bounds.X + Theme.Px(8f), Bounds.Y, Bounds.Width - arrow - Theme.Px(8f), Bounds.Height), IsEnabled ? Theme.Text : Theme.TextDisabled);
        painter.DrawChevron(new UiVec2(Bounds.Right - (arrow * 0.5f), Bounds.Center.Y), Theme.Px(3.5f), UiDirection.Down, Theme.Hairline * 1.5f, Theme.TextMuted);
    }
}

/// <summary>Two to four number fields side by side, each with its axis colour: a position, an angle set, a colour.</summary>
public sealed class VectorField : EditorElement
{
    private static readonly string[] AxisNames = ["X", "Y", "Z", "W"];
    private readonly NumberField[] _fields;

    /// <summary>Initializes a new instance of the <see cref="VectorField"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="count">The number of components, 2 to 4.</param>
    /// <param name="labels">The component labels, or <see langword="null"/> for X, Y, Z, W.</param>
    public VectorField(EditorTheme theme, int count, IReadOnlyList<string>? labels = null)
        : base(theme)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 4);
        _fields = new NumberField[count];
        for (int i = 0; i < count; i++)
        {
            int axis = i;
            NumberField field = new(theme)
            {
                Prefix = labels?[i] ?? AxisNames[i],
                Strip = labels is null && count <= 3 ? theme.AxisColor(i) : null,
            };
            field.ValueChanged += (value, final) => ValueChanged?.Invoke(axis, value, final);
            _fields[i] = field;
            AddChild(field);
        }
    }

    /// <summary>Raised when one component changes: its index, value, and whether the change is final.</summary>
    public event Action<int, double, bool>? ValueChanged;

    /// <summary>Gets the component fields, to set their step and decimals.</summary>
    public IReadOnlyList<NumberField> Fields => _fields;

    /// <summary>Gets or sets the values. Setting them does not raise <see cref="ValueChanged"/>.</summary>
    public IReadOnlyList<double> Values
    {
        get => [.. _fields.Select(static f => f.Value)];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            for (int i = 0; i < _fields.Length && i < value.Count; i++)
            {
                _fields[i].Value = value[i];
            }
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float width = float.IsFinite(available.X) ? available.X : Theme.Px(240f);
        foreach (NumberField field in _fields)
        {
            field.Measure(context, new UiVec2(width / _fields.Length, Theme.ControlHeight));
        }

        return new UiVec2(width, Theme.ControlHeight);
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float gap = Theme.Px(4f);
        float width = (contentRect.Width - (gap * (_fields.Length - 1))) / _fields.Length;
        for (int i = 0; i < _fields.Length; i++)
        {
            _fields[i].Arrange(new UiRect(MathF.Round(contentRect.X + (i * (width + gap))), contentRect.Y, MathF.Floor(width), contentRect.Height));
        }
    }
}
