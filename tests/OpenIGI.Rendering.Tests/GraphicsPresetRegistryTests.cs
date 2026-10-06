using OpenIGI.Core.Configuration;
using OpenIGI.Rendering.Presets;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins how a graphics preset is applied, detected, and lost when the player takes over.
/// </summary>
[Collection(SharedPresetRegistry.Name)]
public sealed class GraphicsPresetRegistryTests
{
    /// <summary>
    /// Vanilla alone keeps the artists' bakes; every preset above it lights in real time, and does so
    /// with the whole real-time stack rather than a fraction of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This assertion has run two ways before. First that ultra held <see cref="LightingModel.Baked"/>
    /// "until probe irradiance exists"; then, once it did, that ultra was the ONE preset that had moved.
    /// Both were pins on how far the work had got, not on what the presets are for. Enhanced and high now
    /// light in real time too, so what is worth pinning is the shape the tiers actually have: the same
    /// systems everywhere, differing only in budget.
    /// </para>
    /// <para>
    /// The second half is the half that bites. A preset can be set to <c>dynamic</c> and still be broken,
    /// because dynamic lighting DISCARDS the bakes and has nothing to put in their place unless the
    /// surface cache, the distance field, the gather and the lamps are all on together — a gather with no
    /// cache to trace returns nothing, and a dynamic preset with no lamps renders a windowless room black.
    /// Half-enabling the stack on a tier is therefore the likely mistake, and it looks like a lighting bug
    /// rather than a preset one.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnlyVanillaKeepsTheBakes()
    {
        _ = RenderCVars.Lighting;

        GraphicsPresetRegistry.Default.Apply(GraphicsPreset.Vanilla);
        Assert.Equal(LightingModel.Baked, RenderCVars.Lighting.Value);

        foreach (GraphicsPreset preset in (ReadOnlySpan<GraphicsPreset>)
            [GraphicsPreset.Enhanced, GraphicsPreset.High, GraphicsPreset.Ultra])
        {
            GraphicsPresetRegistry.Default.Apply(preset);

            Assert.Equal(LightingModel.Dynamic, RenderCVars.Lighting.Value);

            // Everything the dynamic model needs in order to have anything to light with.
            Assert.True(RenderCVars.SurfaceCache.Value, $"{preset} lights dynamically with no surface cache");
            Assert.True(RenderCVars.DistanceFields.Value, $"{preset} lights dynamically with no distance field");
            Assert.True(RenderCVars.ScreenGather.Value > 0f, $"{preset} lights dynamically with no screen gather");
            Assert.True(RenderCVars.RadianceCache.Value != 0, $"{preset} lights dynamically with no radiance cache");
            Assert.True(RenderCVars.LocalLightScale.Value > 0f, $"{preset} lights dynamically with no lamps");
        }
    }

    /// <summary>
    /// Only vanilla has no flashlight: the original has none, and a vanilla run is the original.
    /// </summary>
    [Fact]
    public void OnlyVanillaHasNoFlashlight()
    {
        _ = RenderCVars.Flashlight;

        GraphicsPresetRegistry.Default.Apply(GraphicsPreset.Vanilla);
        Assert.False(RenderCVars.Flashlight.Value, "vanilla must not have a flashlight");
        Assert.Equal(0, RenderCVars.FlashlightShadows.Value);

        foreach (GraphicsPreset preset in (ReadOnlySpan<GraphicsPreset>)
            [GraphicsPreset.Enhanced, GraphicsPreset.High, GraphicsPreset.Ultra])
        {
            GraphicsPresetRegistry.Default.Apply(preset);

            Assert.True(RenderCVars.Flashlight.Value, $"{preset} has no flashlight");
            Assert.Equal(1, RenderCVars.FlashlightShadows.Value);
            Assert.True(RenderCVars.FlashlightFog.Value > 0f, $"{preset} has no flashlight beam in the air");
        }
    }

