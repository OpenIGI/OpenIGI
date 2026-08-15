using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Desktop;

/// <summary>
/// The performance panel: the frame's wall-clock statistics and what each render stage cost the GPU.
/// </summary>
/// <remarks>
/// <para>
/// A view onto instrumentation that already exists — <see cref="FramePerformanceTracker"/> and the
/// device's own stage timers — rather than new measurement. Nothing here starts a timer or keeps a
/// history; it asks and it draws, so turning the panel on cannot change what it reports.
/// </para>
/// <para>
/// Stages that did not run in the last completed GPU frame are dimmed rather than hidden, because
/// which passes are switched off is exactly what a reader is checking when they open this.
/// </para>
/// </remarks>
public sealed class StatsBody : UiElement
{
    private const int LabelColumns = 22;

    private readonly IGraphicsDevice _device;
    private readonly DevConsoleTheme _theme;
    private readonly List<RenderStageTiming> _stages = [];

    private float _cell = 8f;
    private float _textSize = 13f;

    /// <summary>Initialises a new instance of the <see cref="StatsBody"/> class.</summary>
    /// <param name="device">The device whose stage timings are shown.</param>
    /// <param name="theme">The palette and face.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public StatsBody(IGraphicsDevice device, DevConsoleTheme theme)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(theme);

        _device = device;
        _theme = theme;
    }

    /// <summary>Gets or sets the frame statistics to show, supplied by the host each frame.</summary>
    /// <remarks>
    /// Pushed rather than pulled because the wall-clock tracker belongs to the game loop, which is the
    /// only thing that knows when a frame began and ended.
    /// </remarks>
    public FramePerformanceStatistics Frame { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        _textSize = context.Px(DevConsoleCVars.FontSize.Value * 0.95f);
        _cell = _theme.Mono.MeasureWidth("0", _textSize);
        return available;
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float lineHeight = _theme.Mono.LineHeight(_textSize) * 1.35f;
        float y = Bounds.Y + 6f;
        float x = Bounds.X + _cell;

        y = DrawFrameSummary(painter, x, y, lineHeight);
        y += lineHeight * 0.4f;

        painter.FillRect(new UiRect(Bounds.X, y, Bounds.Width, 1f), _theme.Ui.Border);
        y += lineHeight * 0.4f;

        DrawStages(painter, x, y, lineHeight);
    }

    private float DrawFrameSummary(UiPainter painter, float x, float y, float lineHeight)
    {
        FramePerformanceStatistics frame = Frame;

        // The frame rate is the headline and gets the accent; the percentiles beside it are what
        // actually tell you whether it is smooth, which is why they are on the same line rather than
        // buried under it.
        painter.DrawText(
            _theme.Mono,
            $"{frame.FramesPerSecond,6:0.0} fps",
            new UiVec2(x, y),
            _textSize * 1.15f,
            _theme.Ui.Accent);

        painter.DrawText(
            _theme.Mono,
            $"{frame.AverageMilliseconds,6:0.00} ms   1% low {frame.OnePercentLowFramesPerSecond,5:0.0}",
            new UiVec2(x + (_cell * 12f), y),
            _textSize,
            _theme.Ui.TextMuted);

        y += lineHeight;

        painter.DrawText(
            _theme.Mono,
            $"p95 {frame.P95Milliseconds,6:0.00} ms   peak {frame.PeakMilliseconds,6:0.00} ms",
            new UiVec2(x, y),
            _textSize,
            _theme.Ui.TextMuted);

        return y + lineHeight;
    }

    private void DrawStages(UiPainter painter, float x, float y, float lineHeight)
    {
        GpuFrameTiming gpu = _device.GpuTiming();

        painter.DrawText(
            _theme.Mono,
            $"{"gpu total",-LabelColumns}{gpu.Milliseconds,7:0.00} ms",
            new UiVec2(x, y),
            _textSize,
            gpu.SampleCount > 0 ? _theme.Ui.Text : _theme.Ui.TextDisabled);

        y += lineHeight;

        _stages.Clear();
        _stages.AddRange(_device.StageTimings());

        foreach (RenderStageTiming stage in _stages)
        {
            if (y + lineHeight > Bounds.Bottom)
            {
                break;
            }

            Bgra32 colour = stage.Active ? _theme.Ui.Text : _theme.Ui.TextDisabled;
            string label = stage.Stage.ToString().ToLowerInvariant();

            painter.DrawText(
                _theme.Mono,
                string.Create(CultureInfo.InvariantCulture, $"{label,-LabelColumns}{stage.Milliseconds,7:0.00} ms"),
                new UiVec2(x, y),
                _textSize,
                colour);

            if (stage.Active && stage.PeakMilliseconds > 0.0)
            {
                painter.DrawText(
                    _theme.Mono,
                    string.Create(CultureInfo.InvariantCulture, $"peak {stage.PeakMilliseconds,6:0.00}"),
                    new UiVec2(x + (_cell * (LabelColumns + 11f)), y),
                    _textSize,
                    _theme.Ui.TextDisabled);
            }

            y += lineHeight;
        }
    }
}
