using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Services;

/// <summary>
/// The real-time lighting model's structures: the level's distance field and the lit surface cache, built when the model is
/// asked for, converged so a level opens lit, kept current as the sun and the lamps change, and the lamps they light from.
/// </summary>
/// <remarks>
/// The coordinator. The distance field is held by <see cref="DistanceFieldResidency"/>, the lit surface
/// cache and its convergence by <see cref="SurfaceCacheResidency"/>, and a frame's share of the lighting by
/// <see cref="SceneLightingAssembler"/>; what is left here is the lamps they all light from and the
/// decision of which lighting model the frame may use.
/// </remarks>
internal sealed class GlobalIllumination : IHostService
{
    private readonly IGraphicsDevice _device;
    private readonly IGlobalIlluminationHost _host;
    private readonly DistanceFieldResidency _fields;
    private readonly SurfaceCacheResidency _cards;
    private readonly SceneLightingAssembler _sceneLighting;

    /// <summary>Initializes a new instance of the <see cref="GlobalIllumination"/> class.</summary>
    /// <param name="device">The device the structures live on.</param>
    /// <param name="logger">Where builds and convergence are reported.</param>
    /// <param name="work">The workers the distance field bakes on.</param>
    /// <param name="lockstep">Whether frames are captured in lockstep, which runs the convergence in one go.</param>
    /// <param name="host">The level, its sun and the caches.</param>
    public GlobalIllumination(
        IGraphicsDevice device,
        IOpenIgiLogger logger,
        IWorkScheduler work,
        bool lockstep,
        IGlobalIlluminationHost host)
    {
        _device = device;
        _host = host;

        // A field taken into use is half of the pair the convergence waits on, and a field thrown away
        // takes the convergence against it with it -- first, as the field discard always did.
        _fields = new DistanceFieldResidency(
            device,
            logger,
            work,
            host,
            adopted: () => _cards!.BeginConvergence(),
            discarding: () => _cards!.ResetConvergence());
        _cards = new SurfaceCacheResidency(device, logger, lockstep, host, _fields, () => LightFixtures);
        _sceneLighting = new SceneLightingAssembler(device, logger, host, _fields, _cards);
    }

    /// <summary>How long after the sun last moved the GI keeps following it closely, in frames.</summary>
    /// <remarks>
    /// Long enough for the near cache to be relit a few times over at <c>GameHost.GiSunFollowSpread</c>, so a
    /// scrub that stops lands on the new hour's light and the slow blend takes over from there.
    /// </remarks>
    public const int GiSunFollowFrames = 24;

    // How long that warm-up lasts. The radiance cache's running average settles over maxFrames sweeps and
    // one sweep is spread over several frames, so full convergence is a couple of seconds; this covers it.
    /// <summary>How long the exposure warm-up lasts after the cache is (re)built, in frames.</summary>
    public const int GiExposureWarmupFrames = 150;

    /// <summary>Gets the lit surface cache, once captured and lit.</summary>
    public SurfaceCacheLighting? CardLighting => _cards.CardLighting;

    /// <summary>Gets the distance-field bake in flight, if any.</summary>
    public DistanceFieldBake? FieldBake => _fields.FieldBake;

    /// <summary>Gets the level's exact distance field, which the clipmaps are checked against.</summary>
    public SceneDistanceField? DistanceFieldScene => _fields.DistanceFieldScene;

    /// <summary>Gets the clipmaps the level's distance field is resampled into around the camera, once baked.</summary>
    public GlobalDistanceField? DistanceField => _fields.DistanceField;

    /// <summary>Gets or sets whether the clipmaps have been checked against the exact field this level.</summary>
    public bool FieldValidated
    {
        get => _fields.FieldValidated;
        set => _fields.FieldValidated = value;
    }

    /// <summary>Gets or sets the switchboard revision the cache's emitters last matched.</summary>
    public int LampBounceRevision
    {
        get => _cards.LampBounceRevision;
        set => _cards.LampBounceRevision = value;
    }

    /// <summary>Gets or sets the frames left in the exposure warm-up after the cache is (re)built.</summary>
    public int ExposureWarmup
    {
        get => _cards.ExposureWarmup;
        set => _cards.ExposureWarmup = value;
    }

    /// <summary>Gets or sets how many times the bounce has been relit since the cache was (re)built.</summary>
    public int CardBounceFrame
    {
        get => _cards.CardBounceFrame;
        set => _cards.CardBounceFrame = value;
    }

