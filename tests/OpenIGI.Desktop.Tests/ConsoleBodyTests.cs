using OpenIGI.Core.Configuration;
using OpenIGI.Desktop;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Draws the developer console's contents through <see cref="RecordingGraphicsDevice"/> — no GL — to
/// cover the parts a screenshot cannot reach, since a capture run has no way to type.
/// </summary>
public sealed class ConsoleBodyTests
{
    private enum Quality
    {
        Off,
        Low,
        High,
    }

    private sealed record Harness(ConsoleSystem System, ConsoleModel Model, ConsoleBody Body, SpriteBatch Batch, UiPainter Painter);

    private static Harness Build(float width = 1000f, float height = 500f)
    {
        var device = new RecordingGraphicsDevice();

        var registry = new CVarRegistry();
        var system = new ConsoleSystem(registry);
        var model = new ConsoleModel(system);
        var theme = new DevConsoleTheme(new MonoFontSet(device, UiFontLibrary.Mono));
        var body = new ConsoleBody(model, theme);

        var context = new LayoutContext(width, height, reference: new UiVec2(width, height));
        body.Measure(context, new UiVec2(width, height));
        body.Arrange(new UiRect(0f, 0f, width, height));

        var batch = new SpriteBatch(device);
        return new Harness(system, model, body, batch, new UiPainter(batch, context));
    }

    private static void Render(Harness h)
    {
        h.Batch.Begin();
        h.Body.Draw(h.Painter);
        h.Batch.End();
    }

    [Fact]
    public void An_empty_console_draws_its_prompt_and_no_dropdown()
    {
        Harness h = Build();
        Render(h);

        Assert.True(h.Body.DropdownRect.IsEmpty);
        Assert.True(h.Batch.QuadCount > 0);
    }

    [Fact]
    public void Typing_a_prefix_opens_the_dropdown()
    {
        Harness h = Build();
        CVar.Int("r_fog_quality", 32, 0, 128, CVarOptions.None, "Samples per fog ray.", h.System.Variables);
        CVar.Int("r_fog_height", 24, registry: h.System.Variables);

        h.Model.SetInput("r_fog");
        Render(h);

        Assert.Equal(2, h.Model.RichSuggestions.Count);
        Assert.False(h.Body.DropdownRect.IsEmpty);
    }

    /// <summary>
    /// The dropdown floats over the window's own children, so the shell has to be able to ask which
    /// row a click landed on before it offers the press to anything beneath.
    /// </summary>
    [Fact]
    public void A_point_in_the_dropdown_resolves_to_a_row()
    {
        Harness h = Build();
        CVar.Int("r_fog_quality", 32, registry: h.System.Variables);
        CVar.Int("r_fog_height", 24, registry: h.System.Variables);

        h.Model.SetInput("r_fog");
        Render(h);

        UiRect rect = h.Body.DropdownRect;
        Assert.Equal(0, h.Body.SuggestionAt(new UiVec2(rect.X + 5f, rect.Y + 2f)));
        Assert.Equal(-1, h.Body.SuggestionAt(new UiVec2(rect.X - 50f, rect.Y - 50f)));
    }

    [Fact]
    public void Value_completion_shows_once_a_variable_has_been_named()
    {
        Harness h = Build();
        CVar.Choice("r_quality", Quality.Low, registry: h.System.Variables);

        h.Model.SetInput("r_quality ");
        Render(h);

        Assert.Equal(3, h.Model.RichSuggestions.Count);
        Assert.All(h.Model.RichSuggestions, s => Assert.Equal(ConsoleSuggestionKind.Value, s.Kind));
        Assert.False(h.Body.DropdownRect.IsEmpty);
    }

    [Fact]
    public void Accepting_a_value_keeps_the_variable_it_belongs_to()
    {
        Harness h = Build();
        CVar.Choice("r_quality", Quality.Low, registry: h.System.Variables);

        h.Model.SetInput("r_quality hi");
        Assert.True(h.Model.SelectSuggestion(0));
        Assert.True(h.Model.AcceptSuggestion());

        Assert.Equal("r_quality high", h.Model.Input);
    }

    [Fact]
    public void Accepting_a_name_leaves_a_space_ready_for_its_value()
    {
        Harness h = Build();
        CVar.Int("r_fog_quality", 32, registry: h.System.Variables);

        h.Model.SetInput("r_fog");
        Assert.True(h.Model.SelectSuggestion(0));
        Assert.True(h.Model.AcceptSuggestion());

        Assert.Equal("r_fog_quality ", h.Model.Input);
    }

