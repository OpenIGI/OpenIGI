using OpenIGI.Core.Configuration;

namespace OpenIGI.Game;

/// <summary>
/// The gameplay layer's console variables.
/// </summary>
/// <remarks>
/// <para>
/// Declared next to the code that reads them, the way the renderer and the audio seam declare their own.
/// The <c>g_</c> prefix is the one the settings screen already builds its Gameplay page from, so the rows
/// here appear in the menu, the console and <c>settings.cfg</c> without any of the three being told.
/// </para>
/// <para>
/// <b>Nothing declared here may change what the simulation does.</b> These gate presentation only — what
/// is drawn, not what is recorded — so the determinism contract in <c>PLAN.md</c> and the vanilla
/// regression gate both stand whatever they are set to. Anything that would alter observable game logic
/// belongs in <c>TODO.md</c>'s faithfulness work instead.
/// </para>
/// </remarks>
[CVarContainer]
public static class GameplayCVars
{
    /// <summary>Which named profile the gameplay-presentation settings correspond to.</summary>
    /// <remarks>
    /// Setting this writes the profile's values over the variables it names, which is how <c>-vanilla</c>
    /// reaches the gameplay layer: the flag emits <c>g_preset vanilla</c> as a start-up statement,
    /// exactly as it emits <c>gfx_preset vanilla</c> and <c>snd_preset vanilla</c>.
    /// </remarks>
    public static readonly EnumCVar<GameplayProfile> Preset = CVar.Choice(
        "g_preset",
        GameplayProfile.Enhanced,
        CVarOptions.Archive,
        "Gameplay presentation profile: vanilla restores the original's canned death animations.");

    /// <summary>Whether the dead are simulated rather than playing a canned death animation.</summary>
    /// <remarks>
    /// <para>
    /// Off, a body dies exactly as it does in the original: one of the death clips the hit zone and the
    /// killing weapon name, played out with the nine-ray travel limiter behind it. On, that clip still
    /// runs — it is what the simulation moves the corpse by, and none of that changes — but what is
    /// <em>drawn</em> is a physically simulated body, seeded from the round's own direction and the
    /// weapon's momentum.
    /// </para>
    /// <para>Applies to guards and to the player alike; both die through the same rig.</para>
    /// </remarks>
    public static readonly BoolCVar Ragdoll = CVar.Bool(
        "g_ragdoll",
        true,
        CVarOptions.Archive,
        "Simulate the dead instead of playing the original's canned death animations.");

    /// <summary>How hard a round throws a body it kills.</summary>
    /// <remarks>
    /// One multiplier over the whole impulse model, so the feel of the feature is a single number the
    /// player can turn. At 1 a rifle round moves a body about 2.5 m/s, which is roughly thirty times what
    /// it would really do — see <see cref="Physics.RagdollImpulse"/> for why that exaggeration is
    /// deliberate. Zero makes bodies simply fall.
    /// </remarks>
    public static readonly NumericCVar<float> RagdollForce = CVar.Float(
        "g_ragdoll_force",
        1f,
        0f,
        8f,
        CVarOptions.Archive,
        "How hard a round throws the body it kills. 0 makes them simply fall.");

    /// <summary>How many solver substeps each ragdoll takes per game tick.</summary>
    /// <remarks>
    /// Position-based dynamics converges with substeps far better than with iterations, so this is the
    /// dial that trades cost for stiffness. Six holds a body together through a fall onto stairs; below
    /// about four the limbs visibly stretch on impact.
    /// </remarks>
    public static readonly NumericCVar<int> RagdollSubsteps = CVar.Int(
        "g_ragdoll_substeps",
        6,
        1,
        32,
        CVarOptions.Archive,
        "Ragdoll solver substeps per tick. Higher is stiffer and more expensive.");

    /// <summary>Wires the profile variable to the values it stands for.</summary>
    /// <remarks>
    /// A static constructor rather than a registry, the same way <c>AudioCVars</c> does it and for the
    /// same reason: the preset registry lives in the renderer, which this layer does not drive. The
    /// profile is not recomputed when one of its variables is changed by hand — it says what was asked
    /// for, and a player who then turns one thing off is not silently switched to "custom" mid-session.
    /// </remarks>
    static GameplayCVars() => Preset.Changed += (_, _) => Apply(Preset.Value);

    /// <summary>Writes a profile's values over the variables it names.</summary>
    /// <param name="profile">The profile to apply. <see cref="GameplayProfile.Custom"/> changes nothing.</param>
    public static void Apply(GameplayProfile profile)
    {
        switch (profile)
        {
            case GameplayProfile.Vanilla:
                Ragdoll.Value = false;
                break;

            case GameplayProfile.Enhanced:
                Ragdoll.Value = true;
                break;

            default:
                break;
        }
    }
}
