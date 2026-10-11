using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.Rendering.Presets;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>
/// Covers the graphics and display pages' unapplied changes: rows that show a choice without making it,
/// until Apply writes them all or Discard forgets them.
/// </summary>
/// <remarks>
/// The reason it exists is the graphics API row: bound straight to its variable, choosing Direct3D 12 on
/// the way to choosing anything else rebuilt the game there and then. Nothing a staged row does may reach
/// the variable before <see cref="SettingsDraft.Commit"/>.
/// </remarks>
public sealed class SettingsDraftTests
{
    private enum Api
    {
        Vulkan,
        D3D12,
    }

    private static UiTheme Theme()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Barlow-Regular.ttf");
        return new UiTheme(UiFont.ForMeasurement(FontAtlasBuilder.Build(File.ReadAllBytes(path), 48f)));
    }

    private static (CVarRegistry Registry, NumericCVar<int> Shadows, EnumCVar<Api> Renderer) Registry()
    {
        var registry = new CVarRegistry();
        NumericCVar<int> shadows = CVar.Int("r_shadows", 2048, 0, 4096, CVarOptions.Archive, "Shadow map resolution.", registry);
        EnumCVar<Api> renderer = CVar.Choice("r_renderer", Api.Vulkan, CVarOptions.Archive, "Graphics API.", registry);
        return (registry, shadows, renderer);
    }

    [Fact]
    public void A_staged_row_shows_its_choice_and_leaves_the_variable_alone()
    {
        (CVarRegistry registry, _, EnumCVar<Api> renderer) = Registry();
        int changes = 0;
        renderer.Changed += (_, _) => changes++;
        using var draft = new SettingsDraft(registry);

        Dropdown row = CVarBinding.CreateChoiceControl(Theme(), draft.Find("r_renderer")!, "Graphics API", [("vulkan", "Vulkan"), ("d3d12", "Direct3D 12")]);
        row.SelectedIndex = 1;

        Assert.Equal("Direct3D 12", row.SelectedOption);
        Assert.Equal(Api.Vulkan, renderer.Value);
        Assert.Equal(0, changes);
        Assert.True(draft.HasChanges);
    }

    [Fact]
    public void Commit_writes_every_change_and_reports_what_each_held()
    {
        (CVarRegistry registry, NumericCVar<int> shadows, EnumCVar<Api> renderer) = Registry();
        using var draft = new SettingsDraft(registry);
        draft.Find("r_shadows")!.TrySetText("1024", out _);
        draft.Find("r_renderer")!.TrySetText("d3d12", out _);

        IReadOnlyList<(ICVar Variable, string Previous)> written = draft.Commit();

        Assert.Equal(1024, shadows.Value);
        Assert.Equal(Api.D3D12, renderer.Value);
        Assert.False(draft.HasChanges);
        Assert.Contains(written, change => change.Variable == shadows && change.Previous == "2048");
    }

    [Fact]
    public void Commit_writes_the_variables_asked_for_last_after_the_rest()
    {
        (CVarRegistry registry, NumericCVar<int> shadows, EnumCVar<Api> renderer) = Registry();
        using var draft = new SettingsDraft(registry);
        var order = new List<string>();
        shadows.Changed += (_, _) => order.Add("r_shadows");
        renderer.Changed += (_, _) => order.Add("r_renderer");

        // Chosen first, written last: the API change rebuilds the game, and should find the rest in force.
        draft.Find("r_renderer")!.TrySetText("d3d12", out _);
        draft.Find("r_shadows")!.TrySetText("1024", out _);
        draft.Commit(static variable => variable.Name == "r_renderer");

        Assert.Equal(["r_shadows", "r_renderer"], order);
    }

    [Fact]
    public void Discard_puts_every_row_back_on_its_variable()
    {
        (CVarRegistry registry, NumericCVar<int> shadows, _) = Registry();
        using var draft = new SettingsDraft(registry);
        Slider row = Assert.IsType<Slider>(CVarBinding.CreateControl(Theme(), draft.Find("r_shadows")!));

        row.Value = 512f;
        draft.Discard();

        Assert.Equal(2048f, row.Value);
        Assert.Equal(2048, shadows.Value);
        Assert.False(draft.HasChanges);
    }

    [Fact]
    public void Choosing_the_value_already_in_force_is_no_change()
    {
        (CVarRegistry registry, _, _) = Registry();
        using var draft = new SettingsDraft(registry);
        ICVar staged = draft.Find("r_shadows")!;

        staged.TrySetText("1024", out _);
        staged.TrySetText("2048", out _);

        Assert.False(draft.HasChanges);
    }

    [Fact]
    public void A_staged_value_reads_as_the_variable_would_hold_it()
    {
        (CVarRegistry registry, _, _) = Registry();
        using var draft = new SettingsDraft(registry);
        ICVar staged = draft.Find("r_shadows")!;

        // Clamped to the variable's range, exactly as assigning it would.
        staged.TrySetText("9000", out _);

        Assert.Equal("4096", staged.ValueText);
        Assert.True(staged.MatchesText("4096"));
        Assert.False(staged.TrySetText("lots", out string? reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void A_change_made_elsewhere_reaches_a_row_with_nothing_staged_over_it()
    {
        (CVarRegistry registry, NumericCVar<int> shadows, _) = Registry();
        using var draft = new SettingsDraft(registry);
        Slider row = Assert.IsType<Slider>(CVarBinding.CreateControl(Theme(), draft.Find("r_shadows")!));

        shadows.TrySetText("512", out _);

        Assert.Equal(512f, row.Value);
        Assert.False(draft.HasChanges);
    }

    [Fact]
    public void A_quality_group_stages_every_variable_under_it()
    {
        (CVarRegistry registry, NumericCVar<int> shadows, _) = Registry();
        CVar.Int("r_shadow_cascades", 3, 1, 4, CVarOptions.Archive, "Cascade count.", registry);
        var group = new QualityGroup(
            "Shadow Quality",
            "Sun shadows.",
            new QualityLevel("Low", ("r_shadows", "1024"), ("r_shadow_cascades", "2")),
            new QualityLevel("High", ("r_shadows", "2048"), ("r_shadow_cascades", "3")));
        using var draft = new SettingsDraft(registry);
        Dropdown row = QualityBinding.CreateControl(Theme(), draft.Find, group)!;

        row.SelectedIndex = 0;

        Assert.Equal("Low", row.SelectedOption);
        Assert.Equal(2048, shadows.Value);
        Assert.Equal(2, draft.Pending.Count);
    }

    [Fact]
    public void A_preset_is_recognised_from_the_draft_without_being_applied()
    {
        (CVarRegistry registry, NumericCVar<int> shadows, _) = Registry();
        var presets = new GraphicsPresetRegistry();
        presets.Define(shadows, "0", "1024", "2048", "4096");
        using var draft = new SettingsDraft(registry);

        foreach ((ICVar variable, string value) in presets.ValuesOf(GraphicsPreset.Ultra))
        {
            draft.Stage(variable).TrySetText(value, out _);
        }

        Assert.Equal(GraphicsPreset.Ultra, presets.Recognise(draft.Holds));
        Assert.Equal(GraphicsPreset.High, presets.Detect());
        Assert.Equal(2048, shadows.Value);
    }
}
