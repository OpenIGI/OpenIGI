using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for what gunfire draws: the muzzle flame, impacts and the marks rounds leave.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class CombatEffectCVars
{
    /// <summary>How bright a gun's muzzle flame draws, as a fraction of the original's.</summary>
    /// <remarks>
    /// <para>
    /// The flame is an additive glow whose brightness rides its vertex colours, so this scales those
    /// at upload. Zero removes the flash entirely; one is the retail fireball, which vanilla pins so
    /// the original's picture stays exact.
    /// </para>
    /// <para>
    /// The remastered presets sit at 0.45, measured against the tactical-ballistics tracers: the
    /// retail flame is bright enough to swallow the first metres of a tracer, and a subtler flash is
    /// what every modern shooter draws anyway.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> MuzzleFlash = Presets.Define(
        CVar.Float(
            "r_muzzle_flash",
            1f,
            0f,
            1f,
            CVarOptions.Archive,
            "Muzzle flash brightness. 1 is the original's, 0 removes it."),
        vanilla: "1",
        enhanced: "0.45",
        high: "0.45",
        ultra: "0.45");

    /// <summary>Whether the original's own impact effects are drawn.</summary>
    /// <remarks>
    /// <b>Retail behaviour, on everywhere including vanilla.</b> The <c>GunShotEffect</c> a struck
    /// material names (<c>0x438440</c>) — a puff of <c>concrete1.spr</c> half the time and two to four
    /// chips of the material's own model — and the blood a body throws (<c>0x463AD0</c>), which is
    /// separately gated by the game's own blood option. This exists to be turned off for diagnosis,
    /// not for taste; <see cref="ImpactParticles"/> is the remaster's extra burst on top.
    /// </remarks>
    public static readonly BoolCVar ImpactEffects = Presets.Define(
        CVar.Bool(
            "r_impact_effects",
            true,
            CVarOptions.Archive,
            "Draw the original's impact dust, surface chips and blood."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How rich the extra remaster particle burst where a round lands is, or 0 for none.</summary>
    /// <remarks>
    /// A remaster addition <b>on top of</b> <see cref="ImpactEffects"/>: sparks and dust composed from
    /// the material table's own row for the struck surface — metal showers sparks, dirt breathes a slow
    /// brown plume, a body a brief red mist. One is the authored look, two doubles the particle counts,
    /// and vanilla pins zero so a vanilla run shows the original's effects and nothing else.
    /// </remarks>
    public static readonly NumericCVar<float> ImpactParticles = Presets.Define(
        CVar.Float(
            "r_impact_particles",
            1f,
            0f,
            2f,
            CVarOptions.Archive,
            "Particle bursts where rounds land: sparks, dust and debris by surface. 0 turns them off."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the marks rounds leave on the world are drawn.</summary>
    /// <remarks>
    /// Presentation only — the marks are still recorded, so turning this back on shows them. Exists
    /// as much for diagnosis as for taste: decals are one of a handful of world-space quad renderers,
    /// and being able to rule them in or out from the console in seconds is worth a variable.
    /// </remarks>
    public static readonly BoolCVar BulletMarks = Presets.Define(
        CVar.Bool(
            "r_bulletmarks",
            true,
            CVarOptions.Archive,
            "Draw the marks rounds leave on surfaces."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
