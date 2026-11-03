using OpenIGI.UI.Editor.Actions;
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
        (TerrainBrushMode.Paint, "Paint"),
    ];

    private readonly IEditorHost _host;
    private readonly TerrainBrush _brush;
    private static readonly (TerrainObjectMode Mode, string Title, string Tip)[] ObjectModes =
    [
        (TerrainObjectMode.MoveWithGround, "Move", "Objects standing on the ground rise and sink with it"),
        (TerrainObjectMode.Protect, "Protect", "The ground under and around objects is left alone"),
        (TerrainObjectMode.Ignore, "Ignore", "Objects stay put whatever the ground does"),
    ];

    private readonly Button[] _modes;
    private readonly Button[] _objectModes;
    private readonly Slider _radiusSlider;
    private readonly NumberField _radius;
    private readonly Slider _strengthSlider;
    private readonly NumberField _strength;
    private readonly Button _addArea;
    private readonly Button _addMountain;
    private readonly Dropdown _material;
    private readonly List<(string Text, float Y, bool Heading)> _labels = [];
    private float _notesY;
    private float _mountainNoteX;
    private IReadOnlyList<Game.World.TerrainPaintLayer>? _shownLayers;

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

        _objectModes = [.. ObjectModes.Select(m =>
        {
            Button button = new(theme, m.Title) { IsToggle = true, Tooltip = m.Tip };
            button.Clicked += () => _brush.Objects = m.Mode;
            AddChild(button);
            return button;
        })];

        // Each setting a slider to sweep and a field to type: on a doubling scale, as brushes run from a step to a valley.
        _radius = new NumberField(theme) { Minimum = 0.5, Maximum = 500.0, Step = 0.1, Decimals = 1, Value = brush.RadiusMetres };
        _radiusSlider = new Slider(theme) { Minimum = 0.5, Maximum = 500.0, Logarithmic = true, Value = brush.RadiusMetres, Tooltip = "How wide the brush is, in metres ([ and ] in the viewport)" };
        _radius.ValueChanged += (value, _) => SetRadius(value);
        _radiusSlider.ValueChanged += (value, _) => SetRadius(value);
        _strength = new NumberField(theme) { Minimum = 0.05, Maximum = 100.0, Step = 0.05, Decimals = 2, Value = brush.Strength };
        _strengthSlider = new Slider(theme) { Minimum = 0.05, Maximum = 100.0, Logarithmic = true, Value = brush.Strength, Tooltip = "How fast the brush works" };
        _strength.ValueChanged += (value, _) => SetStrength(value);
        _strengthSlider.ValueChanged += (value, _) => SetStrength(value);
        _addArea = new Button(theme, "Add Sculptable Area", KitIcon.Plus)
        {
            Tooltip = "Lay a sculptable heightmap over the ground in the middle of the view",
        };
        _addMountain = new Button(theme, "Add Mountain Area", KitIcon.Plus)
        {
            Tooltip = "A 512 m square shaped with no height limit, built into the level's terrain itself",
        };
        _material = new Dropdown(theme) { Placeholder = "No materials", Tooltip = "The ground material the Paint brush lays, over the square outlined on the ground" };
        _material.SelectionChanged += index => _brush.PaintLayer = index;
        foreach (UiElement child in (UiElement[])[_radiusSlider, _radius, _strengthSlider, _strength, _addArea, _addMountain, _material])
        {
            AddChild(child);
        }
    }

    /// <summary>Sets the action that lays new sculptable ground, shown as a button where only parts of the ground can be shaped.</summary>
    public EditorAction? AddAreaAction
    {
        set => _addArea.Action = value;
    }

    /// <summary>Sets the action that lays a mountain area, shown beside the sculptable-area one.</summary>
    public EditorAction? AddMountainAction
    {
        set => _addMountain.Action = value;
    }

    private bool IsPartial => _host.Terrain is { IsPartial: true } && !Painting;

    private bool Painting => _brush.Mode == TerrainBrushMode.Paint;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        for (int i = 0; i < _modes.Length; i++)
        {
            _modes[i].IsChecked = Modes[i].Mode == _brush.Mode;
        }

        for (int i = 0; i < _objectModes.Length; i++)
        {
            _objectModes[i].IsChecked = ObjectModes[i].Mode == _brush.Objects;
        }

        // The materials the ground offers, picked up again when the mission changes.
        IReadOnlyList<Game.World.TerrainPaintLayer> layers = _host.Terrain?.PaintLayers ?? [];
        if (!ReferenceEquals(layers, _shownLayers))
        {
            _shownLayers = layers;
            _material.Options = [.. layers.Select(static l => l.Name)];
            _brush.PaintLayer = Math.Clamp(_brush.PaintLayer, 0, Math.Max(0, layers.Count - 1));
            _material.SelectedIndex = layers.Count == 0 ? -1 : _brush.PaintLayer;
        }

        // The brush can also be resized from the viewport ([ and ]); keep the field showing what it is.
        if (!_radius.IsFocused && Math.Abs(_radius.Value - _brush.RadiusMetres) > 1e-9)
        {
            _radius.Value = _brush.RadiusMetres;
        }

        if (!_radiusSlider.IsDragging)
        {
            _radiusSlider.Value = _brush.RadiusMetres;
        }

        if (!_strengthSlider.IsDragging)
        {
            _strengthSlider.Value = _brush.Strength;
        }
    }

    private void SetRadius(double value)
    {
        _brush.RadiusMetres = value;
        _radius.Value = value;
        _radiusSlider.Value = value;
    }

    private void SetStrength(double value)
    {
        _brush.Strength = value;
        _strength.Value = value;
        _strengthSlider.Value = value;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// An inspector's layout: a label column and controls of a sensible width rather than stretched across the dock, the tools
    /// first, the brush's settings under them, then the sculptable areas as a section of their own.
    /// </remarks>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        _labels.Clear();
        float pad = Theme.Px(10f);
        float gap = Theme.Px(4f);
        float row = Theme.ControlHeight + Theme.Px(6f);
        float label = Theme.Px(96f);
        float left = contentRect.X + pad;
        float controls = left + label;
        float wide = MathF.Max(Theme.Px(160f), MathF.Min(Theme.Px(440f), contentRect.Right - pad - controls));
        float y = contentRect.Y + pad;

        // The tools, side by side at one width.
        _labels.Add(("Tool", y, false));
        float tool = MathF.Min(Theme.Px(84f), (wide - (gap * (_modes.Length - 1))) / _modes.Length);
        for (int i = 0; i < _modes.Length; i++)
        {
            _modes[i].Place(context, new UiRect(controls + (i * (tool + gap)), y, tool, Theme.ControlHeight));
        }

        y += row + Theme.Px(4f);
        float number = Theme.Px(64f);
        float slider = wide - number - gap;
        _labels.Add(("Radius (m)", y, false));
        _radiusSlider.Place(context, new UiRect(controls, y, slider, Theme.ControlHeight));
        _radius.Place(context, new UiRect(controls + slider + gap, y, number, Theme.ControlHeight));
        y += row;

        // Painting has a material where shaping has a strength and objects to look after.
        bool painting = Painting;
        _labels.Add((painting ? "Material" : "Strength", y, false));
        _material.Place(context, painting ? new UiRect(controls, y, wide, Theme.ControlHeight) : default);
        _strengthSlider.Place(context, painting ? default : new UiRect(controls, y, slider, Theme.ControlHeight));
        _strength.Place(context, painting ? default : new UiRect(controls + slider + gap, y, number, Theme.ControlHeight));
        y += row;
        float objectWidth = MathF.Min(Theme.Px(84f), (wide - (gap * 2f)) / 3f);
        if (!painting)
        {
            _labels.Add(("Objects", y, false));
        }

        for (int i = 0; i < _objectModes.Length; i++)
        {
            _objectModes[i].Place(context, painting ? default : new UiRect(controls + (i * (objectWidth + gap)), y, objectWidth, Theme.ControlHeight));
        }

        y += painting ? 0f : row;

        // The sculptable areas: a heading, then each button as wide as its words, the mountain one only where it can be used.
        _mountainNoteX = 0f;
        if (IsPartial)
        {
            y += Theme.Px(8f);
            _labels.Add(("Sculptable areas", y, true));
            y += Theme.RowHeight + Theme.Px(6f);
            float addWidth = ButtonWidth(_addArea);
            _addArea.Place(context, new UiRect(left, y, addWidth, Theme.ControlHeight));
            bool mountains = _addMountain.Action?.IsEnabled ?? false;
            _addMountain.Place(context, mountains ? new UiRect(left + addWidth + gap, y, ButtonWidth(_addMountain), Theme.ControlHeight) : default);
            _mountainNoteX = mountains ? 0f : left + addWidth + Theme.Px(10f);
            y += row;
        }
        else
        {
            _addArea.Place(context, default);
            _addMountain.Place(context, default);
        }

        _notesY = y + Theme.Px(4f);
    }

    // A button as wide as its words and its icon.
    private float ButtonWidth(Button button) => Theme.Font.MeasureWidth(button.Text, Theme.TextSize) + Theme.Px(44f);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float pad = Theme.Px(10f);
        float left = Bounds.X + pad;
        float width = Bounds.Width - (pad * 2f);
        foreach ((string text, float y, bool heading) in _labels)
        {
            if (heading)
            {
                EditorDraw.Label(painter, Theme, text, new UiRect(left, y, width, Theme.RowHeight), Theme.Text, strong: true);
                painter.FillRect(new UiRect(left, y + Theme.RowHeight + Theme.Px(2f), width, Theme.Hairline), Theme.Border);
                continue;
            }

            EditorDraw.Label(painter, Theme, text, new UiRect(left, y, Theme.Px(96f), Theme.ControlHeight), Theme.TextMuted);
        }

        if (_mountainNoteX > 0f)
        {
            const string note = "Mountain areas need an OpenIGI-only mission (File > Mission Settings).";
            EditorDraw.Label(painter, Theme, note, new UiRect(_mountainNoteX, _addArea.Bounds.Y, Bounds.Right - pad - _mountainNoteX, Theme.ControlHeight), Theme.TextMuted);
        }

        float line = _notesY;
        foreach (string note in Notes())
        {
            EditorDraw.Label(painter, Theme, note, new UiRect(left, line, width, Theme.RowHeight), Theme.TextMuted);
            line += Theme.RowHeight;
        }
    }

    // What the brush can do here and how to use it, a line each.
    private IEnumerable<string> Notes()
    {
        if (_host.Terrain is not { } terrain)
        {
            yield return "This mission's ground cannot be sculpted here yet.";
            yield break;
        }

        if (Painting)
        {
            yield return terrain.PaintLayers.Count == 0
                ? "This mission's ground has no materials that can be painted."
                : "Drag to lay the material inside its outlined square. Shift: take it away.";
        }
        else if (terrain.IsPartial)
        {
            yield return terrain.Grids.Count == 0
                ? "No part of this mission's ground can be sculpted yet: look at the ground and add a sculptable area."
                : $"Only the {terrain.Grids.Count} outlined area(s) can be sculpted. {terrain.LimitDescription} A mountain area has no limit.";
        }

        yield return Painting
            ? "T: terrain tool  \u2022  [ ] radius"
            : "T: terrain tool  \u2022  drag to sculpt  \u2022  Shift: the opposite of Raise or Lower  \u2022  [ ] radius";
    }
}
