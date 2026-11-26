using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>A caption with a value chosen between a minimum and a maximum: stepped, dragged, or typed.</summary>
/// <remarks>
/// <para>
/// The value steps by <see cref="Step"/> on each left/right press, which the control consumes so focus
/// stays put while the player scrubs it. A spring chases the value so the fill and knob glide rather than
/// jump when the value is nudged repeatedly. This is what a graphics or audio slider is built on, each
/// row bound to a CVar in the settings screen.
/// </para>
/// <para>
/// A press on the track takes hold of the thumb, which follows the pointer until the button comes up; Escape
/// during the drag puts the value back. A click on the readout, or Enter on the row, opens it for a typed number:
/// Enter keeps it, Escape drops it, and a number outside the range is held to the nearer end.
/// </para>
/// </remarks>
public class Slider : Control, ITextEntry
{
    // The design's metrics, in design units: a 180-unit track box, a 3-unit rail and fill, a 4×16 thumb,
    // and a monospaced readout at least 44 units wide, 14 units off the track.
    private const float TrackUnits = 180f;
    private const float RailUnits = 3f;
    private const float ThumbWidthUnits = 4f;
    private const float ThumbHeightUnits = 16f;
    private const float ReadoutGapUnits = 14f;
    private const float ReadoutMinUnits = 44f;
    private const float ReadoutSizeUnits = 14f;

    // The large look's: a 14 by 24 thumb a finger can take hold of, and a boxed readout 16 units off the track.
    private const float LargeThumbWidthUnits = 14f;
    private const float LargeThumbHeightUnits = 24f;
    private const float LargeReadoutGapUnits = 16f;
    private const float LargeReadoutSizeUnits = 15f;

    // The longest number the readout takes while it is typed into.
    private const int MaxEntryLength = 12;

    private readonly Spring _display;
    private float _value;
    private UiRect _track;
    private UiRect _readoutHit;
    private string? _readout;
    private float _readoutValue;
    private float _readoutWidth;
    private float _readoutWidthScale = -1f;
    private float _readoutWidthValue;
    private float _dragStartValue;
    private bool _dragging;

    // The number being typed, or null while the readout shows the value; and whether it is still the value as it
    // stood, which the first key typed replaces rather than adds to.
    private string? _entry;
    private bool _entryFresh;

