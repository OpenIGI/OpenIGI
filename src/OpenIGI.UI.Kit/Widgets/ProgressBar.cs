using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>A thin progress bar: an amber fill over a faint track, easing to each new value.</summary>
public class ProgressBar : UiElement
{
    private readonly Spring _shown = new(0f, smoothTime: 0.12f);

    /// <summary>Initializes a new instance of the <see cref="ProgressBar"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public ProgressBar(UiTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
    }

    /// <summary>Gets the theme.</summary>
    public UiTheme Theme { get; }

    /// <summary>Gets or sets the progress, 0 to 1.</summary>
    public float Value { get; set; }

    /// <summary>Gets or sets the bar's thickness, in design units.</summary>
    public float Thickness { get; set; } = 4f;

    /// <summary>Gets or sets the fill's colour, or <see langword="null"/> for the accent.</summary>
    public Bgra32? Fill { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(available.X, context.Px(Thickness));

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        _shown.Target = Math.Clamp(Value, 0f, 1f);
        _shown.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float h = painter.Context.Px(Thickness);
        var track = new UiRect(Bounds.X, Bounds.Center.Y - (h * 0.5f), Bounds.Width, h);
        painter.FillRect(track, UiPalette.White(0.1f));
        painter.FillRect(new UiRect(track.X, track.Y, track.Width * Math.Clamp(_shown.Value, 0f, 1f), h), Fill ?? Theme.Accent);
    }
}