    /// <summary>
    /// The property the virtualised log exists for, asserted where it actually matters: a full
    /// scrollback must not cost more to draw than a short one.
    /// </summary>
    [Fact]
    public void A_full_scrollback_costs_about_what_a_screenful_costs()
    {
        Harness few = Build();
        for (int i = 0; i < 5; i++)
        {
            few.System.Write($"line {i}");
        }

        Render(few);
        int fewQuads = few.Batch.QuadCount;

        Harness many = Build();
        for (int i = 0; i < 4000; i++)
        {
            many.System.Write($"line {i}");
        }

        many.Body.Log.SnapToEnd();
        Render(many);

        // Eight hundred times the content, well under twice the draw calls.
        Assert.True(
            many.Batch.QuadCount < fewQuads * 2 + 400,
            $"4000 lines drew {many.Batch.QuadCount} quads against {fewQuads} for five.");
    }

    // ---- Scrolling ----

    /// <summary>
    /// The bug this covers: the model counts lines back from the newest and the viewport counts pixels
    /// from the top, and nothing converted between them — so scrolling changed a number nothing read
    /// and the log sat still.
    /// </summary>
    [Fact]
    public void Scrolling_the_model_moves_the_viewport()
    {
        Harness h = Build();
        for (int i = 0; i < 200; i++)
        {
            h.System.Write($"line {i}");
        }

        Render(h);
        Assert.True(h.Body.Log.StickToEnd);

        h.Model.Scroll(20, h.Body.VisibleLines);
        Render(h);

        // Twenty lines back from the newest is twenty row-heights short of the bottom, and the
        // viewport stops following the end.
        Assert.False(h.Body.Log.StickToEnd);
        Assert.Equal(
            h.Body.Log.MaxScroll - (20f * h.Body.Log.RowHeight),
            h.Body.Log.ScrollOffset,
            1);
    }

    [Fact]
    public void Returning_to_the_bottom_sticks_to_the_end_again()
    {
        Harness h = Build();
        for (int i = 0; i < 200; i++)
        {
            h.System.Write($"line {i}");
        }

        h.Model.Scroll(20, h.Body.VisibleLines);
        Render(h);
        Assert.False(h.Body.Log.StickToEnd);

        h.Model.Scroll(-1000, h.Body.VisibleLines);
        Render(h);

        Assert.True(h.Body.Log.StickToEnd);
        Assert.Equal(0, h.Model.ScrollOffset);
    }

    [Fact]
    public void A_full_log_overflows_and_offers_a_scrollbar()
    {
        Harness h = Build();
        for (int i = 0; i < 500; i++)
        {
            h.System.Write($"line {i}");
        }

        Render(h);

        Assert.True(h.Body.Log.HasOverflow);
        Assert.False(h.Body.Log.ThumbBounds.IsEmpty);
    }

    [Fact]
    public void A_short_log_has_no_scrollbar_to_grab()
    {
        Harness h = Build();
        h.System.Write("one line");

        Render(h);

        Assert.False(h.Body.Log.HasOverflow);
        Assert.False(h.Body.BeginScrollDrag(new UiVec2(h.Body.Log.Bounds.Right - 4f, h.Body.Log.Bounds.Y + 10f)));
    }

    /// <summary>
    /// Dragging the thumb has to end up expressed in the model's units, because that is what every
    /// other scrolling path reads.
    /// </summary>
    [Fact]
    public void Dragging_the_thumb_scrolls_and_tells_the_model()
    {
        Harness h = Build();
        for (int i = 0; i < 500; i++)
        {
            h.System.Write($"line {i}");
        }

        Render(h);

        UiRect track = h.Body.Log.TrackBounds;
        Assert.True(h.Body.BeginScrollDrag(new UiVec2(track.X + 2f, track.Y + 4f)));
        Assert.True(h.Body.IsScrollDragging);

        Assert.True(h.Model.ScrollOffset > 0, "dragging the thumb to the top should scroll back.");

        h.Body.EndScrollDrag();
        Assert.False(h.Body.IsScrollDragging);
    }

    [Fact]
    public void A_filtered_log_draws_only_what_passes()
    {
        Harness h = Build();
        h.System.Write(new ConsoleLine("kept", ConsoleSeverity.Error, "Render"));
        h.System.Write(new ConsoleLine("dropped", ConsoleSeverity.Output, "Audio"));

        h.Model.Filter.SetCategory("Render", true);
        Render(h);

        Assert.Single(h.Model.VisibleLineIndices);
    }

    [Fact]
    public void Drawing_survives_a_window_squeezed_to_nothing()
    {
        Harness h = Build(width: 40f, height: 30f);
        h.System.Write("a line that is far wider than the window it is in");
        h.Model.SetInput("r_");

        Render(h);

        Assert.True(h.Batch.QuadCount >= 0);
    }
}
