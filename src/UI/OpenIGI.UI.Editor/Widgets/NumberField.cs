using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>
/// A number field: type a value, or press and drag sideways to scrub it — Shift for ten times faster,
/// Ctrl for ten times finer. A click without a drag starts typing.
/// </summary>
/// <remarks>
/// Scrubbing reports every step with <c>final</c> false and the release with <c>final</c> true, so the editor
/// can apply the value live and close the undo step once.
/// </remarks>
public class NumberField : TextField
{
    private double _value;
    private bool _pressArmed;
    private bool _scrubbing;
    private float _pressX;
    private double _pressValue;

    /// <summary>Initializes a new instance of the <see cref="NumberField"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public NumberField(EditorTheme theme)
        : base(theme)
    {
        Validate = text => TryParse(text, out _);
        Committed += text =>
        {
            if (TryParse(text, out double parsed))
            {
                SetValue(parsed, final: true);
            }
        };
    }

    /// <summary>Raised when the value changes; <c>final</c> is false for the steps of a scrub.</summary>
    public event Action<double, bool>? ValueChanged;

    /// <summary>Gets or sets the value. Setting it does not raise <see cref="ValueChanged"/>.</summary>
    public double Value
    {
        get => _value;
        set
        {
            _value = Clamp(value);
            Text = Format(_value);
        }
    }

    /// <summary>Gets or sets the change per pixel of scrubbing.</summary>
    public double Step { get; set; } = 0.01;

    /// <summary>Gets or sets the decimals shown; zero makes the field an integer field.</summary>
    public int Decimals { get; set; } = 3;

    /// <summary>Gets or sets the smallest value allowed.</summary>
    public double Minimum { get; set; } = double.NegativeInfinity;

    /// <summary>Gets or sets the largest value allowed.</summary>
    public double Maximum { get; set; } = double.PositiveInfinity;

    /// <summary>Gets or sets the colour of the strip on the field's left edge, for the axes of a vector.</summary>
    public Bgra32? Strip { get; set; }

    /// <summary>Gets or sets a short label drawn inside the field's left edge (<c>X</c>).</summary>
    public string? Prefix { get; set; }

    /// <inheritdoc/>
    public override bool FocusOnPress => IsFocused;

    /// <inheritdoc/>
    public override EditorCursor Cursor => IsFocused ? EditorCursor.Text : EditorCursor.ResizeHorizontal;

    /// <inheritdoc/>
    protected override UiRect TextBox
    {
        get
        {
            UiRect box = base.TextBox;
            float lead = Prefix is null ? 0f : Theme.Px(12f);
            return new UiRect(box.X + lead, box.Y, box.Width - lead, box.Height);
        }
    }

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        if (IsFocused)
        {
            return base.OnPointerDown(e);
        }

        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        _pressArmed = true;
        _scrubbing = false;
        _pressX = e.Position.X;
        _pressValue = _value;
        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnPointerMove(in PointerEvent e)
    {
        if (!_pressArmed)
        {
            base.OnPointerMove(e);
            return;
        }

        float dx = e.Position.X - _pressX;
        if (!_scrubbing && MathF.Abs(dx) < Theme.Px(3f))
        {
            return;
        }

        if (!_scrubbing)
        {
            _scrubbing = true;
            Root?.SetFocus(null);
        }

        double speed = e.Shift ? 10.0 : e.Control ? 0.1 : 1.0;
        SetValue(_pressValue + (dx * Step * speed), final: false);
    }

    /// <inheritdoc/>
    protected internal override void OnPointerUp(in PointerEvent e)
    {
        if (!_pressArmed)
        {
            base.OnPointerUp(e);
            return;
        }

        _pressArmed = false;
        if (_scrubbing)
        {
            _scrubbing = false;
            SetValue(_value, final: true);
            return;
        }

        // A click without a drag: start typing, with everything selected.
        Focus();
    }

    /// <inheritdoc/>
    protected internal override void OnCaptureLost()
    {
        if (_pressArmed && _scrubbing)
        {
            SetValue(_pressValue, final: true);
        }

        _pressArmed = false;
        _scrubbing = false;
    }

    /// <inheritdoc/>
    protected internal override bool OnKeyDown(in KeyEvent e)
    {
        if (e.Key is KeyCode.Up or KeyCode.Down)
        {
            double step = Decimals == 0 ? 1.0 : Step * 10.0;
            double speed = e.Shift ? 10.0 : e.Control ? 0.1 : 1.0;
            SetValue(_value + ((e.Key == KeyCode.Up ? 1 : -1) * step * speed), final: true);
            Edit.SetText(Format(_value));
            Edit.SelectAll();
            return true;
        }

        return base.OnKeyDown(e);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        base.OnDraw(painter);
        if (Strip is { } strip)
        {
            UiRect bar = new(Bounds.X + Theme.Hairline, Bounds.Y + Theme.Hairline, Theme.Px(2f), Bounds.Height - (Theme.Hairline * 2f));
            painter.FillRect(bar, strip);
        }

        if (Prefix is { } prefix)
        {
            UiRect box = new(Bounds.X + Theme.Px(6f), Bounds.Y, Theme.Px(10f), Bounds.Height);
            EditorDraw.Label(painter, Theme, prefix, box, Strip ?? Theme.TextMuted);
        }
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private void SetValue(double value, bool final)
    {
        double clamped = Clamp(Decimals == 0 ? Math.Round(value) : value);
        bool changed = clamped != _value;
        _value = clamped;
        if (!IsFocused)
        {
            Text = Format(_value);
        }

        if (changed || final)
        {
            ValueChanged?.Invoke(_value, final);
        }
    }

    private double Clamp(double value) => Math.Clamp(value, Minimum, Maximum);

    private string Format(double value) =>
        Decimals == 0
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0." + new string('#', Decimals), CultureInfo.InvariantCulture);
}
