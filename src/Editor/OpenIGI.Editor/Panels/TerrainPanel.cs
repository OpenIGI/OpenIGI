using OpenIGI.Editor.Terrain;
using OpenIGI.Host.Editor;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>The terrain brush's settings: what it does, how wide it is and how strongly it works.</summary>
internal sealed class TerrainPanel : EditorElement
{
    private static readonly (TerrainBrushMode Mode, string Title)[] Modes =
    [
        (TerrainBrushMode.Raise, "Raise"),
        (TerrainBrushMode.Lower, "Lower"),
        (TerrainBrushMode.Smooth, "Smooth"),
        (TerrainBrushMode.Flatten, "Flatten"),
    ];

    private readonly IEditorHost _host;
    private readonly TerrainBrush _brush;
    private readonly Button[] _modes;
    private readonly NumberField _radius;
    private readonly NumberField _strength;

    public TerrainPanel(EditorTheme theme, IEditorHost host, TerrainBrush brush)
        : base(theme)
    {
        _host = host;
        _brush = brush;
        _modes = [.. Modes.Select(m =>
        {
            Button button = new(theme, m.Title) { IsToggle = true };
            button.Clicked += () => _brush.Mode = m.Mode;
            AddChild(button);
            return button;
        })];

        _radius = new NumberField(theme) { Minimum = 0.5, Maximum = 500.0, Step = 0.1, Decimals = 1, Value = brush.RadiusMetres };
        _radius.ValueChanged += (value, _) => _brush.RadiusMetres = value;
        _strength = new NumberField(theme) { Minimum = 0.05, Maximum = 100.0, Step = 0.05, Decimals = 2, Value = brush.Strength };
        _strength.ValueChanged += (value, _) => _brush.Strength = value;
        AddChild(_radius);
        AddChild(_strength);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        for (int i = 0; i < _modes.Length; i++)
        {
            _modes[i].IsChecked = Modes[i].Mode == _brush.Mode;
        }

        // The brush can also be resized from the viewport ([ and ]); keep the field showing what it is.
        if (!_radius.IsFocused && Math.Abs(_radius.Value - _brush.RadiusMetres) > 1e-9)
        {
            _radius.Value = _brush.RadiusMetres;
        }
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float pad = Theme.Px(8f);
        float x = contentRect.X + pad;
        float y = contentRect.Y + pad;
        float width = (contentRect.Width - (pad * 2f) - (Theme.Px(4f) * (_modes.Length - 1))) / _modes.Length;
        foreach (Button mode in _modes)
        {
            mode.Place(context, new UiRect(x, y, MathF.Max(0f, width), Theme.ControlHeight));
            x += width + Theme.Px(4f);
        }

        float label = Theme.Px(90f);
        float field = MathF.Max(0f, contentRect.Width - label - (pad * 2f));
        y += Theme.ControlHeight + pad;
        _radius.Place(context, new UiRect(contentRect.X + pad + label, y, field, Theme.ControlHeight));
        y += Theme.ControlHeight + Theme.Px(4f);
        _strength.Place(context, new UiRect(contentRect.X + pad + label, y, field, Theme.ControlHeight));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float pad = Theme.Px(8f);
        float y = Bounds.Y + pad + Theme.ControlHeight + pad;
        EditorDraw.Label(painter, Theme, "Radius (m)", new UiRect(Bounds.X + pad, y, Theme.Px(90f), Theme.ControlHeight), Theme.TextMuted);
        y += Theme.ControlHeight + Theme.Px(4f);
        EditorDraw.Label(painter, Theme, "Strength", new UiRect(Bounds.X + pad, y, Theme.Px(90f), Theme.ControlHeight), Theme.TextMuted);
        y += Theme.ControlHeight + pad;

        string hint = _host.Terrain is null
            ? "This mission's ground cannot be sculpted here yet."
            : "T: terrain tool  •  drag to paint  •  [ ] radius  •  Shift: the opposite of Raise or Lower";
        EditorDraw.Label(painter, Theme, hint, new UiRect(Bounds.X + pad, y, Bounds.Width - (pad * 2f), Theme.RowHeight), Theme.TextDisabled);
    }

}
