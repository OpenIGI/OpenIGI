using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for the surface cache: the card atlas the real-time GI lights and bounces through.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class SurfaceCacheCVars
{
    /// <summary>How much of the surface cache's bounced radiance a traced gather ray keeps.</summary>
    /// <remarks>
    /// <para>
    /// One is the physical answer: the cache holds real outgoing radiance and a ray that hits a card
    /// should keep all of it. This scales the cache's answer down without touching the sky, which is
    /// resolved separately. Applies to the screen gather and the radiance cache alike.
    /// </para>
    /// <para>
    /// <b>It was 0.15, then 0.35, and both were tuned against a broken target.</b> The target image showed
    /// the office floor at 0.2 out of 255 — black — because the cache that produced it had no coverage
    /// over this level's geometry, so its rays hit surfaces with nothing cached and came back with
    /// nothing. 0.15 existed to reproduce that darkness; 0.35 was half a step back from it, arrived at by
    /// a whole-frame sweep that could not see what the deficit was doing to the exposure.
    /// </para>
    /// <para>
    /// <b>It is back at the physical one, and the measurement that put it there is the fill, not the
    /// frame.</b> Sampled over 1056 office-floor points at fixed exposure with no tone map, the tenth
    /// percentile is the sky-and-bounce fill and the ninetieth is the same surface with the sun on it.
    /// At 0.35 the bounce carried under half the light a physically-normalised bounce delivers; at one
    /// the fill lands about twenty per cent generous while the sunlit end stays within one per cent,
    /// which is the closest a single scalar gets, and it is on the right side: the deficit was what
    /// pinned the auto-exposure against its ceiling.
    /// </para>
    /// <para>
    /// What it costs is the office ceiling, which comes out bright. That region is a strip light this
    /// port lights as an emissive fixture, so most of the excess is the fixture's own emission rather
    /// than bounce this value is delivering; the room's floor, wall, window and sun pool all improve,
    /// and so does the exterior.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> GiBounce = Presets.Define(
        CVar.Float(
            "r_gi_bounce",
            1f,
            0f,
            4f,
            CVarOptions.Archive,
            "Traced bounce strength off the surface cache. 1 is physical; lower gives a sparser, darker bounce."),
        vanilla: "1",
        enhanced: "1",
        high: "1",

        // The physical one wherever the bounce is traced at all, which is now every preset above vanilla.
        // Anything below it is this renderer throwing away light it has already computed, and the fill it
        // was throwing away is what dropped the scene under the metering band.
        ultra: "1");

    /// <summary>How many slices the whole surface cache is refreshed in, one a frame; 0 turns it off.</summary>
    /// <remarks>
    /// The cache near the camera is refreshed every frame within <see cref="GiCacheRadius"/>; this sweep
    /// keeps the REST of it current -- a far surface follows the sun, the lamps and the world filling in
    /// within slices / frame rate seconds, instead of keeping its load-time light until the player walks up
    /// to it. Fewer slices is a faster sweep and a dearer frame.
    /// </remarks>
    public static readonly NumericCVar<int> GiFarRefresh = Presets.Define(
        CVar.Int(
            "r_gi_far_refresh",
            128,
            0,
            4096,
            CVarOptions.Archive,
            "Slices the whole GI cache is refreshed in, one a frame, so far surfaces stay lit correctly. 0 is off."),
        vanilla: "0",

        // Half the per-frame cost on the preset with the least GPU to spare, for a sweep twice as long.
        enhanced: "256",
        high: "128",
        ultra: "128");

    /// <summary>How far from the camera the surface cache keeps refreshing itself, in metres.</summary>
    /// <remarks>
    /// <para>
    /// The cache covers the whole level, and every frame the relight and the bounce advanced a cursor
    /// through it — so a fixed share of every frame went on cards kilometres away, in parts of a map the
    /// player will never stand in. These levels are enormous and a player sees a corner of one; on
    /// mission 1 that was over two milliseconds a frame spent almost entirely outside the room.
    /// </para>
    /// <para>
    /// It was not merely unseen work, it was work that could not produce an answer. The relight traces
    /// its shadows against the distance-field clipmaps, which follow the camera and reach a few hundred
    /// metres; past that there is no field, so a far card re-derived exactly the value it already held.
    /// </para>
    /// <para>
    /// Beyond this radius a card keeps the lighting the load-time seed gave it — the seed still covers
    /// the whole level, so nothing goes dark. What a far card stops doing is tracking change: a lamp
    /// switched across the map will not relight the wall beside it until the player comes within this
    /// distance. Nought restores the old behaviour of refreshing the entire level every cursor round.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> GiCacheRadius = Presets.Define(
        CVar.Float(
            "r_gi_cache_radius",
            60f,
            0f,
            4000f,
            CVarOptions.None,
            "How far from the camera the surface cache keeps refreshing. 0 refreshes the whole level."),

        // Sixty metres, chosen off the measured curve rather than picked. Against a full-level refresh at
        // 1440p on mission 1 it saves a millisecond of the relight and the bounce for an RMSE of 0.39/255
        // -- five times inside this project's 2/255 bar, and a worst single pixel of 29. Forty-five saves
        // half as much again but puts a worst pixel at 71, which is a difference somebody can point at.
        // A hundred and twenty is free and saves nothing: at that reach almost the whole cache is inside.
        vanilla: "60",
        enhanced: "60",
        high: "60",
        ultra: "60");

    /// <summary>How many frames the surface-cache relight spreads its tiles across.</summary>
    /// <remarks>
    /// The real-time GI relight is world-space -- its cost is the same at any output resolution, so it is
    /// the biggest single item in the budget on a high-resolution frame. It traces one slice of the cache a
    /// frame and walks the cursor over this many frames; a static level under a fixed sun re-traces a tile to
    /// the answer it already holds, so a wider spread is nearly free in a converged still and only slows how
    /// fast a newly revealed surface fills its multi-bounce light. Raise it to trade that fill-in latency for
    /// per-frame GPU time (roughly the whole GI stage divides by this), lower it for the snappiest response.
    /// </remarks>
    public static readonly NumericCVar<int> GiAmortize = Presets.Define(
        CVar.Int(
            "r_gi_amortize",
            12,
            2,
            48,
            CVarOptions.Archive,
            "Frames the surface-cache relight spreads its tiles across; higher is cheaper per frame, lags more."),
        vanilla: "12",
        enhanced: "48",
        high: "32",

        // The world-space relight is the one GPU cost a render scale cannot touch -- it is the same work at
        // any output resolution -- so it is the second tier ladder beside r_scale, and it runs the other
        // way: the cheaper the tier, the wider the spread. A converged still is identical whatever this is
        // (the 6-to-12 doubling measured 0.7% mean difference); what a wider spread costs is how quickly
        // newly-revealed geometry fills its bounce, 48 frames at enhanced against 24 at ultra -- under a
        // second either way. Enhanced sits on the declared maximum, so raising that ceiling later is the
        // only way to spread it further.
        ultra: "24");

    /// <summary>The fewest surface-cache tiles one steady-state relight or bounce slice dispatches.</summary>
    /// <remarks>
    /// A floor on the slice, because the tracing hides its ray latency only in a large dispatch. Too high a
    /// floor packs a whole round of work into a few frames and idles the rest, which is a spike every round.
    /// </remarks>
    public static readonly NumericCVar<int> GiMinSlice = CVar.Int(
        "r_gi_min_slice",
        1024,
        64,
        65536,
        CVarOptions.Developer,
        "Fewest surface-cache tiles per amortised GI slice. Lower spreads the work evenly; higher is more efficient.");

    /// <summary>How far the surface cache is converged at load, before the level's first frame is shown.</summary>
    /// <remarks>
    /// <para>
    /// The bounce is a progressive radiosity solve: each pass folds one more bounce into the last one's
    /// result, so the cache climbs towards its multi-bounce equilibrium over the frames after a load
    /// rather than arriving at it. Run in front of the player that is the "lighting up" -- a level that
    /// opens flat and visibly fills with light over the first second or two, which reads as the game
    /// loading badly rather than as a renderer converging.
    /// </para>
    /// <para>
    /// This runs that climb during the load instead, where the frame is already being held: the whole
    /// cache is relit once WITH shadows and this many bounces are folded in, so the first frame the
    /// player sees is the converged one. The direct term matters as much as the bounces -- in the steady
    /// state it is spread over <see cref="GiAmortize"/> frames, so without this a level opens with its
    /// cards unshadowed and darkens as the slices arrive.
    /// </para>
    /// <para>
    /// It could not be done before the distance fields were cached: converging needs something to trace
    /// shadows against, and the field used to arrive seconds after the level did. Zero restores the old
    /// ramp, which is also what a device with no cached field falls back to.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> GiConverge = Presets.Define(
        CVar.Int(
            "r_gi_converge",
            8,
            0,
            32,
            CVarOptions.Archive,
            "Bounces folded into the surface cache at load, so a level opens lit rather than filling in."),

        // Vanilla has no surface cache to converge.
        vanilla: "0",

        // Eight everywhere else. The blend weight is one over one plus the pass, so the first few carry
        // almost all of the difference and the tail is not worth the load time -- the cvar's own notes
        // put bounces three, four and five as the ones that fill a room the sun never reaches.
        enhanced: "8",
        high: "8",
        ultra: "8");

    /// <summary>How many frames the surface-cache relight's temporal accumulation converges over.</summary>
    /// <remarks>
    /// <para>
    /// The cap on the relight's accumulation weight: the blend's alpha bottoms out at one over this, so a
    /// tile keeps adapting to a moving sun rather than freezing, while integrating far more hemisphere
    /// directions than the sixteen a single trace covers.
    /// </para>
    /// <para>
    /// This is the knob for the slow travelling ripple over interior surfaces -- the "heat haze", visible
    /// with the camera standing perfectly still. Each time the relight's cursor reaches a tile it blends a
    /// freshly traced estimate in at this alpha, and that estimate does not agree with the stored one: a
    /// tile gathers from its neighbours' FINAL lighting, and under <see cref="GiAmortize"/> each neighbour
    /// was last updated at a different point in the cursor's sweep, so every tile integrates a slightly
    /// different snapshot of the room. The disagreement is systematic rather than random -- raising the
    /// bounce gather's ray count from sixteen to thirty-six moved the shimmer 1.5%, while halving the
    /// amortisation (twice the retrace rate) doubled it -- so the only lever that reaches it is how much of
    /// one retrace is admitted. Thirty-two measured a 2.1x reduction against the old eight.
    /// </para>
    /// <para>
    /// The cost is adaptation latency. With <see cref="GiAmortize"/> at 24 a tile is retraced every
    /// twenty-four frames, so the accumulation's time constant runs about 3.5 seconds at eight and about
    /// thirteen at thirty-two. That is only the multi-bounce term settling behind a sun the day/night keys
    /// are actively scrubbing; the direct sun, its shadows and the screen gather's own trace are immediate.
    /// Lower it if a scrubbed sun matters more than a still interior.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> GiTemporalFrames = Presets.Define(
        CVar.Int(
            "r_gi_temporal_frames",
            8,
            1,
            64,
            CVarOptions.Archive,
            "Frames the surface-cache relight converges over; higher is steadier, adapts slower."),

        // Paired with r_gi_amortize rather than chosen against it. What the eye actually experiences is the
        // wall-clock time constant, which is the product of the two -- a tile is retraced every r_gi_amortize
        // frames and each retrace is admitted at one over this -- so the ladder holds that product roughly
        // even at about 770 frames: 48x16 at enhanced, 32x24 at high, 24x32 at ultra. A tier therefore buys
        // its cheaper relight without also becoming slower to settle, and vanilla keeps 8 because its
        // relight is not running at all.
        vanilla: "8",
        enhanced: "16",
        high: "24",

        // Highest at ultra, which retraces most often and so can afford to admit the least of each retrace.
        // This is the knob the flat-interior-wall shimmer answers to; thirty-two measured a 2.1x reduction
        // against the old eight.
        ultra: "32");

    /// <summary>Whether every model's surface is captured into the card atlas.</summary>
    /// <remarks>
    /// <para>
    /// The structure that lets light bounce off geometry the camera cannot see: a ray hitting a model
    /// reads its captured surface rather than needing the model drawn. On at every preset that gathers,
    /// which is all of them above vanilla; off at vanilla, where an atlas nothing samples would be load
    /// time and video memory spent on nothing.
    /// </para>
    /// <para>
    /// <b>No longer restart-scope.</b> It was, along with <see cref="SurfaceCacheDensity"/>,
    /// <see cref="RayTracingCVars.DistanceFields"/> and <see cref="RayTracingCVars.DistanceFieldResolution"/> — and that made switching a
    /// preset a trap rather than a setting, because <c>r_lighting dynamic</c> applies at once and DISCARDS
    /// the bakes while the thing meant to replace them could not arrive until the next launch. The frame
    /// went flat and nothing said why. The capture is now made on demand and the lighting model waits for
    /// it; see <c>DesktopGame.RealtimeGi.cs</c>. Turning it on mid-level costs one long frame, because the
    /// capture is a render pass over every distinct model and cannot be anything else.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar SurfaceCache = Presets.Define(
        CVar.Bool(
            "r_cards",
            false,
            CVarOptions.Archive,
            "Capture every model's surface so light can bounce off geometry the camera cannot see."),
        vanilla: "0",
        enhanced: "1",
        high: "1",

        // The real-time global illumination is built on the surface cache: the screen probe gather and the
        // radiance cache both trace it for the indirect fill, so every preset that runs them pays to
        // capture it. Only vanilla, which lights from the artists' bakes, never reads a card.
        //
        // What the tiers vary is r_cards_density rather than whether the atlas exists, because the
        // gather cannot fall back to anything if it is missing -- see the note there.
        ultra: "1");

    /// <summary>How many texels of card a world unit of surface is given.</summary>
    /// <remarks>
    /// The dial that decides both the atlas size and how much detail a bounce carries. Higher resolves
    /// a doorway rather than averaging over a wall, and costs the square of itself — paid at load and in
    /// video memory rather than per frame, which is what makes it a sensible thing to ladder by tier. It is
    /// also the first thing a fresh install pays for, enhanced being the preset applied when no settings
    /// file exists, so the cheapest tier is deliberately close to the floor.
    /// </remarks>
    public static readonly NumericCVar<float> SurfaceCacheDensity = Presets.Define(
        CVar.Float(
            "r_cards_density",
            8f,
            1f,
            64f,
            CVarOptions.Archive,
            "Texels of card per world unit of surface."),
        vanilla: "8",
        enhanced: "12",
        high: "18",
        ultra: "23.05");

    /// <summary>Where to write the captured atlas as images, or empty to write none.</summary>
    /// <remarks>
    /// A capture can only be judged by looking at it. Wrong winding shows as models captured
    /// inside-out, a wrong card rotation as mirrored pages, a missing scissor as pages bleeding into
    /// each other — and every one of those produces an atlas full of entirely plausible surfaces.
    /// </remarks>
    public static readonly StringCVar SurfaceCacheDump = CVar.Text(
        "r_cards_dump",
        string.Empty,
        CVarOptions.Developer,
        "Directory to write the captured card atlas to as PNGs; empty writes none.");

    /// <summary>What the lighting pass writes into its atlas: light, or a chosen diagnostic.</summary>
    /// <remarks>
    /// A shadow trace that fails leaves nothing to look at — a black atlas is equally consistent with a
    /// wrong world position, an unbound sampler, a uniform that never arrived and a field that is
    /// genuinely closed over. Each probe writes one of those intermediates where the light would have
    /// gone, so the dump can tell them apart: 1 writes what the trace sees at its start point, 2 the
    /// reconstructed world position within the outermost clipmap, 3 the world normal, 4 how the trace
    /// ended, 5 whether the card index sends a texel back to itself.
    /// <para>
    /// <b>Keep the maximum in step with the shader.</b> This clamps, silently and by design, so a probe
    /// added to the shader but not added here runs as whichever probe the clamp lands on -- which
    /// presents as the new probe reporting nothing whatsoever, and is indistinguishable from the thing
    /// it was measuring being broken.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> SurfaceCacheProbe = CVar.Int(
        "r_cards_probe",
        0,
        0,
        5,
        CVarOptions.Developer,
        "Diagnostic the card lighting writes instead of light: 0 none, 1 trace start, 2 position, "
        + "3 normal, 4 trace outcome, 5 card index self-test.");

    /// <summary>The diffuse colour boost the surface cache reflects light through.</summary>
    /// <remarks>
    /// The single lever for how bright the bounced light is. Each time light reflects off a card, the
    /// surface's albedo is raised by <c>pow(luminance, boost)</c> before it passes light along, so a
    /// <b>lower</b> value lifts a dark, low-albedo surface's reflectance towards one and lets a sunless
    /// interior carry far more indirect -- which the raw retail albedos (~0.4) otherwise swallow after a
    /// bounce or two. One is off (physical albedo). It never reaches one (the surface would return
    /// everything it received and the feedback would diverge); the shader clamps it below. This is what to
    /// reach for when interiors read dark, ahead of exposure or more bounces.
    /// </remarks>
    public static readonly NumericCVar<float> GlobalIlluminationBoost = Presets.Define(
        CVar.Float(
            "r_gi_boost",
            0.8f,
            0.1f,
            1f,
            CVarOptions.Archive,
            "Diffuse colour boost for bounced light: LOWER lifts dark surfaces so interiors carry more indirect. 1 is off."),
        vanilla: "0.8",

        // Physical on the presets that gather. The lift was set while the cache took vertex colour raw --
        // brighter than the screen -- and while the sky filled every shadow three times over; with both
        // put right it only flattened the bounce: dark surfaces passing light along like pale ones, the
        // corners of a room no darker than its middle.
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
