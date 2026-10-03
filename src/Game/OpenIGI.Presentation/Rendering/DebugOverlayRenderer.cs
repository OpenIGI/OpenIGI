using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using OpenIGI.UI;

namespace OpenIGI.Presentation.Rendering;

/// <summary>Renders the developer diagnostics panels with the shared, modern UI vocabulary.</summary>
/// <remarks>
/// This renderer is deliberately separate from <c>StatusScreenRenderer</c>. The retail HUD uses
/// the game's bitmap status face, which is the right choice for faithful gameplay presentation but made
/// the F12 diagnostics inherit its green colour and pixel font. Diagnostics are a developer surface, so
/// they use the same Barlow widgets as the rest of the modern interface and never read a graphics preset
/// or menu renderer setting.
/// </remarks>
internal sealed class DebugOverlayRenderer : IDisposable
{
    private const float InsetDesign = 18f;
    private const float PanelGapDesign = 10f;
    private const float PanelWidthDesign = 720f;
    private const float RuleWidthDesign = 112f;
    private const float RuleHeightDesign = 2f;
    private const float SpacingDesign = 6f;
    private const float PaddingHorizontalDesign = 14f;
    private const float PaddingVerticalDesign = 12f;
    private const float HeadingSizeDesign = 20f;
    private const float BodySizeDesign = 16f;

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private UiFont? _body;
    private UiTheme? _theme;
    private bool _disposed;

    public DebugOverlayRenderer(IGraphicsDevice device, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);
        _device = device;
        _logger = logger;
    }

    public bool IsReady => _body is not null && _theme is not null;

    public bool Load()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!UiFontLibrary.TypographyAvailable)
        {
            _logger.Warning(
                "DebugOverlay",
                $"No interface font at {UiFontLibrary.FontsDirectory}; diagnostics cannot draw.");
            return false;
        }

        try
        {
            // The design's shared faces and palette, so F12 reads as part of the same interface. It does
            // not depend on the menus having loaded: the shared set is made on first use, so diagnostics
            // stay available under the vanilla menu or any render preset.
            UI.Kit.IgiTypography typography = UiFontLibrary.Typography(_device);
            _body = typography.Body;
            _theme = typography.CreateTheme();
            _theme.BodyTextSize = BodySizeDesign;
            _theme.HeadingTextSize = HeadingSizeDesign;
            _theme.Panel = UI.Widgets.UiPalette.GlassMenu;
            _theme.Shadow = Bgra32.FromRgba(0, 0, 0, 190);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            _logger.Warning("DebugOverlay", $"Could not load the diagnostics font: {ex.Message}");
            _body = null;
            _theme = null;
            return false;
        }
    }

    /// <summary>Draws the F12 state panel in the top-right corner.</summary>
    /// <returns>The rendered panel height in pixels.</returns>
    public float Draw(SpriteBatch batch, IReadOnlyList<string> lines, int screenWidth, int screenHeight)
    {
        return DrawPanel(batch, "DEBUG TELEMETRY", lines, screenWidth, screenHeight, topOffsetPixels: 0f);
    }

    /// <summary>Draws a diagnostics panel in the top-right corner, optionally below another panel.</summary>
    /// <param name="batch">The 2D batch, already begun under a 2D projection.</param>
    /// <param name="lines">The lines, top to bottom.</param>
    /// <param name="screenWidth">The back buffer's width in pixels.</param>
    /// <param name="screenHeight">The back buffer's height in pixels.</param>
    /// <param name="topOffsetPixels">Additional top offset in pixels for stacking below another panel.</param>
    /// <returns>The rendered panel height in pixels.</returns>
    public float DrawTopRight(
        SpriteBatch batch,
        IReadOnlyList<string> lines,
        int screenWidth,
        int screenHeight,
        float topOffsetPixels = 0f)
    {
        return DrawPanel(batch, "FRAME TIMING", lines, screenWidth, screenHeight, topOffsetPixels);
    }

    /// <summary>Gets the gap used when a second top-right diagnostics panel is stacked below the first.</summary>
    public static float PanelGapPixels(int screenWidth, int screenHeight) =>
        new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight)).Px(PanelGapDesign);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The face is the device's shared set, not this renderer's to free.
        _body = null;
        _theme = null;
    }

    private float DrawPanel(
        SpriteBatch batch,
        string title,
        IReadOnlyList<string> lines,
        int screenWidth,
        int screenHeight,
        float topOffsetPixels)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(lines);
        if (_theme is not { } theme)
        {
            return 0f;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        Panel panel = BuildPanel(theme, title, lines);
        panel.Measure(context, new UiVec2(context.SafeRect.Width, context.SafeRect.Height));

        float topInset = context.Px(InsetDesign) + MathF.Max(0f, topOffsetPixels);
        panel.Arrange(PositionPanel(
            context.SafeRect,
            panel.DesiredSize,
            horizontalInset: context.Px(InsetDesign),
            topInset: topInset,
            topRight: true));

        var painter = UiPainter.Create(batch, context);
        panel.Draw(painter);
        painter.StrokeRect(panel.Bounds, MathF.Max(1f, context.Px(1f)), theme.Border);
        return panel.Bounds.Height;
    }

    internal static UiRect PositionPanel(
        UiRect safeRect,
        UiVec2 size,
        float horizontalInset,
        float topInset,
        bool topRight)
    {
        float minX = safeRect.X;
        float maxX = MathF.Max(minX, safeRect.Right - size.X);
        float x = topRight
            ? safeRect.Right - size.X - horizontalInset
            : safeRect.X + horizontalInset;
        x = Math.Clamp(x, minX, maxX);

        float minY = safeRect.Y;
        float maxY = MathF.Max(minY, safeRect.Bottom - size.Y);
        float y = safeRect.Y + topInset;
        y = Math.Clamp(y, minY, maxY);

        return new UiRect(x, y, size.X, size.Y);
    }

    internal static Panel BuildPanel(UiTheme theme, string title, IReadOnlyList<string> lines)
    {
        var panel = new Panel
        {
            Background = theme.Panel,
            Width = PanelWidthDesign,
            Padding = new Thickness(PaddingHorizontalDesign, PaddingVerticalDesign),
            IsHitTestVisible = false,
        };

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = SpacingDesign,
            IsHitTestVisible = false,
        };
        panel.AddChild(stack);

        stack.AddChild(new Label(theme.Heading, title, theme.HeadingTextSize, theme.Accent)
        {
            Shadow = theme.Shadow,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        });

        stack.AddChild(new Panel
        {
            Background = theme.Accent.WithAlpha(190),
            Width = RuleWidthDesign,
            Height = RuleHeightDesign,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        });

        foreach (string line in lines)
        {
            stack.AddChild(new Label(theme.Body, line, theme.BodyTextSize, theme.Text)
            {
                Shadow = theme.Shadow,
                Wrap = true,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
            });
        }

        return panel;
    }
}
