using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.UI.Tests.Editor;

/// <summary>The editor kit's drawing helpers.</summary>
public sealed class EditorDrawTests
{
    /// <summary>
    /// A frame with a see-through inside draws only its outline: nothing in its border colour reaches the middle. Drawing the
    /// border as a filled backdrop under the fill tinted the whole viewport whenever its focus outline showed.
    /// </summary>
    [Fact]
    public void A_see_through_frame_leaves_its_middle_alone()
    {
        RecordingUiRenderer renderer = new();
        UiPainter painter = new(renderer, new LayoutContext(400f, 300f));
        EditorTheme theme = new EditorHarness().Theme;
        Bgra32 border = Bgra32.FromRgba(250, 180, 40, 120);

        EditorDraw.Frame(painter, theme, new UiRect(0f, 0f, 200f, 100f), Bgra32.Transparent, border, 0f);

        Assert.NotEmpty(renderer.Quads);
        Assert.DoesNotContain(renderer.Quads, quad => quad.Corners.All(v => v.Color == border) && Covers(quad.Corners, 100f, 50f));
    }

    private static bool Covers(IReadOnlyList<UiVertex> corners, float x, float y) =>
        corners.Min(static v => v.X) < x && corners.Max(static v => v.X) > x
        && corners.Min(static v => v.Y) < y && corners.Max(static v => v.Y) > y;
}
