using OpenIGI.Core.Configuration;
using OpenIGI.Rendering.Presets;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins how a graphics preset is applied, detected, and lost when the player takes over.
/// </summary>
public sealed class GraphicsPresetRegistryTests
{
    [Fact]
    public void UltraKeepsAuthoredLightingUntilProbeIrradianceExists()
    {
        _ = RenderCVars.Lighting;

        GraphicsPresetRegistry.Default.Apply(GraphicsPreset.Ultra);

        Assert.Equal(LightingModel.Baked, RenderCVars.Lighting.Value);
    }

    private readonly CVarRegistry _variables = new();

    [Fact]
    public void Apply_AssignsEveryVariableThePresetNames()
    {
        GraphicsPresetRegistry presets = new();
        NumericCVar<int> fog = Declare(presets, "r_fog_quality", 3, "0", "1", "3", "4");
        NumericCVar<int> shadows = Declare(presets, "r_shadows", 2, "0", "1", "2", "3");

        int applied = presets.Apply(GraphicsPreset.Ultra);

        Assert.Equal(2, applied);
        Assert.Equal(4, fog.Value);
        Assert.Equal(3, shadows.Value);
        Assert.Equal(GraphicsPreset.Ultra, presets.Current);
    }

    [Fact]
    public void Apply_SkipsVariablesAPresetLeavesAlone()
    {
        GraphicsPresetRegistry presets = new();
        NumericCVar<int> managed = CVar.Int("r_test_partial", 5, 0, 9, registry: _variables);
        presets.Define(managed, vanilla: "0", enhanced: null, high: null, ultra: "9");

        Assert.Equal(0, presets.Apply(GraphicsPreset.Enhanced));
        Assert.Equal(5, managed.Value);

        Assert.Equal(1, presets.Apply(GraphicsPreset.Ultra));
        Assert.Equal(9, managed.Value);
    }

    [Fact]
    public void ChangingAManagedVariableByHandMakesThePresetCustom()
    {
        GraphicsPresetRegistry presets = new();
        NumericCVar<int> fog = Declare(presets, "r_fog_quality", 3, "0", "1", "3", "4");

        presets.Apply(GraphicsPreset.High);
        Assert.Equal(GraphicsPreset.High, presets.Current);

        fog.Value = 2;

        // Nothing is silently overridden: the preset stops claiming to describe the settings.
        Assert.Equal(GraphicsPreset.Custom, presets.Current);
    }

    [Fact]
    public void Detect_RecognisesSettingsLoadedFromAFile()
    {
        GraphicsPresetRegistry presets = new();
        NumericCVar<int> fog = Declare(presets, "r_fog_quality", 3, "0", "1", "3", "4");
        NumericCVar<int> shadows = Declare(presets, "r_shadows", 2, "0", "1", "2", "3");

        // As if settings.cfg had just restored them: assigned individually, not through Apply.
        fog.Value = 1;
        shadows.Value = 1;

        Assert.Equal(GraphicsPreset.Enhanced, presets.Detect());
    }

    [Fact]
    public void Detect_ReportsCustomWhenNothingMatches()
    {
        GraphicsPresetRegistry presets = new();
        NumericCVar<int> fog = Declare(presets, "r_fog_quality", 3, "0", "1", "3", "4");
        Declare(presets, "r_shadows", 2, "0", "1", "2", "3");

        fog.Value = 2;

        Assert.Equal(GraphicsPreset.Custom, presets.Detect());
    }

    [Fact]
    public void PresetChanged_FiresOnceForEachTransition()
    {
        GraphicsPresetRegistry presets = new();
        NumericCVar<int> fog = Declare(presets, "r_fog_quality", 3, "0", "1", "3", "4");
        List<GraphicsPreset> seen = [];
        presets.PresetChanged += (_, preset) => seen.Add(preset);

        presets.Apply(GraphicsPreset.High);
        presets.Apply(GraphicsPreset.High);
        fog.Value = 2;

        Assert.Equal([GraphicsPreset.High, GraphicsPreset.Custom], seen);
    }

    [Fact]
    public void ApplyingCustomDoesNothing()
    {
        GraphicsPresetRegistry presets = new();
        NumericCVar<int> fog = Declare(presets, "r_fog_quality", 3, "0", "1", "3", "4");
        presets.Apply(GraphicsPreset.Vanilla);

        Assert.Equal(0, presets.Apply(GraphicsPreset.Custom));
        Assert.Equal(0, fog.Value);
        Assert.Equal(GraphicsPreset.Vanilla, presets.Current);
    }

    private NumericCVar<int> Declare(
        GraphicsPresetRegistry presets,
        string name,
        int defaultValue,
        string vanilla,
        string enhanced,
        string high,
        string ultra) =>
        presets.Define(
            CVar.Int(name, defaultValue, 0, 9, CVarOptions.Archive, registry: _variables),
            vanilla,
            enhanced,
            high,
            ultra);
}
