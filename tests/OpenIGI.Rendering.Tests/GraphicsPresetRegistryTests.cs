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

    /// <summary>
    /// Every archived render setting has to be named by the presets, so that choosing one fully
    /// determines the picture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An archived setting outside the preset table keeps whatever it was last set to across a preset
    /// change, so two machines "on ultra" render differently and a capture does not reproduce what the
    /// player sees — silently, because nothing reports it. That is exactly how a session running four
    /// times the probe irradiance and nine times the occlusion radius of its own reference captures went
    /// unnoticed. A new archived <c>r_</c> variable therefore has to be given preset values or listed
    /// here with the reason it is a preference rather than a quality level.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryArchivedRenderSettingIsNamedByThePresets()
    {
        // Not quality levels: the first four are the scene's own state rather than how well it is drawn,
        // the next two are the player's own display and performance preferences, and the last names a
        // directory on their disk. A preset has no business overwriting any of them.
        string[] preferences =
        [
            "r_time_of_day", "r_weather_enabled", "r_weather_kind", "r_weather_speed",
            "r_vsync", "r_scale", "r_material_pack",
        ];

        _ = RenderCVars.Lighting;
        HashSet<string> managed = [.. GraphicsPresetRegistry.Default.Managed.Select(variable => variable.Name)];

        string[] unmanaged =
        [
            .. CVarRegistry.Default.All
                .Where(variable => variable.Name.StartsWith("r_", StringComparison.Ordinal))
                .Where(variable => variable.Flags.HasFlag(CVarOptions.Archive))
                .Select(variable => variable.Name)
                .Where(name => !managed.Contains(name) && !preferences.Contains(name))
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            unmanaged.Length == 0,
            "These archived render settings are outside every preset, so choosing a preset leaves them "
                + "wherever they were last set: " + string.Join(", ", unmanaged));
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