    /// <summary>Gets or sets the frames since the sun last moved, for the fast follow a clock scrub needs.</summary>
    public int SunMovedFrames
    {
        get => _cards.SunMovedFrames;
        set => _cards.SunMovedFrames = value;
    }

    // Which sub-meshes of this level are lamps, so a shared texture glows on the fixture and not on the
    // furniture that happens to be skinned with the same page. Held here because the world renderer is
    // rebuilt per mission and this outlives the rebuild.
    /// <summary>Gets which sub-meshes of the level are lamps.</summary>
    public LampDetector.Lamps Lamps { get; private set; } = LampDetector.Lamps.None;

    // The level's inferred lamp fixtures, kept so the frame loop can submit them to the deferred lighting
    // as real point lights each frame (transformed into view space there), the honest way to light an
    // interior the screen gather cannot reach. Also bounced through the surface cache. Empty until a
    // mission loads one; see EmissiveLightProposer.
    /// <summary>Gets the level's lamps, as fixtures.</summary>
    public IReadOnlyList<LightFixture> LightFixtures { get; private set; } = [];

    // Which of those lamps are actually lit at this moment, and how far through switching on or out they
    // are. Built with the fixtures, and read by all three of the things a lamp is -- the punctual light
    // below, the emission the world renderer puts on the fixture's own geometry, and the emitters the
    // surface cache bounces off -- so none of them can disagree about a lamp being off. Null until a
    // mission loads fixtures; see LampSwitchboard.
    /// <summary>Gets what decides each lamp's output, once a level has fixtures.</summary>
    public LampSwitchboard? LampSwitchboard { get; private set; }

    // Which mission's lamps are applied, so a promotion does not survey a level whose backdrop already
    // did -- that survey was the largest single cost of pressing Play.
    /// <summary>Gets the mission whose lamps are applied.</summary>
    public int LampsMissionId { get; private set; } = -1;

    /// <inheritdoc/>
    public string Name => "Global illumination";

    /// <summary>
    /// The lighting model the frame may actually use, which is the one asked for only once the structures
    /// it lights from exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>dynamic</c> DISCARDS the artists' bakes: it does not sample an object lightmap at all. Everything
    /// it puts in their place — the screen probe gather, the world radiance cache, the lamps — reads the
    /// surface cache and the distance field, and both of those are built rather than switched on. So the
    /// instant between asking for dynamic lighting and having something to light with is a frame with the
    /// bakes thrown away and nothing yet to replace them: a flat, wrong picture.
    /// </para>
    /// <para>
    /// That instant used to be permanent. <c>r_cards</c> and <c>r_sdf</c> were <c>RequiresRestart</c> and
    /// built only during a mission load, so choosing Ultra from the settings menu mid-session — or moving
    /// the Global Illumination row — asked for a lighting model whose inputs could not arrive until the
    /// player restarted the game. Nothing said so; the frame simply went flat.
    /// </para>
    /// <para>
    /// Reading the model through here instead makes the switch safe from both ends. <see cref="EnsureStructures"/>
    /// starts building the moment the model is asked for, and until it finishes this reports
    /// <see cref="LightingModel.Baked"/> — so the frame keeps the lighting it already had and crosses over
    /// when there is something better to cross to. There is never a wrong frame, only an early one.
    /// </para>
    /// </remarks>
    public LightingModel EffectiveLighting =>
        LightingCVars.Lighting.Value is var wanted && wanted == LightingModel.Baked ? wanted
        : Ready ? wanted
        : LightingModel.Baked;

    /// <summary>Whether everything the dynamic model lights from is built and usable.</summary>
    /// <remarks>
    /// Both structures, not either: a gather with a distance field and no surface cache traces to a hit it
    /// cannot read the colour of, and one with a cache and no field cannot find the hit at all. The device
    /// check is the same one the builders make — without compute there is no field to build, so a level
    /// loaded on such a device stays on the bakes however the preset is set.
    /// </remarks>
    public bool Ready =>
        CardLighting is not null && DistanceField is not null && _device.Capabilities.SupportsCompute;

    /// <summary>Whether the frame wants the dynamic model at all, on a device that could run it.</summary>
    public bool Wanted =>
        LightingCVars.Lighting.Value == LightingModel.Dynamic && _device.Capabilities.SupportsCompute;

