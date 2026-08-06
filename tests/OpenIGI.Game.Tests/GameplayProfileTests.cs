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
public sealed class GameplayProfileTests : IDisposable
{
    private readonly GameplayProfile _preset = GameplayCVars.Preset.Value;
    private readonly bool _ragdoll = GameplayCVars.Ragdoll.Value;
    private readonly float _force = GameplayCVars.RagdollForce.Value;
    private readonly int _substeps = GameplayCVars.RagdollSubsteps.Value;

    /// <summary>The vanilla profile turns simulated deaths off.</summary>
    [Fact]
    public void TheVanillaProfileRestoresTheOriginalsDeathAnimations()
    {
        GameplayCVars.Apply(GameplayProfile.Vanilla);
        Assert.False(GameplayCVars.Ragdoll.Value);
    }

    /// <summary>The enhanced profile turns them on.</summary>
    [Fact]
    public void TheEnhancedProfileSimulatesTheDead()
    {
        GameplayCVars.Apply(GameplayProfile.Enhanced);
        Assert.True(GameplayCVars.Ragdoll.Value);
    }

    /// <summary>Setting the profile variable applies it, which is how <c>g_preset vanilla</c> works at all.</summary>
    [Fact]
    public void SettingThePresetAppliesIt()
    {
        GameplayCVars.Ragdoll.Value = true;
        GameplayCVars.Preset.Value = GameplayProfile.Vanilla;
        Assert.False(GameplayCVars.Ragdoll.Value);

        GameplayCVars.Preset.Value = GameplayProfile.Enhanced;
        Assert.True(GameplayCVars.Ragdoll.Value);
    }

    /// <summary>The custom profile names no values and so changes nothing.</summary>
    [Fact]
    public void TheCustomProfileChangesNothing()
    {
        GameplayCVars.Ragdoll.Value = true;
        GameplayCVars.Apply(GameplayProfile.Custom);
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
        GameplayCVars.Ragdoll.Value = _ragdoll;
        GameplayCVars.RagdollForce.Value = _force;
        GameplayCVars.RagdollSubsteps.Value = _substeps;
    }
}
