using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for the level's lamps: how they are detected, how far and how brightly they light, and their shadows.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class LocalLightCVars
{
    /// <summary>How strongly the level's detected lamp fixtures light the scene as real lights.</summary>
    /// <remarks>
    /// <para>
    /// The screen gather only carries light that already exists in the frame -- daylight through a window,
    /// a sunlit wall -- so a windowless interior it cannot help: there is no source to gather. This turns
    /// the fixtures the lamp detection proposes into direct lights, which is the honest way to light such
    /// a room -- a real source with a real falloff, not exposure lifting a black frame into grey.
    /// </para>
    /// <para>
    /// Each is punctual with a windowed inverse square, and DIRECTIONAL where the fixture's own geometry
    /// says which way it faces: the light falls off as the cosine of the angle off that axis, so a ceiling
    /// tube lights the floor and leaves the slab it is bolted to dark, which is what a real fitting does
    /// and what an omnidirectional point emphatically does not. Scales the whole set; the fixtures'
    /// relative intensities are the detection's, and this is the one number tuned by eye.
    /// </para>
    /// <para>
    /// Off at vanilla, where the shipped bakes still carry the interiors and adding a second set of lights
    /// on top of light that is already painted in would count the same lamp twice. Every preset above it
    /// lights dynamically and so discards those bakes, which is exactly why they need this: without it a
    /// windowless room has no source at all and reads black.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> LocalLightScale = Presets.Define(
        CVar.Float(
            "r_local_light_scale",
            0f,
            0f,
            64f,
            CVarOptions.Archive,
            "Strength of the level's detected lamp fixtures as real lights. 0 is off."),
        vanilla: "0",
        enhanced: "6",
        high: "6",
        ultra: "6");

    /// <summary>Whether the level's lamps cast shadows, traced against the world like the sun.</summary>
    /// <remarks>
    /// <para>
    /// A punctual light with no shadow term passes through whatever stands between it and the surface.
    /// So a corridor fitting lights the room on the other side of the partition, a floodlight lights the
    /// inside of the building it is bolted to, and the more reach a lamp is given the further that
    /// wrongness spreads -- which is what makes <see cref="LocalLightRange"/> a knob with a ceiling
    /// rather than a number to turn up. This removes that ceiling.
    /// </para>
    /// <para>
    /// One ray per lamp per pixel, and only for the lamps that survive the reach and facing culls, so
    /// indoors it is a handful and outdoors usually none. Traced inside the shading pass rather than in
    /// a pass of its own, which is what keeps it to that: there is no screen-sized buffer to allocate,
    /// no ordering to agree with a second pass, and nothing is spent on a pixel no lamp reaches.
    /// </para>
    /// <para>
    /// Needs an adapter that can trace, and does nothing without one -- the shading pass compiled for
    /// such a device has no trace in it at all. Unlike the sun's shadow this one is OPAQUE: a wire fence
    /// in front of a lamp casts a solid shadow rather than a patterned one.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> LocalLightShadows = Presets.Define(
        CVar.Int(
            "r_local_light_shadows",
            1,
            0,
            1,
            CVarOptions.Archive,
            "Trace the level's lamps against the world so they cannot light through walls."),
        vanilla: "0",
        enhanced: "1",
        high: "1",

        // On wherever the lamps are lit at all, which is every preset above vanilla. It is not really
        // optional there: a punctual light with no shadow term lights through the wall it is bolted to, and
        // a dynamic preset has no bake underneath to disguise it.
        //
        // This was switched off once on a conclusion that turned out to be measured wrongly -- a crop of
        // floor that the desk legitimately shadowed, read through an eye adaptation that moved with the
        // picture. Standing under a lamp with the exposure pinned, the floor beneath it is lit, smooth
        // and clean. tools/lamp-probe.ps1 is that measurement, kept so the next doubt can be settled in
        // two minutes rather than a day.
        ultra: "1");

    /// <summary>Whether the lamp shadow is denoised before the shading reads it.</summary>
    /// <remarks>
    /// <para>
    /// On, which is the only setting anybody should play with: one traced ray per pixel is a coin toss,
    /// and drawing it straight is grain rather than a shadow. This exists so the denoiser can be
    /// measured against its own absence in a single build — the pass still runs and still carries the
    /// depth the shading's upsample needs, it simply stops filtering — which is the only honest way to
    /// say what it is worth.
    /// </para>
    /// <para>
    /// Not archived. A debug lever that persisted itself into settings.cfg would be a shipped
    /// regression waiting for somebody to trip over, and this renderer has already been bitten once by
    /// exactly that.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> LampDenoise = Presets.Define(
        CVar.Int(
            "r_lamp_denoise",
            1,
            0,
            1,
            CVarOptions.None,
            "Filter the traced lamp shadow before shading with it. Off is for measurement only."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>The largest a fixture may be treated as when its shadow ray samples it, in metres.</summary>
    /// <remarks>
    /// <para>
    /// A shadow's penumbra widens with the size of the source casting it, and the size this starts from
    /// is the fixture's <em>extent</em> — a bound on the whole clustered housing, which for a merged
    /// fitting can measure most of two metres. That is not the width of the thing actually emitting, and
    /// treating it as though it were is where the grain came from: the wider the penumbra, the more of
    /// it each of the two taps a pixel can afford has to stand for.
    /// </para>
    /// <para>
    /// A quarter of a metre is about the glass of a real fitting. The softness the eye expects survives
    /// and the variance does not. Raise it for a deliberately diffuse look and expect noise; lower it
    /// towards nought for hard, cheap, obviously-computed edges.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> LocalLightSource = Presets.Define(
        CVar.Float(
            "r_local_light_source",
            0.25f,
            0.02f,
            2f,
            CVarOptions.Archive,
            "Largest a fixture is treated as when sampling its shadow. Wider is softer and noisier."),
        vanilla: "0.25",
        enhanced: "0.25",
        high: "0.25",
        ultra: "0.25");

    /// <summary>How far a detected lamp reaches, against the reach its own size and colour imply.</summary>
    /// <remarks>
    /// <para>
    /// The extractor derives a reach from the fixture's size and brightness, and that derivation is
    /// conservative: it comes from cutting an inverse square at a fixed threshold, so it stops where the
    /// light becomes arithmetically negligible rather than where it stops being visible. On a dark
    /// interior wall the difference is the whole tail of the falloff, and the symptom is a corridor whose
    /// far end is black until the player walks into each lamp's reach in turn — the light appearing to
    /// switch on rather than to fall off.
    /// </para>
    /// <para>
    /// Two doubles every fixture's reach. The near field barely moves — the windowed inverse square is
    /// still an inverse square, and a wider window only lifts what was being cut off at the edge — so
    /// this extends the pool of light rather than brightening the middle of it.
    /// </para>
    /// <para>
    /// It is a knob rather than a bigger constant because there IS an upper bound, and it is not about
    /// taste: these lights cast no shadows. Nothing stops a lamp reaching through the wall it is mounted
    /// on, so a reach much past the room the fixture stands in lights the rooms either side of it too,
    /// and a level lit that way glows from the inside out. Raise it for a big hall; lower it if light is
    /// appearing where a wall should have stopped it.
    /// </para>
    /// <para>
    /// <b>Doubled to four, and the selection no longer scales with it.</b> Raising this used to do two
    /// unrelated things at once: it widened the reach the shader culls and falls off against — which is
    /// what anyone raising it wants — and it also scaled the term <c>LevelLamps.SelectNearest</c> ranks by,
    /// <c>distance - radius * range</c>. The second is why raising it once made lamps go out EARLIER
    /// than before: scaling the radius term scales how much a fixture's own size counts in the
    /// comparison, so big distant battens crowded the small near fittings out of a budget only
    /// sixty-four deep. The ranking now uses the unscaled radius, so this widens the light and nothing
    /// else, and a lamp keeps its slot on where it is rather than on how big it is.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> LocalLightRange = Presets.Define(
        CVar.Float(
            "r_local_light_range",
            3f,
            0.25f,
            8f,
            CVarOptions.Archive,
            "How far a detected lamp reaches, against its derived reach. Higher lights further, and leaks."),
        vanilla: "3",
        enhanced: "3",
        high: "3",

        // Brought down a notch from four now that the lamps are actually shadowed. Four was set to push
        // the light pool past a wall it could not be stopped at, back when these lights cast no shadow of
        // any kind and a corridor's far end went black between fittings; with the walls now stopping the
        // light where they should, the reach no longer has to overshoot to fill a room, and a shorter
        // one leaves less to leak on the paths a shadow ray cannot reach.
        ultra: "3");

    /// <summary>Whether the level's lamps are detected at all: as emission, as lights and in the bounce.</summary>
    /// <remarks>
    /// <para>
    /// The level declares no point lights. Its lamps are worked out from what the artists baked around
    /// each texture and from the SHAPE of the geometry that draws it -- small, above head height, flat,
    /// facing down and painted near-white is a light fixture and very little else -- and the conclusion
    /// then does three things at once: the fixture geometry is made emissive so a lamp looks like the
    /// thing doing the lighting, a punctual light is placed at every one so a windowless room has a
    /// source, and the same fixtures feed the surface cache so that room bounces. All three or none:
    /// a lamp that lights a floor without glowing reads as a room lit by nothing, and a lamp that glows
    /// without lighting reads as a decal.
    /// </para>
    /// <para>
    /// Off is the honest reading of a level with no lamps at all -- a sealed interior goes dark, which is
    /// what it would be. Turn it off to see what the level looks like on daylight alone, or when a
    /// detection has gone wrong in a level whose fixtures file has not been corrected yet.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> GlobalIlluminationLamps = Presets.Define(
        CVar.Int(
            "r_gi_lamps",
            1,
            0,
            1,
            CVarOptions.Archive,
            "Detect the level's lamps: emissive fixtures, real lights at each, and their bounce."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How brightly the detected lamp geometry itself glows, against what the detector proposed.</summary>
    /// <remarks>
    /// <para>
    /// This is the FIXTURE, not the light it casts -- <see cref="LocalLightScale"/> is that. A lamp has to
    /// be brighter in the frame than any surface it lights or it does not read as the source: at its own
    /// albedo a fluorescent panel is a pale grey rectangle with a mysterious pool of light beneath it, and
    /// what makes it a lamp is being written above what a lit surface can reach, where the bloom finds it.
    /// </para>
    /// <para>
    /// One means the strength the detection proposed, which is a lamp at six times its albedo and a
    /// merely-bright surface -- an advert panel, a lit sign -- proportionally less. The two knobs are
    /// separate because they answer different complaints: a room that is too bright wants the light turned
    /// down, and a fixture that looks like a sticker wants this turned up.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> LampEmissiveScale = Presets.Define(
        CVar.Float(
            "r_lamp_emissive",
            1f,
            0f,
            4f,
            CVarOptions.Archive,
            "How brightly detected lamp geometry glows, against the detected strength. 0 stops it glowing."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