    /// <summary>Initializes a new instance of the <see cref="Slider"/> class.</summary>
    /// <param name="theme">The theme to draw from.</param>
    /// <param name="label">The caption.</param>
    /// <param name="minimum">The smallest value.</param>
    /// <param name="maximum">The largest value.</param>
    /// <param name="value">The initial value.</param>
    /// <param name="step">How far one left/right press moves the value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximum"/> is not greater than <paramref name="minimum"/>.</exception>
    public Slider(UiTheme theme, string label, float minimum, float maximum, float value, float step)
        : base(theme)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minimum, maximum);
        Label = label ?? string.Empty;
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        _value = Math.Clamp(value, minimum, maximum);
        _display = new Spring(Normalised, smoothTime: 0.08f);
    }

    /// <summary>Raised when the value changes.</summary>
    public event Action<float>? ValueChanged;

    /// <summary>Gets or sets the caption.</summary>
    public string Label { get; set; }

    /// <summary>Gets the smallest value.</summary>
    public float Minimum { get; }

    /// <summary>Gets the largest value.</summary>
    public float Maximum { get; }

    /// <summary>Gets or sets how far one press moves the value.</summary>
    public float Step { get; set; }

    /// <summary>Gets or sets a formatter for the value readout, or <see langword="null"/> for the plain number.</summary>
    public Func<float, string>? Format { get; set; }

    /// <summary>
    /// Gets or sets what a typed number means as a value, or <see langword="null"/> when it is the value itself.
    /// </summary>
    /// <remarks>
    /// For a readout that is not the value: one that shows how far a mouse travels for a full turn takes that distance
    /// typed, and this turns it back into the slider's position. <see langword="null"/> from it leaves the value as it was.
    /// </remarks>
    public Func<float, float?>? FromTyped { get; set; }

    /// <summary>Gets where the track was laid out, in pixels: what a press takes hold of the thumb on.</summary>
    public UiRect TrackBounds => _track;

    /// <summary>Gets a value indicating whether a number is being typed into the readout.</summary>
    public bool IsEditing => _entry is not null;

    /// <summary>Gets the number typed so far, or an empty string when none is being typed.</summary>
    public string EntryText => _entry ?? string.Empty;

    /// <inheritdoc/>
    bool ITextEntry.IsTakingText => IsEditing;

    /// <inheritdoc/>
    bool ITextEntry.IsModal => true;

    /// <summary>Gets or sets the current value, clamped to the range.</summary>
    public float Value
    {
        get => _value;
        set
        {
            float clamped = Math.Clamp(value, Minimum, Maximum);
            if (clamped == _value)
            {
                return;
            }

            _value = clamped;
            _display.Target = Normalised;
            ValueChanged?.Invoke(_value);
        }
    }

    private float Normalised => (_value - Minimum) / (Maximum - Minimum);

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        if (IsEditing)
        {
            return true;
        }

        switch (direction)
        {
            case NavDirection.Left:
                Value -= Step;
                return true;
            case NavDirection.Right:
                Value += Step;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A click on the readout opens it for a typed number. A press on the track never arrives here: it is taken by
    /// <see cref="CapturesPointer"/> as the start of a drag. One on the caption just focuses the row.
    /// </remarks>
    public override void ActivateAt(UiVec2 point)
    {
        if (IsEnabled && !IsEditing && _readoutHit.Contains(point))
        {
            PlaySound(UiSounds.Choose);
            BeginEdit();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A press on the track, or just either end of it, takes hold of the thumb: the value lands where the pointer is,
    /// snapped to <see cref="Step"/>, and follows it until the button comes up.
    /// </remarks>
    public override bool CapturesPointer(UiVec2 point)
    {
        if (!OnTrack(point))
        {
            return false;
        }

        _dragStartValue = _value;
        _dragging = true;
        SetPressed(true);
        SetValueAt(point);
        return true;
    }

    /// <inheritdoc/>
    public override void PointerDragged(UiVec2 point)
    {
        if (_dragging)
        {
            SetValueAt(point);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Released, the drag is the choice; abandoned, the value goes back to where the press found it.</remarks>
    public override void PointerReleased(UiVec2 point, bool cancelled)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        SetPressed(false);
        if (cancelled)
        {
            Value = _dragStartValue;
            return;
        }

        RaiseActivated();
    }

    /// <summary>Opens the readout for a typed number, starting from the value as it stands.</summary>
    public void BeginEdit()
    {
        if (!IsEnabled || IsEditing)
        {
            return;
        }

        _entry = Readout();
        _entryFresh = true;
    }

    /// <inheritdoc/>
    /// <remarks>Only what a number is written with: digits, a sign, and a point (a comma is taken as one).</remarks>
    public bool Type(char character)
    {
        if (_entry is null)
        {
            return false;
        }

        if (character == ',')
        {
            character = '.';
        }

        if (!char.IsAsciiDigit(character) && character is not ('.' or '-' or '+'))
        {
            return false;
        }

        string current = _entryFresh ? string.Empty : _entry;
        if (current.Length >= MaxEntryLength)
        {
            return false;
        }

        _entry = current + character;
        _entryFresh = false;
        return true;
    }

    /// <inheritdoc/>
    public bool Backspace()
    {
        if (_entry is not { Length: > 0 } entry)
        {
            return false;
        }

        _entry = _entryFresh ? string.Empty : entry[..^1];
        _entryFresh = false;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Kept, the number is held to the range but not snapped to <see cref="Step"/>: a typed value is the one wanted. What
    /// does not read as a number, or a readout left as it was opened, leaves the value alone.
    /// </remarks>
    public void EndEntry(bool commit)
    {
        if (_entry is not { } entry)
        {
            return;
        }

        _entry = null;
        if (!commit || _entryFresh
            || !float.TryParse(entry, NumberStyles.Float, CultureInfo.InvariantCulture, out float typed))
        {
            return;
        }

        float? value = FromTyped is { } fromTyped ? fromTyped(typed) : typed;
        if (value is { } chosen && float.IsFinite(chosen))
        {
            Value = chosen;
            RaiseActivated();
        }
    }

    /// <inheritdoc/>
    /// <remarks>Enter on the row opens the readout for a typed number.</remarks>
    protected override void OnActivate() => BeginEdit();

    /// <inheritdoc/>
    /// <remarks>Losing the focus keeps a number being typed, as clicking away does.</remarks>
    protected override void OnFocusChanged(bool focused)
    {
        if (!focused)
        {
            EndEntry(commit: true);
        }
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        // The track is resolved here rather than in the draw so a click can be mapped onto it whether or
        // not the widget has been drawn yet. It sits at the right of the row, left of the readout.
        float readout = ReadoutWidth(context);

        // Large: the track and its boxed readout are 330 units together, less on a row too narrow for that and a caption.
        float width = Theme.Large
            ? MathF.Max(context.Px(90f), MathF.Min(context.Px(330f), contentRect.Width * 0.48f) - readout - context.Px(LargeReadoutGapUnits))
            : context.Px(TrackUnits);
        float right = contentRect.Right - context.Px(Theme.RowInset) - readout - context.Px(Theme.Large ? LargeReadoutGapUnits : ReadoutGapUnits);
        float height = context.Px(Theme.Large ? LargeThumbHeightUnits : ThumbHeightUnits);
        _track = new UiRect(right - width, contentRect.Y + ((contentRect.Height - height) * 0.5f), width, height);

        // What a click opens for typing: from where the track's grip ends to the row's right edge, the row's height.
        float left = MathF.Max(contentRect.Right - context.Px(Theme.RowInset) - readout, _track.Right + _track.Height);
        _readoutHit = new UiRect(left, contentRect.Y, MathF.Max(0f, contentRect.Right - left), contentRect.Height);
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        _display.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        UiRowStyle.Measure(this, context, available);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float highlight = UiRowStyle.Highlight(this);
        UiRowStyle.DrawFrame(painter, this, Label, highlight, _track.X);

        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        UiRect track = _track;
        float rail = context.Px(Theme.Large ? 4f : RailUnits);
        float railY = track.Y + ((track.Height - rail) * 0.5f);
        float t = Math.Clamp(_display.Value, 0f, 1f);
        Bgra32 fill = IsEnabled ? Theme.Accent : Theme.TextDisabled;

        painter.FillRect(new UiRect(track.X, railY, track.Width, rail), UiPalette.Fade(Theme.Track, appear));
        painter.FillRect(new UiRect(track.X, railY, track.Width * t, rail), UiPalette.Fade(fill, appear));

        float thumbW = context.Px(Theme.Large ? LargeThumbWidthUnits : ThumbWidthUnits);
        float thumbX = track.X + (track.Width * t) - (thumbW * 0.5f);
        Bgra32 thumb = IsEnabled ? Interpolate.Lerp(Theme.TextBright, Theme.AccentHover, MathF.Max(highlight * 0.35f, _dragging ? 1f : 0f)) : Theme.TextDisabled;
        var thumbBox = new UiRect(thumbX, track.Y, thumbW, track.Height);
        if (Theme.Large)
        {
            // An ink edge, so the thumb stands off the amber it sits on.
            painter.FillRect(thumbBox.Inflate(new Thickness(context.Px(2f), context.Px(2f))), UiPalette.Fade(Theme.OnAccent, appear));
        }

        painter.FillRect(thumbBox, UiPalette.Fade(thumb, appear));
        DrawReadout(painter, appear);
    }

    private void DrawReadout(UiPainter painter, float appear)
    {
        LayoutContext context = painter.Context;
        float sizePx = context.Px(Theme.Large ? LargeReadoutSizeUnits : ReadoutSizeUnits);
        string readout = _entry ?? Readout();
        float readoutWidth = Theme.Mono.MeasureWidth(readout, sizePx);
        float right = Bounds.Right - context.Px(Theme.RowInset);
        var box = new UiRect(right - ReadoutWidth(context), Bounds.Y, ReadoutWidth(context), Bounds.Height);

        // The large look boxes its readout always; the compact one only while it is being typed into, a little wider
        // than the number so the box does not touch it.
        float outset = Theme.Large ? 0f : context.Px(6f);
        float boxHeight = context.Px(Theme.Large ? 34f : 24f);
        var frame = new UiRect(box.X - outset, Bounds.Center.Y - (boxHeight * 0.5f), box.Width + (outset * 2f), boxHeight);
        if (_entry is not null)
        {
            painter.FillRect(frame, UiPalette.Fade(UiPalette.InkAt(0.6f), appear));
            painter.StrokeRect(frame, context.Px(1f), UiPalette.Fade(Theme.Accent, appear));
        }
        else if (Theme.Large)
        {
            painter.StrokeRect(frame, context.Px(1f), UiPalette.White(0.12f * appear));
        }

        if (Theme.Large)
        {
            right -= context.Px(10f);
        }

        // Being typed into, the number ends at a caret -- faint while the first key will still replace it all.
        float caretWidth = _entry is null ? 0f : context.Px(2f);
        painter.PushClip(frame);
        painter.DrawText(
            Theme.Mono,
            readout,
            new UiVec2(right - readoutWidth - (caretWidth * 2f), UiRowStyle.CenterY(Theme.Mono, sizePx, box)),
            sizePx,
            UiPalette.Fade(IsEnabled ? Theme.Text : Theme.TextDisabled, appear));
        if (_entry is not null)
        {
            float caretHeight = Theme.Mono.LineHeight(sizePx) * 0.78f;
            Bgra32 caret = _entryFresh ? Theme.Accent.WithAlpha(110) : Theme.Accent;
            painter.FillRect(
                new UiRect(right - caretWidth, Bounds.Center.Y - (caretHeight * 0.5f), caretWidth, caretHeight),
                UiPalette.Fade(caret, appear));
        }

        painter.PopClip();
    }

    private bool OnTrack(UiVec2 point)
    {
        UiRect track = _track;
        if (!IsEnabled || track.Width <= 0f)
        {
            return false;
        }

        float slack = track.Height;
        return point.X >= track.X - slack && point.X <= track.Right + slack;
    }

    private void SetValueAt(UiVec2 point)
    {
        UiRect track = _track;
        float t = Math.Clamp((point.X - track.X) / track.Width, 0f, 1f);
        float raw = Minimum + (t * (Maximum - Minimum));
        Value = Step > 0f ? Minimum + (MathF.Round((raw - Minimum) / Step) * Step) : raw;
    }

    // Formatted once per value rather than once per frame: the readout is drawn every frame the menu is
    // up, and a formatter that builds a sentence would otherwise allocate it each time.
    private string Readout()
    {
        if (_readout is null || _readoutValue != _value)
        {
            _readoutValue = _value;
            _readout = Format?.Invoke(_value) ?? _value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        return _readout;
    }

    // Wide enough for either end of the range as well as the current value, so the track does not shift
    // sideways as a formatted readout grows a digit.
    private float ReadoutWidth(LayoutContext context)
    {
        if (_readoutWidthScale == context.Scale && _readoutWidthValue == _value)
        {
            return _readoutWidth;
        }

        float size = context.Px(Theme.Large ? LargeReadoutSizeUnits : ReadoutSizeUnits);
        float widest = MathF.Max(Theme.Mono.MeasureWidth(Readout(), size), Theme.Mono.MeasureWidth(Format(Minimum), size));
        widest = MathF.Max(widest, Theme.Mono.MeasureWidth(Format(Maximum), size));

        _readoutWidthScale = context.Scale;
        _readoutWidthValue = _value;
        _readoutWidth = Theme.Large ? MathF.Max(context.Px(60f), widest + context.Px(20f)) : MathF.Max(context.Px(ReadoutMinUnits), widest);
        return _readoutWidth;

        string Format(float value) =>
            this.Format?.Invoke(value) ?? value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