    /// <summary>
    /// Only vanilla is drawn the original's rain; every preset above it gets the remastered storm.
    /// </summary>
    /// <remarks>
    /// The two rain renderers are chosen between by <c>r_storm</c> alone, so this one variable is the
    /// whole of "which rain does this player see". Vanilla means the shipped band of thin vertical
    /// streaks -- it is the regression gate, and it has to stay that -- and every tier above it means
    /// the particle storm: wind, shelter, splashes and wet ground. A tier that quietly lost the storm
    /// would show 1998's rain in the middle of an otherwise modern picture, in the three retail missions
    /// that rain, and nothing would report it.
    /// </remarks>
    [Fact]
    public void OnlyVanillaDrawsTheOriginalsRain()
    {
        _ = RenderCVars.Storm;

        GraphicsPresetRegistry.Default.Apply(GraphicsPreset.Vanilla);
        Assert.False(RenderCVars.Storm.Value, "vanilla has to keep the original's rain band");

        foreach (GraphicsPreset preset in (ReadOnlySpan<GraphicsPreset>)
            [GraphicsPreset.Enhanced, GraphicsPreset.High, GraphicsPreset.Ultra])
        {
            GraphicsPresetRegistry.Default.Apply(preset);
            Assert.True(RenderCVars.Storm.Value, $"{preset} draws the original's rain band, not the storm");
        }
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
        // Not quality levels: the first six are the scene's own state rather than how well it is drawn,
        // r_vsync is the player's own display preference, and the last three name a directory on their
        // disk. A preset has no business overwriting any of them.
        //
        // The two pack paths especially. They point at assets a player built for themselves with a tool,
        // from their own installation, because this project cannot ship the game's art -- so a preset
        // clearing one would silently throw away work that took an hour and a graphics card, and there is
        // no quality tier at which "forget where the player put their textures" is the right answer.
        //
        // r_scale used to be listed here and no longer is. It is the internal resolution the temporal
        // upscaler reconstructs from, which is the single largest difference between the quality tiers,
        // so it is a quality level in the fullest sense -- and it is now named by all four presets.
        //
        // r_day_night_cycle joins them for the same reason the weather does: it is a choice about what
        // the world is doing while you play it, not about how well the world is drawn. A preset naming it
        // would mean picking Ultra silently started the clock, or picking Vanilla silently stopped it.
        // r_lamp_daylight with it: whether the lamps follow that clock is the world's behaviour too.
        string[] preferences =
        [
            "r_weather_enabled", "r_weather_kind", "r_weather_speed",
            "r_storm_intensity", "r_weather_force", "r_day_night_cycle", "r_day_length", "r_lamp_daylight",
            "r_vsync", "r_world_briefing",

            // How worn the flashlight's switch is -- how often it chatters before it catches. A property of
            // the torch the player carries, kept whatever tier draws it; a preset resetting it would be
            // swapping their torch for a new one.
            "r_flashlight_wear",

            // NVIDIA's features. They need an optional runtime and an RTX adapter, so a preset writing one would
            // be writing something many machines it runs on cannot do; and a player who chose DLSS or a frame
            // cap keeps it whatever tier they pick. Each falls back to the engine's own path, and says why,
            // where it cannot run.
            "r_reconstruction", "r_dlss_quality", "r_frame_generation", "r_low_latency", "r_frame_limit",
            "r_dlssg_ui_recomposition",
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

    [Fact]
    public void Lock_AppliesThePresetOverWhateverWasRestored()
    {
        GraphicsPresetRegistry presets = new();
        NumericCVar<int> fog = Declare(presets, "r_fog_quality", 3, "0", "1", "3", "4");

        // As if settings.cfg had restored a saved Ultra.
        fog.Value = 4;

        presets.Lock(GraphicsPreset.Vanilla);

        Assert.Equal(0, fog.Value);
        Assert.Equal(GraphicsPreset.Vanilla, presets.Current);
        Assert.Equal(GraphicsPreset.Vanilla, presets.LockedPreset);
    }

    [Fact]
    public void Lock_RefusesEveryOtherPreset()
    {
        GraphicsPresetRegistry presets = new();
        NumericCVar<int> fog = Declare(presets, "r_fog_quality", 3, "0", "1", "3", "4");
        presets.Lock(GraphicsPreset.Vanilla);

        foreach (GraphicsPreset preset in (ReadOnlySpan<GraphicsPreset>)
            [GraphicsPreset.Enhanced, GraphicsPreset.High, GraphicsPreset.Ultra])
        {
            Assert.False(presets.IsAllowed(preset));
            Assert.Throws<InvalidOperationException>(() => presets.Apply(preset));
            Assert.Throws<InvalidOperationException>(() => presets.Lock(preset));
        }

        Assert.Equal(0, fog.Value);
        Assert.Equal(GraphicsPreset.Vanilla, presets.Current);
    }

    [Fact]
    public void Lock_StillAllowsTheLockedPresetBack()
    {
        GraphicsPresetRegistry presets = new();
        NumericCVar<int> fog = Declare(presets, "r_fog_quality", 3, "0", "1", "3", "4");
        presets.Lock(GraphicsPreset.Vanilla);

        // The lock is on choosing a preset, not on the variables: a hand change still reads Custom, and
        // re-applying the locked preset is how the settings get back to it.
        fog.Value = 2;
        Assert.Equal(GraphicsPreset.Custom, presets.Current);

        Assert.Equal(1, presets.Apply(GraphicsPreset.Vanilla));
        Assert.Equal(0, fog.Value);
        Assert.Equal(GraphicsPreset.Vanilla, presets.Current);
    }

    [Fact]
    public void Lock_RejectsCustom()
    {
        GraphicsPresetRegistry presets = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => presets.Lock(GraphicsPreset.Custom));
        Assert.Null(presets.LockedPreset);
    }

    [Fact]
    public void GfxPresetCommand_RefusesAPresetOutsideTheLock()
    {
        GraphicsPresetRegistry presets = new();
        NumericCVar<int> fog = Declare(presets, "r_fog_quality", 3, "0", "1", "3", "4");
        presets.Lock(GraphicsPreset.Vanilla);

        ConsoleSystem console = new(_variables);
        GraphicsPresetCommands.Register(console, presets);
        console.Execute("gfx_preset ultra");

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
