using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>
/// The renderer's console variables for how bright the frame is shown: tone mapping, exposure, and the
/// day/night cycle that takes brightness off it.
/// </summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class ExposureCVars
{
    /// <summary>How out-of-range light is brought back into what a display can show.</summary>
    public static readonly EnumCVar<ToneMapOperator> ToneMap = Presets.Define(
        CVar.Choice(
            "r_tonemap",
            ToneMapOperator.Neutral,
            CVarOptions.Archive,
            "Tone map: none reproduces the original exactly, neutral stays out of the way, aces and agx are filmic."),
        vanilla: "none",

        // ACES filmic across the modern presets. Neutral was fine while the world had no highlights to tame,
        // but with auto-exposure on and a window several stops brighter than the room it stands in, there is
        // now a real highlight range: ACES' shoulder rolls the blown window off with its colour intact rather
        // than clipping it to flat white, and holds more midtone contrast than AgX over the range this game
        // occupies. Vanilla stays untouched -- none reproduces the original exactly.
        enhanced: "aces",
        high: "aces",
        ultra: "aces");

    /// <summary>The linear value that maps to display white.</summary>
    /// <remarks>
    /// How much headroom above white the tone map reserves. It must match what the scene produces:
    /// this world is LDR textures multiplied by LDR lightmaps, so almost nothing exceeds one, and the
    /// old fixed value of 4 spent the curve's whole range compressing a span that was not there. One
    /// disables the roll-off completely.
    /// <para>
    /// <b>It is a Reinhard parameter, and only the Reinhard operator has one.</b> A white point is the
    /// single number that curve is shaped by; the film curve the higher presets run is shaped by its own
    /// slope, toe, shoulder and black/white levels instead, and has no scalar for this to be. So at any
    /// preset above <c>r_tonemap 1</c> this setting is inert — not ignored by the backend, which would be
    /// a bug, but meaningless to the curve in use. It was found by the cvar sweep reporting it as
    /// reaching nothing with nothing saying why, which is exactly what that sweep is for.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> WhitePoint = Presets.Define(
        CVar.Float(
            "r_white_point",
            1.1f,
            1f,
            8f,
            CVarOptions.Archive,
            "Linear value that maps to display white. Lower is more contrast; 1 disables the roll-off."),
        vanilla: "1.1",
        enhanced: "3",
        high: "3",
        ultra: "3");

    /// <summary>A linear multiplier applied to the scene before tone mapping.</summary>
    /// <remarks>
    /// <para>
    /// With <see cref="AutoExposure"/> on this becomes a bias on the adapted exposure rather than the
    /// whole of it: the eye adapts to the scene's own brightness and this nudges the result up or down.
    /// </para>
    /// <para>
    /// This is the player's knob and nothing else writes it. The day/night cycle's own dimming is a
    /// SEPARATE term -- see <see cref="Nightfall"/> and <see cref="EffectiveExposure"/> -- so that a
    /// night which darkens the picture never rewrites, and never has to restore, the brightness the
    /// player chose. Read <see cref="EffectiveExposure"/>, not this, wherever the frame is exposed.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> Exposure = Presets.Define(
        CVar.Float(
            "r_exposure",
            1.414f,
            MinimumExposure,
            16.0f,
            CVarOptions.Archive,
            "Scene brightness before tone mapping. Half a stop over the level's own lighting."),

        // Half a stop over unity on every modern preset, which is where this number started life: a
        // half-stop auto-exposure compensation chosen while the target look was being calibrated.
        //
        // It was moved to a flat one on the argument that a calibration is not a default a player
        // should inherit, and that a brightness slider reading 1.4 before anyone has
        // touched it has a neutral that is not neutral. Played rather than compared, one is too dark --
        // the dynamic model's own output does not fill the tone curve on its own -- so the argument loses
        // to the picture, and the slider's neutral is simply not where its scale happens to read one.
        //
        // It belongs to a cluster that moves together -- r_sun_scale and r_atmosphere_sun_scale are read
        // ONLY by the dynamic model, r_white_point and r_bloom_threshold were shaped against its curve,
        // and r_screen_gi_max bounds its feedback loop. Change the lighting model on a preset without
        // bringing all of them and the frame is mis-exposed, not merely cheaper.
        //
        // Vanilla stays at one, and not because it would be ignored: vanilla never tone maps, so the
        // value genuinely does nothing there, and the preset that has to stay pixel-identical to the
        // retail build is the last place to carry a number that says the picture was graded.
        vanilla: "1",
        enhanced: "1.414",
        high: "1.414",
        ultra: "1.414");

    /// <summary>The least exposure the frame may be shown at, which is the floor on <see cref="Exposure"/>.</summary>
    /// <remarks>
    /// Named because <see cref="EffectiveExposure"/> subtracts from the player's value and has to land
    /// somewhere: a night deep enough to take the whole of a low brightness setting must still leave a
    /// picture, not a black screen.
    /// </remarks>
    public const float MinimumExposure = 0.05f;

    /// <summary>How much brightness a full night takes off <see cref="Exposure"/>.</summary>
    /// <remarks>
    /// Subtractive, in the slider's own units, so a default brightness of one is shown at 0.7 under a
    /// midnight sky. It is deliberately small: the lighting model already darkens a night scene on its
    /// own, and this exists because the eye adaptation then spends most of that darkness lifting the
    /// picture back towards day. Taking a little off the compensation is what lets a night read as one.
    /// </remarks>
    public const float NightBrightnessDrop = 0.3f;

    /// <summary>How far into night the day/night cycle has taken the scene: 0 in daylight, 1 at full night.</summary>
    /// <remarks>
    /// <para>
    /// Written by the day/night cycle once per frame from the swept sun's own elevation, and read only
    /// through <see cref="EffectiveExposure"/>. It is the hidden half of the brightness: the player owns
    /// <see cref="Exposure"/>, the sky owns this, and the two are combined at the point of use rather
    /// than the sky editing the player's setting behind their back.
    /// </para>
    /// <para>
    /// Published rather than kept as a private field so the console can be asked what the night is
    /// currently doing to the picture; read-only there, because the next frame's sweep would overwrite
    /// anything typed at it.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> Nightfall = CVar.Float(
        "r_nightfall",
        0f,
        0f,
        1f,
        CVarOptions.Hidden | CVarOptions.ReadOnly,
        "How far into night the sun has gone, 0 to 1. Dims the picture; not a setting.");

    /// <summary>Whether the time of day runs on its own while a mission is played.</summary>
    /// <remarks>
    /// <para>
    /// Off, a mission stays at the hour its authors lit it at, which is what every mission in the
    /// original does and what its lighting was designed around. On, that authored hour is merely where
    /// the mission opens: the sun then sweeps on from there at whatever <see cref="DayLength"/> is set
    /// to, which is quick enough by default that a mission of ordinary length ends under a different sky
    /// than it began under.
    /// </para>
    /// <para>
    /// Default off, because it changes the look of every level away from the one its artists chose, and
    /// because a mission whose sun set halfway through is a different mission to play.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar DayNightCycle = CVar.Bool(
        "r_day_night_cycle",
        false,
        CVarOptions.Archive,
        "Let the time of day run while a mission is played; r_day_length says how long a day takes.");

    /// <summary>How long a whole day of the cycle takes, in real minutes.</summary>
    /// <remarks>
    /// <para>
    /// Forty minutes by default: long enough that the light is a setting rather than a strobe, short
    /// enough that a mission of ordinary length visibly ends under a different sky than it opened on. An
    /// hour was the first figure tried and it was too slow to be worth having -- most missions finished
    /// under the sun they started with, so the setting did nothing anybody could see.
    /// </para>
    /// <para>
    /// Free to set to anything, including a minute, which is the honest way to LOOK at the sweep: a
    /// whole sunrise-to-night pass in the time it takes to walk across a level. The cycle steps the
    /// clock a fixed slice of the arc at a time rather than a fixed number of seconds, so the sweep is
    /// equally smooth at a minute a day and at half a day a day; what changes is how often it steps.
    /// </para>
    /// <para>
    /// Does nothing at all unless <see cref="DayNightCycle"/> is on. It is not the scrub keys' rate --
    /// those sweep a day in thirty seconds and are meant to be held and watched.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> DayLength = CVar.Float(
        "r_day_length",
        40f,
        0.5f,
        720f,
        CVarOptions.Archive,
        "How long a whole day takes when the day/night cycle is on, in real minutes.");

    /// <summary>Which of the level's lamps go off by day and come on at dusk.</summary>
    /// <remarks>
    /// <para>
    /// <b>Not retail.</b> The original's lamps are painted into its lightmaps and burn at noon as at
    /// midnight. Here each one follows the sun through a photocell of its own -- see
    /// <c>LampPhotocells</c> -- so as the sky darkens the yard's lamps strike one by one, raggedly and
    /// audibly, and at dawn go out again.
    /// </para>
    /// <para>
    /// Outdoors by default: a lamp with a roof over it is lit by day as by night in any real building,
    /// and a windowless room whose lamps had gone out at noon would be a room the player cannot see in.
    /// Everywhere puts every lamp on the daylight. Off is the original's lamps, always lit.
    /// </para>
    /// <para>
    /// Follows the sun whether or not the clock is running: the scrub keys move it either way. Nothing
    /// happens under the vanilla preset, which draws the original's lamps, or with a pinned
    /// <c>-sundir</c>, which is a comparison capture and keeps every lamp as the level ships it.
    /// </para>
    /// </remarks>
    public static readonly EnumCVar<LampDaylight> LampDaylight = CVar.Choice(
        "r_lamp_daylight",
        global::OpenIGI.Rendering.LampDaylight.Outdoors,
        CVarOptions.Archive,
        "Which lamps go off by day and come on at dusk: off, outdoors or everywhere. Not in the original.");

    /// <summary>The brightness the frame is actually exposed at: the player's, less the night's.</summary>
    /// <remarks>
    /// Every consumer of the exposure reads THIS, including the two that only want to divide a
    /// display-referred colour by whatever the resolve is about to multiply it by -- the first-person
    /// rig's lighting and the selection glow. Reading <see cref="Exposure"/> there instead would leave
    /// them compensating for an exposure the frame is no longer using, so the arms and the marker would
    /// drift brighter as the night came in.
    /// </remarks>
    public static float EffectiveExposure => MathF.Max(
        Exposure.Value
            - (NightBrightnessDrop * Nightfall.Value)
            - (AuthoredNightDropFor.Value * AuthoredNightfall.Value),
        MinimumExposure);

    /// <summary>How much darker a title's OWN night makes the picture, in stops, at full night.</summary>
    /// <remarks>
    /// <para>
    /// Far more than <see cref="NightBrightnessDrop"/>, and for a different reason. That one trims the
    /// eye's compensation as a sun sets, over a scene the adaptation is otherwise handling. This one
    /// answers a scene the adaptation cannot handle at all: I.G.I. 2 authors night by leaving the sun UP
    /// and painting it nearly black, so every luminance in the frame is low together and the adaptation
    /// reads it as a correctly-exposed dark subject and lifts the whole thing back to mid grey. The sky
    /// blows out, the sun-facing hillsides go white, and the mission reads as an overcast noon.
    /// </para>
    /// <para>
    /// <b>Fitted against vanilla's GROUND</b>, which is the one surface both paths composite from the same
    /// product -- texture times detail times the tile's bake -- so a difference there is the exposure and
    /// nothing else. Frame means are no use for this: the sky is most of the frame and the two paths draw
    /// different skies, so fitting on the frame fits on the sky.
    /// </para>
    /// <para>
    /// Mission 1, mean ground luminance over the lower half of the frame against vanilla's 15.22, swept on
    /// r_exposure (which enters this the same way, one for one): 0.909 at 1.55, 1.101 at 1.62. The
    /// crossing is 1.585, which is an EffectiveExposure of 0.482 at full night.
    /// </para>
    /// <para>
    /// Stated per unit of the SWEEP's nightfall, because that is what now drives it: a night-bound level
    /// sits at midnight where the sweep reads a full one, so 1.414 - (0.3 + this) x 1 = 0.482. It used to
    /// be stated against the level's own authored figure of 0.85, which is where the 0.8 came from -- the
    /// same exposure, said against a number that no longer reaches it.
    /// </para>
    /// <para>
    /// <b>The previous value was 1.0, and it was fitted against a broken picture.</b> The ground's bake was
    /// being written into its albedo then, so every lit pixel of it was about thirty-five times too dark
    /// and the adaptation was opening against that; the drop was compensating for a bug. Refitting had to
    /// wait for the ground to be right AND for its false specular to go, because that lobe was carrying
    /// about a third of the ground's brightness on the dynamic model alone.
    /// </para>
    /// <para>
    /// It is a bias on the adapted value, not a fixed exposure, so a dark interior in the same mission
    /// still opens up. And it is multiplied by what the LEVEL says about its own darkness, so a title that
    /// says nothing -- Project I.G.I. -- pays none of it whatever this is set to.
    /// </para>
    /// </remarks>
    public const float AuthoredNightDrop = 0.632f;

    /// <summary>How many units of exposure the current level's own night takes off, at full night.</summary>
    /// <remarks>
    /// <see cref="AuthoredNightDrop"/> unless the title says otherwise. It was fitted against I.G.I. 2's
    /// night missions, which leave the sun up and paint it nearly black. Project I.G.I.'s night missions
    /// are bound through the cycle's own moon instead, whose darkness the lighting already computes, so
    /// they carry a drop of their own. Set by the shell as a level loads; read only through
    /// <see cref="EffectiveExposure"/>.
    /// </remarks>
    public static readonly NumericCVar<float> AuthoredNightDropFor = CVar.Float(
        "r_authored_night_drop",
        AuthoredNightDrop,
        0f,
        2f,
        CVarOptions.Hidden | CVarOptions.ReadOnly,
        "How much exposure the level's own night takes off at full night; not a setting.");

    /// <summary>How dark a title says its own level is, 0 to 1; 0 for a title that does not say.</summary>
    /// <remarks>
    /// Separate from <see cref="Nightfall"/> so the two cannot be confused: that one is the day/night
    /// sweep's reading of its own swept sun, this one is what the level itself declares. Project I.G.I.
    /// declares nothing and leaves this at nought, which is what keeps its exposure exactly as it was.
    /// </remarks>
    public static readonly NumericCVar<float> AuthoredNightfall = CVar.Float(
        "r_authored_nightfall",
        0f,
        0f,
        1f,
        CVarOptions.Hidden | CVarOptions.ReadOnly,
        "How dark the level itself says it is, 0 to 1. Dims the picture; not a setting.");

    /// <summary>The hour a level's sun starts at, or -1 for the hour the level's authors gave it.</summary>
    /// <remarks>
    /// <para>
    /// The day-night cycle reproduces a level's authored sun EXACTLY only at the level's own authored
    /// hour -- its elevation and bearing are fitted through that hour -- so a fixed start hour is the
    /// authored sun for the levels authored at that hour and a different sky for every other. Negative
    /// means "the level's own", which is what every mission should open on.
    /// </para>
    /// <para>
    /// Not archived, deliberately. It used to be, and one scrub written to the settings file made every
    /// mission afterwards open at night -- a preference nobody remembers making, and one whose effect
    /// reads as the game being broken. A start hour set at the console or in the menu now lasts the
    /// session and no longer.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> TimeOfDay = CVar.Float(
        "r_time_of_day",
        -1f,
        -1f,
        24f,
        CVarOptions.None,
        "Hour of day the sun starts a level at, 0-24; -1 (the default) is the level's own authored hour.");

    /// <summary>Whether the exposure adapts to the scene's own brightness over time.</summary>
    /// <remarks>
    /// Off by default, and only ever engages where a tone map is on -- the vanilla copy path returns
    /// before exposure is applied at all, so a traced preset can adapt while vanilla stays byte-identical.
    /// A traced scene without eye adaptation reads worse than one lit the original's way, which is why the
    /// plan calls it non-optional there; it is opt-in here until a preset turns it on.
    /// </remarks>
    public static readonly BoolCVar AutoExposure = Presets.Define(
        CVar.Bool(
            "r_autoexposure",
            false,
            CVarOptions.Archive,
            "Adapt exposure to the scene's average brightness over time. Only where a tone map is on."),
        vanilla: "0",

        // ON for every modern preset. It was turned off in the fixed-exposure era on the theory that a
        // calibrated exposure lit interiors and exteriors together -- but a fixed exposure tuned for a sunlit
        // exterior crushes an interior to black, which is exactly what a windowed room looked like: dark and
        // flat. Adapting the exposure to what the camera is actually looking at is what every modern engine
        // does, and it is what lets a dim room read bright next to a blown-out window while the tone map's
        // shoulder rolls the window off rather than clipping it. r_exposure is a bias on the adapted value
        // here, no longer the whole of it. Vanilla keeps the original's fixed brightness.
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
