using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>Covers the settings-row ↔ CVar binding: the right control per type, both ways, with reset.</summary>
public sealed class CVarBindingTests
{
    private enum Quality
    {
        Off,
        Low,
        High,
        Ultra,
    }

    private static UiTheme Theme()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Saira-Regular.ttf");
        return new UiTheme(UiFont.ForMeasurement(FontAtlasBuilder.Build(File.ReadAllBytes(path), 48f)));
    }

    [Fact]
    public void Humanize_drops_a_known_prefix_and_title_cases()
    {
        Assert.Equal("Vsync", CVarBinding.Humanize("r_vsync"));
        Assert.Equal("Music Volume", CVarBinding.Humanize("snd_music_volume"));
        Assert.Equal("Field Of View", CVarBinding.Humanize("field_of_view"));
        Assert.Equal("Remastered HUD", CVarBinding.Humanize("r_remastered_hud"));
    }

    [Fact]
    public void A_boolean_binds_to_a_toggle_both_ways()
    {
        var registry = new CVarRegistry();
        BoolCVar cvar = CVar.Bool("r_smooth", false, CVarOptions.Archive, "Smooth presentation", registry);

        var toggle = Assert.IsType<Toggle>(CVarBinding.CreateControl(Theme(), cvar));
        Assert.False(toggle.Value);

        // Widget → CVar
        toggle.Value = true;
        Assert.Equal("1", cvar.ValueText);

        // CVar → widget (e.g. a preset or reset writing it)
        cvar.TrySetText("0", out _);
        Assert.False(toggle.Value);

        cvar.Reset();
        Assert.False(toggle.Value);
    }

    [Fact]
    public void An_enum_binds_to_a_dropdown_over_its_choices()
    {
        var registry = new CVarRegistry();
        EnumCVar<Quality> cvar = CVar.Choice("r_gi", Quality.Low, CVarOptions.Archive, "GI quality", registry);

        var dropdown = Assert.IsType<Dropdown>(CVarBinding.CreateControl(Theme(), cvar));
        Assert.Equal(4, dropdown.Options.Count);
        Assert.Equal("low", dropdown.SelectedOption);

        dropdown.SelectedIndex = 3; // ultra
        Assert.Equal("ultra", cvar.ValueText);

        cvar.TrySetText("off", out _);
        Assert.Equal("off", dropdown.SelectedOption);
    }

    [Fact]
    public void A_bounded_number_binds_to_a_slider()
    {
        var registry = new CVarRegistry();
        NumericCVar<int> cvar = CVar.Int("r_msaa", 2, 0, 8, CVarOptions.Archive, "MSAA samples", registry);

        var slider = Assert.IsType<Slider>(CVarBinding.CreateControl(Theme(), cvar));
        Assert.Equal(0f, slider.Minimum, 3);
        Assert.Equal(8f, slider.Maximum, 3);
        Assert.Equal(2f, slider.Value, 3);

        slider.HandleNavigate(NavDirection.Right); // +1 step
        Assert.Equal("3", cvar.ValueText);

        cvar.TrySetText("6", out _);
        Assert.Equal(6f, slider.Value, 3);
    }

    [Fact]
    public void An_unbounded_number_has_no_control()
    {
        var registry = new CVarRegistry();
        NumericCVar<int> cvar = CVar.Int("r_seed", 0, null, null, CVarOptions.None, "Unbounded", registry);
        Assert.False(CVarBinding.CanBind(cvar));
        Assert.Null(CVarBinding.CreateControl(Theme(), cvar));
    }

    [Fact]
    public void Two_way_binding_does_not_feed_back_on_itself()
    {
        var registry = new CVarRegistry();
        NumericCVar<int> cvar = CVar.Int("r_msaa", 2, 0, 8, CVarOptions.Archive, "MSAA", registry);
        var slider = (Slider)CVarBinding.CreateControl(Theme(), cvar)!;

        int changes = 0;
        cvar.Changed += (_, _) => changes++;

        cvar.TrySetText("5", out _); // one external change
        Assert.Equal(1, changes);    // the widget update must not bounce back into another TrySetText
        Assert.Equal(5f, slider.Value, 3);
    }
}
