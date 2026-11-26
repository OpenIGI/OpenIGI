using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the vanilla gameplay profile.
/// </summary>
/// <remarks>
/// <para>
/// "Vanilla restores the original" is a promise, and a promise nothing asserts is a comment. The
/// remaster track's whole contract is that <c>-vanilla</c> gives back the 2000 build, and for the
/// gameplay layer that means the retail death animations and no simulated bodies.
/// </para>
/// <para>
/// These variables are process-wide <c>static readonly</c> singletons, so the isolation has to be manual
/// — the same arrangement <c>AudioProfileTests</c> uses.
/// </para>
/// </remarks>
[Collection(ExperienceSettings.Name)]
public sealed class GameplayProfileTests : IDisposable
{
    private readonly ExperiencePreset _preset = GameplayCVars.Preset.Value;
    private readonly float _force = GameplayCVars.RagdollForce.Value;
    private readonly int _substeps = GameplayCVars.RagdollSubsteps.Value;

    // Every value an experience writes, for putting back: applying one reaches all of them.
    private readonly (ICVar Variable, string Value)[] _managed =
        [.. ExperiencePresetRegistry.Default.Managed.Select(static variable => (variable, variable.ValueText))];

    /// <summary>The vanilla profile turns simulated deaths off.</summary>
    [Fact]
    public void TheVanillaProfileRestoresTheOriginalsDeathAnimations()
    {
        GameplayCVars.Apply(ExperiencePreset.Vanilla);
        Assert.False(GameplayCVars.Ragdoll.Value);
    }

    /// <summary>The vanilla profile takes away what the original never had: saves mid-mission, and bodies that stay.</summary>
    [Fact]
    public void TheVanillaProfileTakesAwayTheRemastersAdditions()
    {
        GameplayCVars.Apply(ExperiencePreset.Enhanced);
        Assert.True(GameplayCVars.Igi1Saves.Value);
        Assert.True(GameplayCVars.BodiesStay.Value);
        Assert.Equal(ExperiencePreset.Enhanced, ExperiencePresetRegistry.Default.Detect());

        GameplayCVars.Apply(ExperiencePreset.Vanilla);
        Assert.False(GameplayCVars.Igi1Saves.Value);
        Assert.False(GameplayCVars.BodiesStay.Value);
        Assert.Equal(ExperiencePreset.Vanilla, ExperiencePresetRegistry.Default.Detect());

        GameplayCVars.BodiesStay.Value = true;
        Assert.Equal(ExperiencePreset.Custom, ExperiencePresetRegistry.Default.Detect());
    }

    /// <summary>Simulated deaths ship off, and are something a player turns on.</summary>
    [Fact]
    public void SimulatedDeathsAreOffByDefault()
    {
        Assert.False(GameplayCVars.Ragdoll.DefaultValue);
    }

    /// <summary>The enhanced profile leaves them as the player set them, either way.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheEnhancedProfileLeavesSimulatedDeathsAlone(bool chosen)
    {
        GameplayCVars.Ragdoll.Value = chosen;
        GameplayCVars.Apply(ExperiencePreset.Enhanced);
        Assert.Equal(chosen, GameplayCVars.Ragdoll.Value);
    }

    /// <summary>Setting the profile variable applies it, which is how <c>g_preset vanilla</c> works at all.</summary>
    [Fact]
    public void SettingThePresetAppliesIt()
    {
        GameplayCVars.Preset.Value = ExperiencePreset.Enhanced;
        GameplayCVars.Ragdoll.Value = true;
        GameplayCVars.Preset.Value = ExperiencePreset.Vanilla;
        Assert.False(GameplayCVars.Ragdoll.Value);
    }

    /// <summary>The custom profile names no values and so changes nothing.</summary>
    [Fact]
    public void TheCustomProfileChangesNothing()
    {
        GameplayCVars.Ragdoll.Value = true;
        GameplayCVars.Apply(ExperiencePreset.Custom);
        Assert.True(GameplayCVars.Ragdoll.Value);
    }

    /// <summary>The variables carry the <c>g_</c> prefix the settings screen groups its gameplay page by.</summary>
    [Fact]
    public void EveryVariableIsOnTheGameplayPage()
    {
        Assert.StartsWith("g_", GameplayCVars.Preset.Name, StringComparison.Ordinal);
        Assert.StartsWith("g_", GameplayCVars.Ragdoll.Name, StringComparison.Ordinal);
        Assert.StartsWith("g_", GameplayCVars.RagdollForce.Name, StringComparison.Ordinal);
        Assert.StartsWith("g_", GameplayCVars.RagdollSubsteps.Name, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GameplayCVars.Preset.Value = _preset;
        foreach ((ICVar variable, string value) in _managed)
        {
            variable.TrySetText(value, out _);
        }

        GameplayCVars.RagdollForce.Value = _force;
        GameplayCVars.RagdollSubsteps.Value = _substeps;
    }
}