    /// <summary>
    /// Whether anything the dynamic model needs is still on its way: a field baking or not yet attempted, a
    /// cache not yet captured, or a convergence that has not finished.
    /// </summary>
    /// <remarks>
    /// This is what a loading screen waits on. It is deliberately "still coming" rather than "not ready":
    /// a level whose capture found nothing, or whose bake produced nothing, is not going to change, and a
    /// gate that waited for it would hold a panel over a level that was as lit as it would ever be.
    /// </remarks>
    public bool Pending
    {
        get
        {
            if (!Wanted || _host.Scene is null)
            {
                return false;
            }

            bool fieldPending = _fields.IsPending;
            bool cardsPending = _cards.IsCapturePending;
            bool convergencePending = _cards.IsConvergencePending(Ready);

            return fieldPending || cardsPending || convergencePending;
        }
    }

    /// <summary>Whether a convergence is stepping right now.</summary>
    public bool IsConverging => _cards.IsConverging;

    /// <summary>How far the convergence has come, or one when there is none to wait for.</summary>
    public float ConvergeProgress => _cards.ConvergeProgress;

    /// <summary>
    /// Builds whatever the real-time lighting model needs and does not yet have, and throws away whatever
    /// it no longer needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called once a frame from the render loop as well as during a mission load, which is what lets the
    /// settings menu turn real-time global illumination on in a running level. The work is the same work
    /// the load does; only its trigger is new.
    /// </para>
    /// <para>
    /// The costs are not symmetrical and it matters which is which. The distance-field bake is a worker
    /// thread that the loop already polls, so asking for it mid-session costs nothing on the frame it is
    /// asked for. The surface-cache capture is a render pass over every distinct model and cannot be
    /// anything else — the atlas is a framebuffer and the geometry belongs to this context — so it lands as
    /// one long frame. That is a visible hitch, and it is the honest price of the switch; it is logged so
    /// that a hitch nobody asked about can be explained afterwards.
    /// </para>
    /// <para>
    /// A backdrop counts. The menu's level used to be a picture and never gathered, which meant it was lit
    /// by the artists' bakes while the mission it became was lit by the real-time model -- and the picture
    /// changed the moment Play was pressed. Building the structures for the backdrop too is what makes
    /// the menu show the level the player is about to be standing in.
    /// </para>
    /// </remarks>
    public void EnsureStructures()
    {
        // First, because the lite backdrop's overrides decide whether either structure is wanted at all.
        _host.UpdateBackdropProfile();

        if (_host.Scene is null || !_host.HasLevel)
        {
            return;
        }

        EnsureDistanceFields();
        EnsureSurfaceCache();
    }

    /// <summary>Bakes or reads the level's distance field if it is wanted and missing, or throws it away if not.</summary>
    public void EnsureDistanceFields() => _fields.EnsureDistanceFields();

    /// <summary>Takes a baked field into use, and writes it to the cache when it was freshly baked.</summary>
    /// <param name="scene">The level's field.</param>
    /// <param name="atlas">Every distinct model's field, packed for the GPU.</param>
    /// <param name="fromCache">Whether this came off disk, in which case there is nothing to write back.</param>
    public void AdoptDistanceField(SceneDistanceField scene, DistanceFieldAtlas atlas, bool fromCache) =>
        _fields.AdoptDistanceField(scene, atlas, fromCache);

    /// <summary>Captures and lights the level's surface cache if it is wanted and missing, or throws it away if not.</summary>
    public void EnsureSurfaceCache() => _cards.EnsureSurfaceCache();

    /// <summary>Runs this frame's share of the convergence, if one is in flight. Once per frame.</summary>
    public void StepConvergence() => _cards.StepConvergence();

    /// <summary>Throws the level's distance field away, and whatever was converging against it.</summary>
    public void DiscardDistanceFields() => _fields.DiscardDistanceFields();

    /// <summary>Throws the level's surface cache away, and whatever was converging it.</summary>
    public void DiscardSurfaceCache() => _cards.DiscardSurfaceCache();

