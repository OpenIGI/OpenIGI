using OpenIGI.Core.Configuration;
using Xunit;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>
/// Covers the scrollback filter and the index map the console view draws from.
/// </summary>
public sealed class ConsoleLogFilterTests
{
    [Fact]
    public void A_fresh_filter_shows_everything_and_says_so()
    {
        var filter = new ConsoleLogFilter();

        Assert.False(filter.IsActive);
        Assert.True(filter.Matches(new ConsoleLine("x", ConsoleSeverity.Error)));
        Assert.True(filter.Matches(new ConsoleLine("y", ConsoleSeverity.Output)));
    }

    [Fact]
    public void Hiding_a_severity_hides_only_that_severity()
    {
        var filter = new ConsoleLogFilter();
        filter.SetSeverity(ConsoleSeverity.Output, false);

        Assert.True(filter.IsActive);
        Assert.False(filter.Matches(new ConsoleLine("x", ConsoleSeverity.Output)));
        Assert.True(filter.Matches(new ConsoleLine("x", ConsoleSeverity.Error)));
    }

    /// <summary>
    /// An empty category set means "no category filter". Reading it as "hide everything" would blank
    /// the console the moment a reader deselected their last category, which is the opposite of what
    /// deselecting means.
    /// </summary>
    [Fact]
    public void An_empty_category_set_shows_every_category()
    {
        var filter = new ConsoleLogFilter();

        Assert.True(filter.Matches(new ConsoleLine("x", ConsoleSeverity.Output, "Render")));
        Assert.True(filter.Matches(new ConsoleLine("x", ConsoleSeverity.Output, "Audio")));
    }

    [Fact]
    public void Naming_a_category_restricts_to_it()
    {
        var filter = new ConsoleLogFilter();
        filter.SetCategory("Render", true);

        Assert.True(filter.Matches(new ConsoleLine("x", ConsoleSeverity.Output, "Render")));
        Assert.False(filter.Matches(new ConsoleLine("x", ConsoleSeverity.Output, "Audio")));
    }

    [Fact]
    public void Search_matches_the_message_and_ignores_case()
    {
        var filter = new ConsoleLogFilter { Search = "cascade" };

        Assert.True(filter.Matches(new ConsoleLine("Built 4 CASCADES")));
        Assert.False(filter.Matches(new ConsoleLine("Loaded terrain")));
    }

    /// <summary>
    /// The category is carried beside the text precisely so that searching cannot accidentally match
    /// it — a search for "render" should find messages about rendering, not every line the renderer
    /// happened to write.
    /// </summary>
    [Fact]
    public void Search_does_not_match_the_category()
    {
        var filter = new ConsoleLogFilter { Search = "Render" };

        Assert.False(filter.Matches(new ConsoleLine("loaded terrain", ConsoleSeverity.Output, "Render")));
    }

    [Fact]
    public void Criteria_combine_with_and()
    {
        var filter = new ConsoleLogFilter { Search = "fog" };
        filter.SetCategory("Render", true);

        Assert.True(filter.Matches(new ConsoleLine("fog rebuilt", ConsoleSeverity.Output, "Render")));
        Assert.False(filter.Matches(new ConsoleLine("fog rebuilt", ConsoleSeverity.Output, "Audio")));
        Assert.False(filter.Matches(new ConsoleLine("terrain rebuilt", ConsoleSeverity.Output, "Render")));
    }

    [Fact]
    public void Reset_restores_everything()
    {
        var filter = new ConsoleLogFilter { Search = "x" };
        filter.SetSeverity(ConsoleSeverity.Output, false);
        filter.SetCategory("Render", true);

        filter.Reset();

        Assert.False(filter.IsActive);
        Assert.True(filter.Matches(new ConsoleLine("anything")));
    }

    // ---- The model's index map ----

    [Fact]
    public void An_unfiltered_model_shows_every_line_in_order()
    {
        var console = new ConsoleSystem(new CVarRegistry());
        var model = new ConsoleModel(console);
        console.Write("a");
        console.Write("b");

        Assert.Equal([0, 1], model.VisibleLineIndices);
    }

    [Fact]
    public void The_index_map_follows_the_filter()
    {
        var console = new ConsoleSystem(new CVarRegistry());
        var model = new ConsoleModel(console);
        console.Write(new ConsoleLine("keep", ConsoleSeverity.Error));
        console.Write(new ConsoleLine("drop", ConsoleSeverity.Output));
        console.Write(new ConsoleLine("keep too", ConsoleSeverity.Error));

        model.Filter.SetSeverity(ConsoleSeverity.Output, false);

        Assert.Equal([0, 2], model.VisibleLineIndices);
    }

    [Fact]
    public void A_line_written_after_filtering_is_still_filtered()
    {
        var console = new ConsoleSystem(new CVarRegistry());
        var model = new ConsoleModel(console);
        model.Filter.SetSeverity(ConsoleSeverity.Output, false);

        console.Write(new ConsoleLine("dropped", ConsoleSeverity.Output));
        console.Write(new ConsoleLine("kept", ConsoleSeverity.Error));

        Assert.Equal([1], model.VisibleLineIndices);
    }

    [Fact]
    public void Scrolling_counts_shown_lines_rather_than_hidden_ones()
    {
        var console = new ConsoleSystem(new CVarRegistry());
        var model = new ConsoleModel(console);
        for (int i = 0; i < 10; i++)
        {
            console.Write(new ConsoleLine($"line {i}", i < 4 ? ConsoleSeverity.Error : ConsoleSeverity.Output));
        }

        model.Filter.SetSeverity(ConsoleSeverity.Output, false);

        // Four lines survive the filter and two fit on screen, so the view can only scroll back twice.
        model.Scroll(100, visibleLines: 2);
        Assert.Equal(2, model.ScrollOffset);
    }
}
