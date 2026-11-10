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
/// <b>Nothing declared here may change what the simulation does, with one named exception.</b> These
/// gate presentation only — what is drawn, not what is recorded — so the determinism contract and the
/// vanilla regression gate both stand whatever they are set to. Anything that would alter observable game
/// logic belongs with the faithfulness work instead. The exception is <see cref="RealisticDrops"/>.
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

    /// <summary>Whether Project I.G.I. offers save games, which the original never had.</summary>
    /// <remarks>
    /// Off while the level's state is still being given to the save engine piece by piece: a save that kept only some of a
    /// level would load into a mission that is not the one saved. I.G.I. 2's save games are its own and do not read this.
    /// </remarks>
    public static readonly BoolCVar Igi1Saves = CVar.Bool(
        "g_igi1_saves",
        false,
        CVarOptions.Archive,
        "Offer save games in Project I.G.I. (in development: only part of a level is kept).");

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
    /// <para>
    /// Off by default, and no profile turns it on: the canned deaths are the game as shipped, and the
    /// simulation is something a player asks for.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar Ragdoll = CVar.Bool(
        "g_ragdoll",
        false,
        CVarOptions.Archive,
        "Simulate the dead instead of playing the original's canned death animations.");

    /// <summary>Whether a dropped weapon falls as a real rigid body or the way the original drops it.</summary>
    /// <remarks>
    /// <para>
    /// Off in the Vanilla preset, which drops a gun exactly as <c>GunPickup</c> does
    /// (<c>0x46C980</c>/<c>0x46CEA0</c>): laid on its side, falling straight down onto the surface
    /// under it and bouncing at half speed until it settles.
    /// </para>
    /// <para>
    /// On in every other preset: the gun leaves the hand with the body's motion and the round's knock,
    /// and falls as a rigid body with its real mass, the inertia of its own mesh and contacts against
    /// the real world triangles -- tumbling, bouncing, sliding off slopes and lying across ledges.
    /// </para>
    /// <para>
    /// Where the gun ends up decides where the player picks it up, so this is the exception to the rule
    /// above. It is safe because the Vanilla preset forces it off, and the regression gate runs Vanilla.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar RealisticDrops = Core.Configuration.Presets.GraphicsPresetRegistry.Default.Define(
        CVar.Bool(
            "g_realistic_drops",
            true,
            CVarOptions.Archive,
            "Dropped weapons fall as real rigid bodies. Off drops them the way the original does."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether I.G.I. 2's player's ground speed turns with him, or drifts on as retail's does.</summary>
    /// <remarks>
    /// Retail keeps the speed pointing where it pointed when the player turns, and clears the part left pointing sideways only
    /// at the strafe step a tick, so a turn at a run slides him sideways like a car. On, the speed turns with him, as Project
    /// I.G.I.'s player's does. A third exception to the rule above: where the player goes is game logic. The Vanilla preset
    /// forces it off, as with <see cref="RealisticDrops"/>.
    /// </remarks>
    public static readonly BoolCVar SteadyTurns = Core.Configuration.Presets.GraphicsPresetRegistry.Default.Define(
        CVar.Bool(
            "g_steady_turns",
            true,
            CVarOptions.Archive,
            "I.G.I. 2's player keeps moving the way he faces as he turns. Off drifts sideways after a turn, as the original does."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether Project I.G.I.'s dead soldiers stay where they fell, or leave the world once out of sight as the original's do.</summary>
    /// <remarks>
    /// <para>
    /// The original's HumanAI (<c>0x44F410</c>) lets a body lie 300 ticks and then takes it out of the world on the first tick the
    /// last render did not draw it, so a corpse the player turns away from or walks past is gone when he comes back. On, every body
    /// stays for the rest of the mission.
    /// </para>
    /// <para>
    /// A <c>GuardGenerator</c>'s guard is the exception either way: the generator clones its replacement only once the body is out
    /// of the world (<c>0x487EE0</c>), so his body goes when its stay runs out, as the original's does, and his gun stays behind.
    /// </para>
    /// <para>
    /// A fourth exception to the rule above: a body in the world is game logic (a mine still meets it). The Vanilla preset forces
    /// it off, as with <see cref="RealisticDrops"/>.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar BodiesStay = Core.Configuration.Presets.GraphicsPresetRegistry.Default.Define(
        CVar.Bool(
            "g_bodies_stay",
            true,
            CVarOptions.Archive,
            "Dead soldiers stay where they fell. Off takes a body away once it is out of sight, as the original does."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How hard a round throws a body it kills.</summary>
    /// <remarks>
    /// <para>
    /// One multiplier over the whole impulse model, so the feel of the feature is a single number the
    /// player can turn. See <c>RagdollImpulse</c> for why the exaggeration underneath it
    /// is deliberate. Zero makes bodies simply fall.
    /// </para>
    /// <para>
    /// One. A body shot dead mostly drops where it stood — its legs give out — with the round rocking the
    /// part it struck; a shotgun at close range or a grenade still visibly throws it. Turn it up for the
    /// cinematic knock-back other games use.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> RagdollForce = CVar.Float(
        "g_ragdoll_force",
        1f,
        0f,
        8f,
        CVarOptions.Archive,
        "How hard a round throws the body it kills. 0 makes them simply fall.");

    /// <summary>What fraction of real gravity a corpse falls at.</summary>
    /// <remarks>
    /// <para>
    /// One: real gravity. The solver falls at the original's own 9.8 m/s² — see <c>Gravity</c>, which
    /// deliberately picks that over the 18.6 the player jumps under.
    /// </para>
    /// <para>
    /// It used to default to seven tenths, on the grounds that real gravity reads fast on a corpse. It
    /// read as weightless instead: under seven tenths of a <i>g</i> a falling man hangs in the air like
    /// a man on the moon. What reads as too fast is a body that drops straight down with no collapse,
    /// and the muscle fade (<c>g_ragdoll_muscle</c>) is what fixes that.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> RagdollGravity = CVar.Float(
        "g_ragdoll_gravity",
        1f,
        0.1f,
        2f,
        CVarOptions.Archive,
        "How fast a corpse falls, as a fraction of real gravity. 1 is physically honest.");

    /// <summary>For how long a corpse keeps any muscle tone, in seconds.</summary>
    /// <remarks>
    /// <para>
    /// For this long after death the joints hold — the pose the body died in, or the death clip with
    /// <c>g_ragdoll_muscle_clip</c> — with a strength that fades to nothing. Each joint lets go over its own
    /// share of it: the knees and ankles first, the trunk last. So a body shot standing keeps its trunk
    /// together while its legs give, goes where the round and gravity take it, and only then goes limp
    /// over whatever it landed on.
    /// </para>
    /// <para>
    /// Half a second, which is about how long a person shot dead keeps any tone at all. Zero is a body
    /// that is limp from the instant of death and drops like a puppet with its strings cut.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> RagdollMuscle = CVar.Float(
        "g_ragdoll_muscle",
        0.5f,
        0f,
        3f,
        CVarOptions.Archive,
        "Seconds a corpse keeps any muscle tone before it goes limp. 0 is limp at once.");

    /// <summary>Whether a corpse's muscles follow the death animation rather than hold the pose it died in.</summary>
    /// <remarks>
    /// <para>
    /// Off. Following the authored clip makes every death the animator's: a guard shot from behind at a
    /// window turns and folds to the floor the way the clip folds, rather than pitching forwards onto the
    /// sill. Holding the pose he died in and letting it go leaves where he ends up to the round, gravity
    /// and what is around him.
    /// </para>
    /// <para>
    /// On gives the more choreographed falls, which some prefer: the original's death animation plays out
    /// on every joint for <c>g_ragdoll_muscle</c> seconds, and only then does the body go limp and the
    /// physics take it the rest of the way.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar RagdollMuscleClip = CVar.Bool(
        "g_ragdoll_muscle_clip",
        false,
        CVarOptions.Archive,
        "Corpses play the original death animation first, then go limp: authored falls instead of physical ones.");

    /// <summary>Draws every soldier's bones and corpse shapes over the frame.</summary>
    /// <remarks>
    /// 1 names each bone where it sits; 2 also draws the capsules a ragdoll collides with, for a living
    /// soldier as the rig would build them from his current pose. For checking a rig, not for playing.
    /// </remarks>
    public static readonly NumericCVar<int> RagdollDebug = CVar.Int(
        "g_ragdoll_debug",
        0,
        0,
        2,
        CVarOptions.Developer | CVarOptions.Hidden,
        "Draw soldiers' bones: 1 names them, 2 also draws the ragdoll capsules.");

    /// <summary>How many solver substeps each ragdoll takes per game tick.</summary>
    /// <remarks>
    /// <para>
    /// Position-based dynamics converges with substeps far better than with iterations, so this is the
    /// dial that trades cost for stiffness — and it is the <em>only</em> convergence knob that matters,
    /// because each substep runs a single solver sweep. It is also the whole cost of the feature: every
    /// constraint in the body is projected this many times a tick.
    /// </para>
    /// <para>
    /// Eight, measured. Sixteen costs three times as much and buys nothing that can be seen — at both
    /// counts every body settles, and the joint drift left at rest is around a centimetre either way.
    /// Below about six the drift starts to show on impact.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> RagdollSubsteps = CVar.Int(
        "g_ragdoll_substeps",
        8,
        1,
        64,
        CVarOptions.Archive,
        "Ragdoll solver substeps per tick. Higher is stiffer and more expensive.");

    /// <summary>Whether bullet impacts are heard as well as seen.</summary>
    /// <remarks>
    /// The sounds are the material table's own six per row — dirt for terrain, the struck row for
    /// objects, the <c>Flesh</c> row for a body — picked and rate-limited exactly as the retail shot
    /// loop does (<c>BulletImpactSounds</c>).
    /// </remarks>
    public static readonly BoolCVar ImpactSounds = CVar.Bool(
        "g_impact_sounds",
        true,
        CVarOptions.Archive,
        "Play the struck material's own impact sound where a round lands.");

    /// <summary>Whether the player is drawn from behind rather than through their own eyes.</summary>
    /// <remarks>
    /// <para>
    /// Presentation only, and deliberately so: the eye the shot is fired from, the ear the world is
    /// heard through and everything the simulation records stay exactly where the first-person view put
    /// them. Only the point the frame is rasterised from moves, and the character the original always
    /// carried — the body <see cref="Player.HumanPlayerBody"/> describes, the one the death camera
    /// reveals — is drawn instead of the arms.
    /// </para>
    /// <para>
    /// <b>Not archived, on purpose.</b> A default key is bound to it (<c>DefaultBinds</c>), and the one
    /// rule that table has is that a key the game binds for itself must not decide what the game starts
    /// as tomorrow. A view the player flipped to while looking at a jump is not a setting they saved.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar ThirdPerson = CVar.Bool(
        "g_thirdperson",
        false,
        CVarOptions.None,
        "Draw the player from behind instead of through their own eyes.");

    /// <summary>How far behind the head the third-person eye settles, in metres.</summary>
    public static readonly NumericCVar<float> ThirdPersonDistance = CVar.Float(
        "g_thirdperson_distance",
        2.6f,
        0.5f,
        8f,
        CVarOptions.Archive,
        "How far behind the player the third-person camera sits, in metres.");

    /// <summary>How far to the player's right the third-person eye sits, in metres.</summary>
    /// <remarks>
    /// The shoulder offset is what keeps the character out of the middle of the frame. It costs a
    /// truthful centred crosshair — the round still leaves the eye, which is no longer on the camera's
    /// own axis — which is why the remaster HUD projects the aim point rather than drawing a cross at
    /// the centre of the screen. Zero puts the camera exactly on the eye's sight line, where the two
    /// coincide again.
    /// </remarks>
    public static readonly NumericCVar<float> ThirdPersonShoulder = CVar.Float(
        "g_thirdperson_shoulder",
        0.55f,
        -2f,
        2f,
        CVarOptions.Archive,
        "How far right of the player the third-person camera sits, in metres. 0 centres it behind them.");

    /// <summary>How far above the eye the third-person camera sits, in metres.</summary>
    public static readonly NumericCVar<float> ThirdPersonHeight = CVar.Float(
        "g_thirdperson_height",
        0.25f,
        -1f,
        2f,
        CVarOptions.Archive,
        "How far above the player's eye the third-person camera sits, in metres.");

    /// <summary>How long the move between the two views takes, in seconds.</summary>
    /// <remarks>
    /// The whole point of the feature having a number at all: the camera travels the arc between the eye
    /// and the shoulder over this long, with zero velocity and zero acceleration at both ends, so
    /// neither entering nor leaving the view is a cut. Zero is the cut, for anyone who wants one.
    /// </remarks>
    public static readonly NumericCVar<float> ThirdPersonBlend = CVar.Float(
        "g_thirdperson_blend",
        0.35f,
        0f,
        2f,
        CVarOptions.Archive,
        "How long the camera takes to travel between the first- and third-person views, in seconds.");

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

            default:
                break;
        }
    }
}
