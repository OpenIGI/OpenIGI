using OpenIGI.Core.Configuration;
using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Host.DevTools;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI;
using Xunit;
using OpenIGI.Rendering.Headless;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Covers the variable browser, the watch panel and the world inspector through
/// <see cref="RecordingGraphicsDevice"/>, with no GL context.
/// </summary>
public sealed class PanelTests
{
    private enum Quality
    {
        Off,
        Low,
        High,
    }

    private static DevConsoleTheme Theme() =>
        new(new MonoFontSet(new RecordingGraphicsDevice(), UiFontLibrary.Mono));

    private static void Render(UiElement element, float width = 900f, float height = 500f)
    {
        var device = new RecordingGraphicsDevice();
        var context = new LayoutContext(width, height, reference: new UiVec2(width, height));
        element.Measure(context, new UiVec2(width, height));
        element.Arrange(new UiRect(0f, 0f, width, height));

        var batch = new SpriteBatch(device);
        var painter = UiPainter.Create(batch, context);
        batch.Begin();
        element.Draw(painter);
        batch.End();
    }

    // ---- The variable browser ----

    [Fact]
    public void The_browser_lists_the_registry_and_narrows_as_the_filter_is_typed()
    {
        var registry = new CVarRegistry();
        CVar.Int("r_fog_quality", 32, registry: registry);
        CVar.Int("r_shadows", 2, registry: registry);
        CVar.Bool("snd_mute", false, registry: registry);

        var browser = new CVarBrowserBody(registry, Theme());
        Assert.Equal(3, browser.Matches.Count);

        browser.FilterEdit.SetText("fog");
        browser.ApplyFilter();

        Assert.Equal("r_fog_quality", Assert.Single(browser.Matches).Name);
    }

    [Fact]
    public void Hidden_variables_are_not_listed()
    {
        var registry = new CVarRegistry();
        CVar.Int("r_secret", 1, flags: CVarOptions.Hidden, registry: registry);

        Assert.Empty(new CVarBrowserBody(registry, Theme()).Matches);
    }

    /// <summary>
    /// Cycling is the fastest thing the table can do, and the reason most visits to it are one
    /// keystroke rather than an edit.
    /// </summary>
    [Fact]
    public void A_switch_cycles_between_its_two_values()
    {
        var registry = new CVarRegistry();
        var vsync = CVar.Bool("r_vsync", true, registry: registry);
        var browser = new CVarBrowserBody(registry, Theme()) { SelectedIndex = 0 };

        Assert.True(browser.CycleSelected());
        Assert.False(vsync.Value);

        Assert.True(browser.CycleSelected());
        Assert.True(vsync.Value);
    }

    [Fact]
    public void An_enumeration_cycles_through_all_of_its_members()
    {
        var registry = new CVarRegistry();
        var quality = CVar.Choice("r_quality", Quality.Off, registry: registry);
        var browser = new CVarBrowserBody(registry, Theme()) { SelectedIndex = 0 };

        browser.CycleSelected();
        Assert.Equal(Quality.Low, quality.Value);

        browser.CycleSelected();
        Assert.Equal(Quality.High, quality.Value);

        browser.CycleSelected();
        Assert.Equal(Quality.Off, quality.Value);
    }

    [Fact]
    public void A_number_is_edited_in_place_rather_than_cycled()
    {
        var registry = new CVarRegistry();
        var shadows = CVar.Int("r_shadows", 2, 0, 4, registry: registry);
        var browser = new CVarBrowserBody(registry, Theme()) { SelectedIndex = 0 };

        Assert.False(browser.CycleSelected());
        Assert.True(browser.BeginEdit());
        Assert.Equal("2", browser.ValueEdit.Text);

        browser.ValueEdit.SetText("4");
        Assert.True(browser.CommitEdit());
        Assert.Equal(4, shadows.Value);
    }

    [Fact]
    public void A_rejected_value_keeps_the_edit_open_and_the_variable_unchanged()
    {
        var registry = new CVarRegistry();
        var shadows = CVar.Int("r_shadows", 2, 0, 4, registry: registry);
        var browser = new CVarBrowserBody(registry, Theme()) { SelectedIndex = 0 };

        browser.BeginEdit();
        browser.ValueEdit.SetText("not a number");

        Assert.False(browser.CommitEdit());
        Assert.True(browser.IsEditing);
        Assert.Equal(2, shadows.Value);
    }

    [Fact]
    public void Cancelling_an_edit_leaves_the_value_alone()
    {
        var registry = new CVarRegistry();
        var shadows = CVar.Int("r_shadows", 2, 0, 4, registry: registry);
        var browser = new CVarBrowserBody(registry, Theme()) { SelectedIndex = 0 };

        browser.BeginEdit();
        browser.ValueEdit.SetText("4");
        browser.CancelEdit();

        Assert.False(browser.IsEditing);
        Assert.Equal(2, shadows.Value);
    }

    [Fact]
    public void A_read_only_variable_refuses_to_be_edited_or_cycled()
    {
        var registry = new CVarRegistry();
        CVar.Bool("r_locked", true, flags: CVarOptions.ReadOnly, registry: registry);
        var browser = new CVarBrowserBody(registry, Theme()) { SelectedIndex = 0 };

        Assert.False(browser.BeginEdit());
        Assert.False(browser.CycleSelected());
    }

