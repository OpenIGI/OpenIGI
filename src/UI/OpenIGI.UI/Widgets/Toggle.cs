using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>An on/off row: a caption on the left and a two-segment OFF / ON switch on the right.</summary>
/// <remarks>
/// <para>
/// The switch is the design's: a 112×30 track with two opposite corners cut, and a knob that slides
/// across to cover whichever side is current — grey over OFF, amber over ON — with the words set in the
/// condensed face so the state is read, not inferred from a position.
/// </para>
/// <para>
/// Left and right set the value directly rather than toggling, so a pad player can never flip it the
/// wrong way by pressing twice; accept or a click toggles.
/// </para>
/// </remarks>
public class Toggle : Control
{
    private const float TrackWidthUnits = 112f;
    private const float TrackHeightUnits = 30f;
    private const float CutUnits = 8f;

    private static readonly string Off = "OFF";
    private static readonly string On = "ON";

    private readonly Tween _knob = new(0f, duration: 0.22f, Ease.Snappy);
    private bool _value;

    /// <summary>Initializes a new instance of the <see cref="Toggle"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="label">The caption.</param>
    /// <param name="value">The initial value.</param>
    public Toggle(UiTheme theme, string label, bool value = false)
        : base(theme)
    {
        Label = label ?? string.Empty;
        _value = value;
        _knob.SnapTo(value ? 1f : 0f);
    }

    /// <summary>Raised when the value changes.</summary>
    public event Action<bool>? ValueChanged;

    /// <summary>Gets or sets the caption.</summary>
    public string Label { get; set; }

    /// <summary>Gets or sets the value.</summary>
    public bool Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            _knob.Retarget(value ? 1f : 0f);
            ValueChanged?.Invoke(value);
        }
    }

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        switch (direction)
        {
            case NavDirection.Left when Value:
                Value = false;
                return true;
            case NavDirection.Right when !Value:
                Value = true;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    protected override void OnActivate() => Value = !Value;

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        _knob.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        UiRowStyle.Measure(this, context, available);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float highlight = UiRowStyle.Highlight(this);
        UiRowStyle.DrawFrame(painter, this, Label, highlight);
        DrawSwitch(painter, SwitchRect(painter.Context), Theme, _knob.Value, highlight, IsEnabled, Visual.Appear);
    }

    /// <summary>Draws the OFF / ON switch on its own, for a widget that wants it outside a row.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="track">The switch's box, normally 112×30 design units.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="on">Where the knob is, 0 over OFF to 1 over ON.</param>
    /// <param name="highlight">How lit the switch is under the pointer, 0 to 1.</param>
    /// <param name="enabled">Whether the switch can be used.</param>
    /// <param name="appear">The owner's entrance opacity.</param>
    public static void DrawSwitch(UiPainter painter, UiRect track, UiTheme theme, float on, float highlight, bool enabled, float appear)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);

        float cut = painter.Context.Px(CutUnits);
        const UiCorners Corners = UiCorners.TopLeft | UiCorners.BottomRight;

        painter.FillCutRect(track, cut, Corners, UiPalette.White(Interpolate.Lerp(0.05f, 0.09f, highlight) * appear));

        // The knob is the cut shape seen through its own box, so it keeps the track's corners wherever it is.
        float half = track.Width * 0.5f;
        var knob = new UiRect(track.X + (half * on), track.Y, half, track.Height);
        Bgra32 knobColor = Interpolate.Lerp(UiPalette.White(0.16f), enabled ? theme.Accent : theme.TextDisabled, on);
        painter.PushClip(knob);
        painter.FillCutRect(track, cut, Corners, UiPalette.Fade(knobColor, appear));
        painter.PopClip();

        float sizePx = painter.Context.Px(14f);
        float tracking = 0.2f;
        DrawWord(Off, new UiRect(track.X, track.Y, half, track.Height), Interpolate.Lerp(theme.TextBright, theme.TextDim, on));
        DrawWord(On, new UiRect(track.X + half, track.Y, half, track.Height), Interpolate.Lerp(theme.TextDim, theme.OnAccent, on));

        void DrawWord(string word, UiRect box, Bgra32 color)
        {
            float width = UiPainter.MeasureLabel(theme.Heading, word, sizePx, tracking);
            var at = new UiVec2(box.X + ((box.Width - width) * 0.5f), UiRowStyle.CapsCenterY(theme.Heading, sizePx, box));
            painter.DrawLabel(theme.Heading, word, at, sizePx, UiPalette.Fade(enabled ? color : theme.TextDisabled, appear), tracking);
        }
    }

    private UiRect SwitchRect(LayoutContext context)
    {
        float width = context.Px(TrackWidthUnits);
        float height = context.Px(TrackHeightUnits);
        return new UiRect(
            Bounds.Right - context.Px(UiRowStyle.Inset) - width,
            Bounds.Y + ((Bounds.Height - height) * 0.5f),
            width,
            height);
    }
}