    /// <summary>Puts a level's proposed lamps into every place that reads them.</summary>
    /// <param name="lamps">The level's fixtures and glowing geometry, or none.</param>
    /// <param name="missionId">The mission they belong to, which seeds the switchboard.</param>
    /// <remarks>
    /// The lamps a level implies but never declares, used in three places: as EMISSION on the fixture
    /// geometry itself, so a lamp looks like the thing doing the lighting; as real lights in the deferred
    /// pass, so a windowless room has a source at all; and as emitters the surface cache bounces off,
    /// where there is one. All three matter together -- a room lit from a fixture that does not glow
    /// reads as lit by nothing, and a fixture that glows without lighting reads as a decal.
    /// Shared by the backdrop and the played mission, so a promoted level keeps the lamps its backdrop
    /// was already showing rather than surveying them a second time.
    /// </remarks>
    public void ApplyLamps(EmissiveLightProposer.Proposal lamps, int missionId)
    {
        LampsMissionId = missionId;
        LightFixtures = lamps.Fixtures;

        // One switchboard per set of fixtures, seeded by the mission so two levels' broken tubes do not
        // stutter in step. Everything that reads a lamp's output reads it from here, and everything that
        // holds one is handed the same instance rather than a copy of its state.
        LampSwitchboard = new LampSwitchboard(lamps.Fixtures, missionId);
        LampBounceRevision = LampSwitchboard.Revision;

        // Which sub-meshes glow and how brightly. Per sub-mesh rather than per texture, because one
        // texture is not one brightness -- a floodlight and the lamp over a door draw with the same page
        // -- and because a lamp's page also skins the furniture beside it. r_lamp_emissive scales the
        // whole set on the way in rather than in the shader: it is a property of the material, so it
        // belongs where the material is decided and costs nothing per pixel.
        float emissiveScale = LocalLightCVars.LampEmissiveScale.Value;

        Lamps = emissiveScale == 1f
            ? lamps.Lamps
            : lamps.Lamps with
            {
                SubMeshes = lamps.Lamps.SubMeshes.ToDictionary(
                    e => e.Key, e => e.Value * emissiveScale, StringComparer.OrdinalIgnoreCase),
            };

        if (_host.World is not null)
        {
            _host.World.Lamps = Lamps;
            _host.World.Switchboard = LampSwitchboard;
        }

        // The lit cache reads the fixtures as area emitters; one built before they were known has to be
        // told about them. The refresh sweep carries them to the far cards.
        CardLighting?.SetLocalLights(LightFixtures);
    }

    /// <summary>Forgets the structures a session is about to replace, without freeing them.</summary>
    public void Forget()
    {
        _cards.Forget();
        _fields.Forget();
    }

    /// <summary>Takes a finished distance-field bake out of flight.</summary>
    /// <param name="baked">Receives what it produced.</param>
    /// <returns><see langword="true"/> when one finished.</returns>
    public bool TryCollectFieldBake(out DistanceFieldBake.Result baked) => _fields.TryCollectFieldBake(out baked);

    /// <summary>Waits for the bake in flight and adopts it, as a capture must rather than race the worker.</summary>
    public void WaitForFieldBake() => _fields.WaitForFieldBake();

    /// <summary>Takes a backdrop's distance field, read or still baking, as the level's own.</summary>
    /// <param name="resolution">The resolution it was read or is being baked at.</param>
    /// <param name="read">The field read from the cache, if it was there.</param>
    /// <param name="baking">The bake in flight, if it was not.</param>
    public void AdoptBackdropField(int resolution, SceneDistanceField? read, DistanceFieldBake? baking) =>
        _fields.AdoptBackdropField(resolution, read, baking);

    /// <inheritdoc/>
    public void Dispose()
    {
        _cards.Dispose();
        _fields.Dispose();
    }

    /// <summary>Gathers the screen-space indirect light and resolves the deferred lighting.</summary>
    /// <param name="state">The frame loop's state, which holds the screen gather's histories.</param>
    /// <param name="eye">The camera the frame is drawn from.</param>
    /// <param name="captured">Whether the G-buffer was captured this frame.</param>
    /// <param name="sceneTarget">The scene target the G-buffer was drawn at.</param>
    /// <param name="unjitteredProjection">The frame's projection without its jitter.</param>
    public void LightScene(FrameLoopState state, Camera3D eye, bool captured, SceneTargetDescription sceneTarget, Matrix4f unjitteredProjection) =>
        _sceneLighting.LightScene(state, eye, captured, sceneTarget, unjitteredProjection);

    /// <summary>Hands the device this frame's indirect light: the probe field, the sky's fill, the surface cache and the distance field.</summary>
    /// <param name="state">The frame loop's state.</param>
    /// <param name="eye">The camera the frame is drawn from.</param>
    /// <param name="light">The sun the frame lights with.</param>
    public void PrepareSceneLighting(FrameLoopState state, Camera3D eye, DirectionalLightSettings light) =>
        _sceneLighting.PrepareSceneLighting(state, eye, light);
}