    [Fact]
    public void Resetting_restores_the_default()
    {
        var registry = new CVarRegistry();
        var shadows = CVar.Int("r_shadows", 2, 0, 4, registry: registry);
        shadows.Value = 4;

        var browser = new CVarBrowserBody(registry, Theme()) { SelectedIndex = 0 };
        Assert.True(browser.ResetSelected());
        Assert.Equal(2, shadows.Value);
        Assert.True(shadows.IsDefault);
    }

    [Fact]
    public void The_browser_draws_a_full_registry_without_complaint()
    {
        var registry = new CVarRegistry();
        for (int i = 0; i < 400; i++)
        {
            CVar.Int($"r_thing{i}", i, registry: registry);
        }

        var browser = new CVarBrowserBody(registry, Theme());
        Render(browser);

        Assert.Equal(400, browser.Matches.Count);
    }

    // ---- The world inspector ----

    private sealed class FakeWorld : IWorldInspector
    {
        public List<InspectableEntity> Entities { get; } = [];

        public int CameraMovedTo { get; private set; } = -1;

        public void Describe(string filter, List<InspectableEntity> into)
        {
            into.Clear();
            foreach (InspectableEntity entity in Entities)
            {
                if (filter.Length == 0 || entity.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    into.Add(entity);
                }
            }
        }

        public void Detail(int id, List<string> into)
        {
            into.Clear();
            into.Add($"detail for {id}");
        }

        public bool TryTeleportPlayerTo(int id) => true;

        public bool TryMoveCameraTo(int id)
        {
            CameraMovedTo = id;
            return true;
        }

        public bool TryProject(int id, out ScreenMarker marker)
        {
            marker = new ScreenMarker(100f, 100f, 12.5, true);
            return true;
        }
    }

    [Fact]
    public void The_inspector_says_so_when_no_world_is_attached()
    {
        var inspector = new InspectorBody(Theme());
        Render(inspector);

        Assert.False(inspector.HasWorld);
        Assert.Empty(inspector.Entities);
    }

    [Fact]
    public void An_attached_world_is_listed_and_filtered()
    {
        var world = new FakeWorld();
        world.Entities.Add(new InspectableEntity(0, "soldier", "Guard_1", default, "alive"));
        world.Entities.Add(new InspectableEntity(1, "soldier", "Sniper_2", default, "dead"));

        var inspector = new InspectorBody(Theme());
        inspector.Attach(world);
        Render(inspector);

        Assert.Equal(2, inspector.Entities.Count);

        inspector.FilterEdit.SetText("sniper");
        Render(inspector);

        Assert.Equal("Sniper_2", Assert.Single(inspector.Entities).Name);
    }

    /// <summary>
    /// Detaching has to clear what was listed. A panel still showing the last mission's soldiers after
    /// the world went away is worse than an empty one, because every row is a lie.
    /// </summary>
    [Fact]
    public void Detaching_the_world_empties_the_panel()
    {
        var world = new FakeWorld();
        world.Entities.Add(new InspectableEntity(0, "soldier", "Guard_1", default, "alive"));

        var inspector = new InspectorBody(Theme());
        inspector.Attach(world);
        Render(inspector);
        Assert.Single(inspector.Entities);

        inspector.Attach(null);
        Render(inspector);

        Assert.Empty(inspector.Entities);
        Assert.False(inspector.HasWorld);
    }

    [Fact]
    public void A_selection_beyond_the_list_is_clamped_rather_than_thrown()
    {
        var world = new FakeWorld();
        world.Entities.Add(new InspectableEntity(7, "soldier", "Guard_1", new Vector3d(1, 2, 3), "alive"));

        var inspector = new InspectorBody(Theme());
        inspector.Attach(world);
        Render(inspector);

        inspector.SelectedIndex = 99;
        Assert.Equal(0, inspector.SelectedIndex);
        Assert.Equal(7, inspector.Selected!.Value.Id);
    }

    // ---- The watch panel ----

    [Fact]
    public void The_watch_panel_draws_pinned_values_and_an_empty_state()
    {
        var registry = new CVarRegistry();
        CVar.Bool("r_vsync", true, registry: registry);
        var watch = new ConsoleWatchList();

        var body = new WatchBody(watch, registry, Theme());
        Render(body, 400f, 150f);

        watch.Add("r_vsync");
        Render(body, 400f, 150f);

        Assert.Single(watch.Names);
    }

    // ---- The command palette ----

    [Fact]
    public void The_palette_finds_commands_and_variables_but_not_values()
    {
        var registry = new CVarRegistry();
        var console = new ConsoleSystem(registry);
        CVar.Bool("r_vsync", true, registry: registry);
        console.Commands.Add("reload", "Reloads.", static _ => { });

        var palette = new CommandPaletteBody(console, Theme());
        palette.Query.SetText("r");
        palette.Refresh();

        Assert.Contains(palette.Matches, m => m.Text == "r_vsync");
        Assert.Contains(palette.Matches, m => m.Text == "reload");
        Assert.DoesNotContain(palette.Matches, m => m.Kind == ConsoleSuggestionKind.Value);
    }

    [Fact]
    public void The_palette_selection_wraps_at_both_ends()
    {
        var registry = new CVarRegistry();
        var console = new ConsoleSystem(registry);
        CVar.Bool("a_one", true, registry: registry);
        CVar.Bool("a_two", true, registry: registry);

        var palette = new CommandPaletteBody(console, Theme());
        palette.Query.SetText("a_");
        palette.Refresh();

        Assert.Equal(2, palette.Matches.Count);

        palette.SelectedIndex = -1;
        Assert.Equal(1, palette.SelectedIndex);

        palette.SelectedIndex = 2;
        Assert.Equal(0, palette.SelectedIndex);
    }
}
