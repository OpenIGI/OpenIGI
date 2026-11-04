using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>
/// A value picked along a track by dragging its handle or clicking where it should go, beside a number field that types it
/// exactly; on a logarithmic track every doubling is the same distance, for ranges that run from fine to huge.
/// </summary>
public sealed class Slider : EditorElement
{
    private bool _dragging;
    private double _value;

    /// <summary>Initializes a new instance of the <see cref="Slider"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public Slider(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Raised as the value changes, with whether the change is final (the drag let go) rather than in passing.</summary>
    public event Action<double, bool>? ValueChanged;

    /// <summary>Gets or sets the least value.</summary>
    public double Minimum { get; set; }

    /// <summary>Gets or sets the greatest value.</summary>
    public double Maximum { get; set; } = 1.0;

    /// <summary>Gets or sets whether the track is logarithmic: every doubling the same distance. Needs a positive minimum.</summary>
    public bool Logarithmic { get; set; }

    /// <summary>Gets or sets the value, clamped to the range; setting it raises nothing.</summary>
    public double Value
    {
        get => _value;
        set => _value = Math.Clamp(value, Minimum, Math.Max(Minimum, Maximum));
    }

    /// <inheritdoc/>
    public override EditorCursor Cursor => EditorCursor.Hand;

    /// <summary>Gets whether the handle is being dragged.</summary>
    public bool IsDragging => _dragging;

    private UiRect Track
    {
        get
        {
            float inset = Theme.Px(7f);
            return new UiRect(Bounds.X + inset, Bounds.Center.Y - Theme.Px(2f), MathF.Max(1f, Bounds.Width - (inset * 2f)), Theme.Px(4f));
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, Theme.ControlHeight);

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        if (e.Button != MouseButton.Left || !IsEnabled)
        {
            return false;
        }

        _dragging = true;
        SetFrom(e.Position.X, final: false);
        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnPointerMove(in PointerEvent e)
    {
        if (_dragging)
        {
            SetFrom(e.Position.X, final: false);
        }
    }

    /// <inheritdoc/>
    protected internal override void OnPointerUp(in PointerEvent e)
    {
        if (_dragging)
        {
            _dragging = false;
            SetFrom(e.Position.X, final: true);
        }
    }

    /// <inheritdoc/>
    protected internal override void OnCaptureLost()
    {
        if (_dragging)
        {
            _dragging = false;
            ValueChanged?.Invoke(_value, true);
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        UiRect track = Track;
        float at = track.X + (track.Width * (float)Fraction(_value));
        EditorDraw.Fill(painter, Theme, track, Theme.Input, Theme.Px(2f));
        EditorDraw.Fill(painter, Theme, new UiRect(track.X, track.Y, at - track.X, track.Height), IsEnabled ? Theme.Accent : Theme.TextDisabled, Theme.Px(2f));
        float handle = Theme.Px(_dragging || IsPointerOver ? 7f : 6f);
        painter.FillDot(new UiVec2(at, track.Center.Y), handle, IsEnabled ? Theme.Text : Theme.TextDisabled);
    }

    private double Fraction(double value)
    {
        if (Maximum <= Minimum)
        {
            return 0.0;
        }

        return Logarithmic && Minimum > 0.0
            ? Math.Log(value / Minimum) / Math.Log(Maximum / Minimum)
            : (value - Minimum) / (Maximum - Minimum);
    }

    private void SetFrom(float x, bool final)
    {
        UiRect track = Track;
        double t = Math.Clamp((x - track.X) / track.Width, 0.0, 1.0);
        double value = Logarithmic && Minimum > 0.0
            ? Minimum * Math.Pow(Maximum / Minimum, t)
            : Minimum + ((Maximum - Minimum) * t);
        _value = Math.Clamp(value, Minimum, Maximum);
        ValueChanged?.Invoke(_value, final);
    }
}
