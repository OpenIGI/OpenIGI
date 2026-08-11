using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Protects the shared-widget composition and safe-area anchoring of the diagnostics surface.</summary>
public sealed class DebugOverlayRendererTests
{
    [Theory]
    [InlineData(640f, 480f)]
    [InlineData(1920f, 1080f)]
    [InlineData(3440f, 1440f)]
    public void Panel_uses_shared_widgets_and_keeps_the_same_safe_inset(float width, float height)
    {
        using UiFont font = TestFont();
        var theme = new UiTheme(font)
        {
            BodyTextSize = 16f,
            HeadingTextSize = 20f,
        };
        string[] lines = ["mode: WALK", "pos units (120, 240, 360)"];

        Panel panel = DebugOverlayRenderer.BuildPanel(theme, "DEBUG TELEMETRY", lines);
        var context = new LayoutContext(width, height);
        panel.Measure(context, context.SafeRect.Size);
        panel.Arrange(DebugOverlayRenderer.PositionPanel(
            context.SafeRect,
            panel.DesiredSize,
            horizontalInset: context.Px(18f),
            topInset: context.Px(18f),
            topRight: true));

        Assert.IsType<StackPanel>(panel.Children[0]);
        StackPanel stack = (StackPanel)panel.Children[0];
        Assert.Equal(lines.Length + 2, stack.Children.Count);
        Assert.IsType<Label>(stack.Children[0]);
        Assert.IsType<Panel>(stack.Children[1]);
        Assert.All(stack.Children.Skip(2), child => Assert.IsType<Label>(child));
        Assert.Equal(theme.Panel, panel.Background);
        Assert.Equal(
            context.SafeRect.Right - panel.Bounds.Width - context.Px(18f),
            panel.Bounds.X,
            0.01f);
        Assert.Equal(context.SafeRect.Y + context.Px(18f), panel.Bounds.Y, 0.01f);
    }

    [Fact]
    public void Top_right_panels_can_stack_without_overlapping()
    {
        var safeRect = new UiRect(20f, 12f, 1800f, 1000f);
        var firstSize = new UiVec2(340f, 220f);
        var secondSize = new UiVec2(420f, 180f);

        UiRect first = DebugOverlayRenderer.PositionPanel(
            safeRect,
            firstSize,
            horizontalInset: 18f,
            topInset: 18f,
            topRight: true);
        UiRect second = DebugOverlayRenderer.PositionPanel(
            safeRect,
            secondSize,
            horizontalInset: 18f,
            topInset: 18f + first.Height + 12f,
            topRight: true);

        Assert.Equal(safeRect.Right - firstSize.X - 18f, first.X);
        Assert.Equal(safeRect.Y + 18f, first.Y);
        Assert.Equal(firstSize, first.Size);
        Assert.Equal(safeRect.Right - secondSize.X - 18f, second.X);
        Assert.Equal(first.Bottom + 12f, second.Y);
        Assert.Equal(secondSize, second.Size);
    }

    private static UiFont TestFont()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Saira-Regular.ttf");
        return UiFont.ForMeasurement(FontAtlasBuilder.Build(File.ReadAllBytes(path), pixelHeight: 48f));
    }
}
