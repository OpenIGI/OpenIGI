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
/// gate presentation only — what is drawn, not what is recorded — so the determinism contract in
/// <c>PLAN.md</c> and the vanilla regression gate both stand whatever they are set to. Anything that
/// would alter observable game logic belongs in <c>TODO.md</c>'s faithfulness work instead.
/// </para>
/// <para>
/// The exception is <see cref="Ballistics"/> and its family: tactical ballistics <em>is</em> a
/// gameplay change — a round takes time to arrive and drops on the way — and it says so rather than
/// pretending to be a look. It is defensible here because it defaults <b>off</b>, no profile turns it
/// on (<c>vanilla</c> forces it off, <c>enhanced</c> leaves it alone), and the regression gate never
/// runs with it set. A player who types <c>g_ballistics 1</c> is explicitly asking for a game the
/// original is not.
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

    /// <summary>How hard a round throws a body it kills.</summary>
    /// <remarks>
    /// <para>
    /// One multiplier over the whole impulse model, so the feel of the feature is a single number the
    /// player can turn. See <see cref="Physics.RagdollImpulse"/> for why the exaggeration underneath it
    /// is deliberate. Zero makes bodies simply fall.
    /// </para>
    /// <para>
    /// One and a half, measured: a rifle round carries a body about 1.6 m, a shotgun about 3, a pistol
    /// about half. At 1 the knock is real but easy to miss, because a guard is standing <em>on</em> the
    /// floor when he is shot and the contact takes most of it back straight away.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> RagdollForce = CVar.Float(
        "g_ragdoll_force",
        1.5f,
        0f,
        8f,
        CVarOptions.Archive,
        "How hard a round throws the body it kills. 0 makes them simply fall.");

    /// <summary>What fraction of real gravity a corpse falls at.</summary>
    /// <remarks>
    /// <para>
    /// Seven tenths. <b>The one number in the ragdoll that is a look rather than a measurement</b>, and
    /// it is a variable rather than a constant for exactly that reason. The solver already falls at the
    /// original's own 9.8 m/s² — see <see cref="Physics.RagdollBody.Gravity"/>, which deliberately picks
    /// that over the 18.6 the player jumps under — so this is not correcting anything.
    /// </para>
    /// <para>
    /// Real gravity simply reads fast on a corpse: the camera is close, the fall is a metre and a half,
    /// and there is none of the anticipation that sells the speed of a jump. Dropping a ragdoll under one
    /// <i>g</i> is what most games do about it. 1 is honest physics; below about 0.5 bodies start to
    /// float.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> RagdollGravity = CVar.Float(
        "g_ragdoll_gravity",
        0.7f,
        0.1f,
        2f,
        CVarOptions.Archive,
        "How fast a corpse falls, as a fraction of real gravity. 1 is physically honest.");

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

    /// <summary>Whether a round is a simulated projectile rather than the original's instant ray.</summary>
    /// <remarks>
    /// <para>
    /// <b>The one gameplay-mutating variable in this container</b>; see the container remarks for why
    /// it is allowed to be. Off, every round resolves the tick it is fired, exactly as retail does.
    /// On, the player's rounds fly at their weapon's real muzzle velocity, drop under gravity and
    /// slow by calibre drag (<see cref="Weapons.Ballistics.BallisticProfile"/>) — you lead a moving
    /// target — and both sides' fire draws a visible streak.
    /// </para>
    /// <para>Off by default in every profile. <c>vanilla</c> forces it off; <c>enhanced</c> does not touch it.</para>
    /// </remarks>
    public static readonly BoolCVar Ballistics = CVar.Bool(
        "g_ballistics",
        false,
        CVarOptions.Archive,
        "Tactical ballistics: rounds fly with travel time, drop and drag instead of hitting instantly.");

    /// <summary>How much air a round flies through.</summary>
    /// <remarks>
    /// One multiplier over every profile's drag constant, so the feel is a single number: zero is a
    /// vacuum where only gravity bends the shot, one is the calibre model as computed.
    /// </remarks>
    public static readonly NumericCVar<float> BallisticsDrag = CVar.Float(
        "g_ballistics_drag",
        1f,
        0f,
        4f,
        CVarOptions.Archive,
        "Air drag on ballistic rounds. 0 is a vacuum, 1 the per-calibre model.");

    /// <summary>What fraction of real gravity a round drops under.</summary>
    /// <remarks>
    /// The rounds fall at the engine's own 9.8 m/s² — the same constant the ragdoll picked over the
    /// player's 18.6, and for the same reason: a bullet is an object, not a jump. This scales it for
    /// players who want a flatter or more exaggerated arc.
    /// </remarks>
    public static readonly NumericCVar<float> BallisticsGravity = CVar.Float(
        "g_ballistics_gravity",
        1f,
        0f,
        2f,
        CVarOptions.Archive,
        "Bullet drop on ballistic rounds, as a fraction of real gravity.");

    /// <summary>The least time a ballistic round flies in open air, in seconds.</summary>
    /// <remarks>
    /// The weapon table's effective ranges were authored for the hit-scan path and are short for a
    /// flying round — an MP5's 80 m is a fifth of a second. The round's range is raised to at least
    /// this many seconds of flight, so far targets are reachable; a wall or a body still stops it
    /// the moment it is met.
    /// </remarks>
    public static readonly NumericCVar<float> BallisticsMinimumFlight = CVar.Float(
        "g_ballistics_min_flight",
        2f,
        0f,
        10f,
        CVarOptions.Archive,
        "The least seconds a ballistic round flies in open air, whatever its weapon's range says.");

    /// <summary>Whether the AI's fire draws ballistic streaks too.</summary>
    /// <remarks>
    /// Visual only, and only read while <see cref="Ballistics"/> is on: a guard's round is still
    /// resolved by its own combat code in its own tick, and the streak flies the line that
    /// resolution already chose. Incoming fire becomes something you can see and duck.
    /// </remarks>
    public static readonly BoolCVar BallisticsAiTracers = CVar.Bool(
        "g_ballistics_ai_tracers",
        true,
        CVarOptions.Archive,
        "Draw ballistic streaks for AI fire while g_ballistics is on. Visual only.");

    /// <summary>Whether bullet impacts are heard as well as seen.</summary>
    /// <remarks>
    /// The sounds are the material table's own six per row — dirt for terrain, the struck row for
    /// objects, the <c>Flesh</c> row for a body — picked and rate-limited exactly as the retail shot
    /// loop does (<see cref="World.BulletImpactSounds"/>). It applies to hit-scan fire and to the
    /// optional ballistic path.
    /// </remarks>
    public static readonly BoolCVar BallisticsImpactSounds = CVar.Bool(
        "g_ballistics_impact_sounds",
        true,
        CVarOptions.Archive,
        "Play the struck material's own impact sound for hit-scan and ballistic rounds.");

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

                // The one gameplay-mutating option. Enhanced deliberately does NOT set it back: it is
                // an opt-in on top of any profile, but vanilla means the 2000 build, full stop.
                Ballistics.Value = false;
                break;

            default:
                break;
        }
    }
}
