using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>
/// A value the settings read as but nobody can choose -- a quality group's Custom, a graphics API of auto -- is never offered
/// as a step.
/// </summary>
public sealed class SettingsReadingsTests
{
    private static UiTheme Theme() => new(OpenIGI.UI.Text.UiFont.ForMeasurement(
        OpenIGI.UI.Text.FontAtlasBuilder.Build(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Barlow-Regular.ttf")), 48f)));

    /// <summary>Custom is listed while the values are custom, and gone once they are a step again.</summary>
    [Fact]
    public void A_quality_groups_custom_is_offered_only_while_it_is_the_reading()
    {
        var cvars = new CVarRegistry();
        NumericCVar<int> ao = CVar.Int("r_test_ao", 0, 0, 8, CVarOptions.Archive, "AO.", cvars);
        var group = new QualityGroup("Ambient Occlusion", "Help.", new QualityLevel("Off", ("r_test_ao", "0")), new QualityLevel("On", ("r_test_ao", "4")));

        Dropdown row = QualityBinding.CreateControl(Theme(), cvars, group)!;
        int custom = row.Options.Count - 1;
        Assert.False(row.Offered(custom));

        ao.Value = 2;
        Assert.Equal(custom, row.SelectedIndex);
        Assert.True(row.Offered(custom));

        row.Step(1);
        Assert.NotEqual(custom, row.SelectedIndex);
        Assert.False(row.Offered(custom));
    }

    /// <summary>A stored value that stands for another of the choices is shown as that one, and adds no choice of its own.</summary>
    [Fact]
    public void A_stored_value_standing_for_another_reads_as_it()
    {
        var cvars = new CVarRegistry();
        StringCVar api = CVar.Text("r_test_api", "auto", CVarOptions.Archive, "API.", cvars);

        Dropdown row = CVarBinding.CreateChoiceControl(
            Theme(), api, "Graphics API", [("d3d12", "Direct3D 12"), ("vulkan", "Vulkan")], reads: stored => stored == "auto" ? "d3d12" : stored);

        Assert.Equal(["Direct3D 12", "Vulkan"], row.Options);
        Assert.Equal("Direct3D 12", row.SelectedOption);

        row.SelectedIndex = 1;
        Assert.Equal("vulkan", api.Value);
    }
}
