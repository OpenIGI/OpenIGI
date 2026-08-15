using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.UI.Tests;

/// <summary>
/// Covers the row-virtualised list the console's log and variable browser are built on.
/// </summary>
public sealed class VirtualListViewTests
{
    private static UiTheme Theme(RecordingGraphicsDevice device)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "JetBrainsMono-Regular.ttf");
        UiFont font = UiFont.Upload(device, FontAtlasBuilder.Build(File.ReadAllBytes(path), 32f));
        return new UiTheme(font, null, font);
    }

    private static (VirtualListView List, List<int> Drawn) Build(int rows, float viewportHeight)
    {
        var device = new RecordingGraphicsDevice();
        var list = new VirtualListView(Theme(device))
        {
            RowCount = rows,
            RowHeight = 20f,
            ShowScrollBar = false,
            StickToEnd = false,
        };

        List<int> drawn = [];
        list.DrawRow = (painter, row, rect) =>
        {
            drawn.Add(row);
            painter.FillRect(rect, OpenIGI.Core.Graphics.Bgra32.White);
        };

        var context = new LayoutContext(1000f, viewportHeight, reference: new UiVec2(1000f, viewportHeight));
        list.Measure(context, new UiVec2(1000f, viewportHeight));
        list.Arrange(new UiRect(0f, 0f, 1000f, viewportHeight));

        var batch = new SpriteBatch(device);
        var painter = new UiPainter(batch, context);
        batch.Begin();
        list.Draw(painter);
        batch.End();

        return (list, drawn);
    }

    /// <summary>
    /// The property the whole widget exists for. A console that measured four thousand rows to show
    /// twenty of them would spend the frame budget it was built to report.
    /// </summary>
    [Fact]
    public void A_huge_list_draws_only_the_rows_that_fit()
    {
        (VirtualListView list, List<int> drawn) = Build(rows: 4096, viewportHeight: 400f);

        // Twenty rows fit; one extra may straddle the bottom edge.
        Assert.InRange(drawn.Count, 20, 22);
        Assert.Equal(4096, list.RowCount);
    }

    [Fact]
    public void The_rows_drawn_are_the_ones_scrolled_to()
    {
        (VirtualListView list, List<int> drawn) = Build(rows: 4096, viewportHeight: 400f);
        Assert.Equal(0, drawn[0]);

        list.SnapTo(1000f);

        (_, List<int> after) = Redraw(list);
        Assert.Equal(50, after[0]);
    }

    [Fact]
    public void A_list_shorter_than_its_viewport_draws_every_row()
    {
        (_, List<int> drawn) = Build(rows: 5, viewportHeight: 400f);
        Assert.Equal([0, 1, 2, 3, 4], drawn);
    }

    [Fact]
    public void An_empty_list_draws_nothing()
    {
        (_, List<int> drawn) = Build(rows: 0, viewportHeight: 400f);
        Assert.Empty(drawn);
    }

    [Fact]
    public void Scrolling_is_clamped_to_the_content()
    {
        (VirtualListView list, _) = Build(rows: 100, viewportHeight: 400f);

        list.ScrollOffset = -500f;
        Assert.Equal(0f, list.ScrollOffset);

        list.ScrollOffset = 999999f;
        Assert.Equal(list.MaxScroll, list.ScrollOffset);
        Assert.Equal((100 * 20f) - 400f, list.MaxScroll);
    }

    [Fact]
    public void A_row_can_be_found_under_a_point()
    {
        (VirtualListView list, _) = Build(rows: 100, viewportHeight: 400f);

        Assert.Equal(0, list.RowAt(new UiVec2(10f, 5f)));
        Assert.Equal(3, list.RowAt(new UiVec2(10f, 70f)));
        Assert.Equal(-1, list.RowAt(new UiVec2(10f, 5000f)));
    }

    [Fact]
    public void Ensure_visible_reveals_a_row_by_the_shortest_move()
    {
        (VirtualListView list, _) = Build(rows: 100, viewportHeight: 400f);

        list.EnsureVisible(50);
        Assert.Equal((51 * 20f) - 400f, list.ScrollOffset);

        list.EnsureVisible(0);
        Assert.Equal(0f, list.ScrollOffset);
    }

    /// <summary>
    /// A log should follow new output, which means the offset has to track a content height that grows
    /// underneath it rather than staying where it was.
    /// </summary>
    [Fact]
    public void Sticking_to_the_end_follows_rows_as_they_arrive()
    {
        var device = new RecordingGraphicsDevice();
        var list = new VirtualListView(Theme(device))
        {
            RowCount = 10,
            RowHeight = 20f,
            StickToEnd = true,
        };

        var context = new LayoutContext(1000f, 200f, reference: new UiVec2(1000f, 200f));
        list.Measure(context, new UiVec2(1000f, 200f));
        list.Arrange(new UiRect(0f, 0f, 1000f, 200f));

        list.Update(1f);
        Assert.Equal(0f, list.ScrollOffset);

        list.RowCount = 100;
        list.Update(1f);
        Assert.Equal((100 * 20f) - 200f, list.ScrollOffset);
    }

    private static (VirtualListView List, List<int> Drawn) Redraw(VirtualListView list)
    {
        var device = new RecordingGraphicsDevice();
        List<int> drawn = [];
        Action<UiPainter, int, UiRect>? previous = list.DrawRow;
        list.DrawRow = (painter, row, rect) =>
        {
            drawn.Add(row);
            painter.FillRect(rect, OpenIGI.Core.Graphics.Bgra32.White);
        };

        var context = new LayoutContext(1000f, list.Bounds.Height, reference: new UiVec2(1000f, list.Bounds.Height));
        var batch = new SpriteBatch(device);
        var painter = new UiPainter(batch, context);
        batch.Begin();
        list.Draw(painter);
        batch.End();

        list.DrawRow = previous;
        return (list, drawn);
    }
}
