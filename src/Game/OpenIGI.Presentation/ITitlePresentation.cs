using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>What one draw of the world should include.</summary>
/// <param name="MinimumRadius">
/// Objects whose bounding radius is below this are skipped. The shadow cascades use it to drop casters
/// too small to read at their resolution; a main pass passes zero.
/// </param>
/// <param name="OcclusionCulling">Whether the previous frame's visibility results may reject objects.</param>
/// <param name="GlassOnly">
/// Whether to draw only the level's glazing -- half of a texture on a placement whose other half went
/// into the G-buffer, so it is a pass of its own rather than part of the rotor pass beside it.
/// </param>
/// <param name="RotorsOnly">
/// Draws only the placements <see cref="WorldPlacement.IsRotor"/> marks, and nothing else. A spinning
/// rotor disc is alpha-blended and has to be laid over the world after the opaque pass rather than
/// sorted into it, so the host issues a second draw for them. A title whose scenes mark no rotors draws
/// nothing and costs a walk of the list.
/// </param>
public readonly record struct WorldDrawOptions(
    float MinimumRadius = 0f,
    bool OcclusionCulling = false,
    bool RotorsOnly = false,
    bool GlassOnly = false);

/// <summary>Draws a title's static world geometry.</summary>
/// <remarks>
/// <para>
/// The host owns the frame and the order of its passes; the title owns what each pass draws. That is
/// the original engine's own <c>RenderMode</c> seam moved up one level — the engine always drew through
/// a registered mode, and it always drew <em>this game's</em> world through it.
/// </para>
/// <para>
/// <b>This is a census, not a design.</b> Every member is one the host already used on I.G.I. 1's world
/// renderer, and there are exactly as many as it used — 24 of the 24 that class made public. Inventing a
/// smaller surface would have meant leaving the host holding the concrete type for whatever was left
/// out, which is the thing this interface exists to stop.
/// </para>
/// <para>
/// The bindings below are the part a second title will use least. They are what a level hands the world
/// so it can animate the kinds of object that level contains, and every one is nullable for that exact
/// reason: I.G.I. 2 declares no <c>Elevator</c> task at all, and a title binds what it has. A null
/// binding is not a missing feature, it is a game without that kind of object in it.
/// </para>
/// </remarks>
public interface IWorldPresenter : IDisposable
{
    /// <summary>Re-reads every texture this presenter holds, after the override folder has changed.</summary>
    /// <remarks>
    /// A replacement pack is dropped in while the game runs and <c>r_overrides</c> is toggled; what it
    /// replaces has already been uploaded, so each presenter has to be told to read its own again.
    /// Defaulted to nothing, for a title that caches none.
    /// </remarks>
    void ReloadTextures()
    {
    }

    /// <summary>Gets how many sub-meshes the last draw issued.</summary>
    int LastDrawCount { get; }

    /// <summary>Gets how many triangles the last draw issued.</summary>
    int LastTriangleCount { get; }

    /// <summary>Gets how many of the last draw's sub-meshes were lightmapped.</summary>
    int LastLightmappedCount { get; }

    /// <summary>Gets how many placements the last draw rejected as hidden.</summary>
    int LastOccludedCount { get; }

    /// <summary>Gets or sets what each of the level's textures is made of.</summary>
    MaterialLibrary Materials { get; set; }

    /// <summary>Gets or sets how the title composites the objects this presenter draws.</summary>
    /// <remarks>
    /// Read off the level through <see cref="ITitlePresentationProvider.ReadObjectShading"/> and applied
    /// per sub-mesh: a baked lightmap is multiplied by the title's gain, and a sub-mesh with no bake is
    /// lit by the title's fixed-function sun if it has one. <see cref="ObjectShading.Default"/> is the
    /// compositing the port always had, and what a title that answers nothing gets.
    /// </remarks>
    ObjectShading Shading { get; set; }

    /// <summary>Gets or sets the live rotating world objects, or null for a title with none.</summary>
    /// <remarks>
    /// Radar dishes and the authored <c>Rotating</c> tasks. The scene walk places them; this turns them,
    /// so the presenter asks it for a transform per draw rather than holding an angle of its own.
    /// </remarks>
    RotatingObjectRegistry? RotatingObjects { get; set; }

    /// <summary>Gets or sets the detail textures the world is shaded with, or null for none.</summary>
    MaterialDetailPack? DetailMaps { get; set; }

    /// <summary>Gets or sets the shared decode of the level's textures, or null to decode inline.</summary>
    TextureDecodeCache? Decoded { get; set; }

    /// <summary>Gets or sets where the level's lamps are, so a detected emitter may glow.</summary>
    LampDetector.Lamps Lamps { get; set; }

    /// <summary>Gets or sets which of those lamps are lit, or null for all of them.</summary>
    LampSwitchboard? Switchboard { get; set; }

    /// <summary>Gets or sets the level's doors, or null when it has none.</summary>
    DoorRegistry? Doors { get; set; }

    /// <summary>Gets or sets the level's elevators, or null when it has none.</summary>
    ElevatorRegistry? Elevators { get; set; }

    /// <summary>Gets or sets the level's alarm speakers, or null when it has none.</summary>
    AlarmSpeakerRegistry? AlarmSpeakers { get; set; }

    /// <summary>Gets or sets the level's destructible objects, or null when it has none.</summary>
    DestructibleObjectRegistry? Destructibles { get; set; }

    /// <summary>Gets or sets the level's security cameras, or null when it has none.</summary>
    SecurityCameraRegistry? SecurityCameras { get; set; }

    /// <summary>Gets or sets the placements that turn on the spot, or null when none do.</summary>
    IPlacementSpin? Spins { get; set; }

    /// <summary>Gets or sets the placements a title's own objects move, or null when none do.</summary>
    IPlacementMotion? Motions { get; set; }

    /// <summary>Gets or sets which placements a title's own objects have destroyed, or null when none can be.</summary>
    IPlacementDestruction? Destruction { get; set; }

    /// <summary>Gets or sets the level's pickups, or null when it has none.</summary>
    GenericPickupRegistry? GenericPickups { get; set; }

    /// <summary>
    /// Gets or sets the level's direct interactions: GenericTBA destruction and the stationary-gun
    /// stands that turn with their gun. Null when the level has none.
    /// </summary>
    WorldInteractionRegistry? Interactions { get; set; }

    /// <summary>Gets or sets the cut-scene runtime that poses animated placements, or null for none.</summary>
    CutsceneRuntime? Cutscenes { get; set; }

    /// <summary>Prepares the frame, interpolating animated placements between ticks.</summary>
    /// <param name="tickFraction">How far the frame sits between the previous tick and the next.</param>
    /// <param name="smooth">Whether to interpolate at all.</param>
    void BeginFrame(float tickFraction, bool smooth);

    /// <summary>Draws the world's placed objects from one viewpoint.</summary>
    /// <param name="scene">The placed objects to draw.</param>
    /// <param name="camera">Where the world is seen from.</param>
    /// <param name="projection">The projection to draw under.</param>
    /// <param name="options">What the draw should include.</param>
    void Draw(WorldScene scene, in Camera3D camera, in Matrix4f projection, in WorldDrawOptions options);

    /// <summary>Issues the visibility queries the next frame's culling reads.</summary>
    /// <param name="camera">Where the world is seen from.</param>
    void IssueOcclusionQueries(in Camera3D camera);

    /// <summary>Draws one placement's silhouette.</summary>
    /// <param name="scene">The scene the placement belongs to.</param>
    /// <param name="camera">Where it is seen from.</param>
    /// <param name="placementIndex">Which placement to outline.</param>
    /// <param name="colour">The outline colour.</param>
    /// <param name="radius">How far the outline is pushed out.</param>
    /// <param name="throughWalls">Whether the outline shows through geometry in front of it.</param>
    /// <returns><see langword="false"/> when the index names no placement.</returns>
    bool DrawOutline(
        WorldScene scene,
        Camera3D camera,
        int placementIndex,
        Bgra32 colour,
        float radius,
        bool throughWalls);

    /// <summary>Gets where each placement is drawn this frame: its door, elevator, cut-scene or spin pose.</summary>
    /// <remarks>What a mark held in a placement's frame is drawn against, so it moves with the object.</remarks>
    PlacementPose DrawPoses { get; }

    /// <summary>Gets every placement that has been drawn away from where it was placed since the scene loaded.</summary>
    /// <remarks>Sticky, so the frame one comes to rest on still counts it. What the surface cache carries.</remarks>
    IReadOnlyCollection<WorldPlacement> Movers { get; }

    /// <summary>Uploads one placement's geometry now rather than on the frame that first needs it.</summary>
    /// <param name="scene">The scene the placement belongs to.</param>
    /// <param name="index">Which placement to make resident.</param>
    /// <returns><see langword="false"/> when the index names no placement.</returns>
    bool MakeResident(WorldScene scene, int index);

    /// <summary>Supplies lightmaps baked outside the shipped data, or clears them.</summary>
    /// <param name="baked">The baked maps, or null to use only what the level shipped.</param>
    /// <param name="discardRetail">Whether the level's own lightmaps are dropped in favour of these.</param>
    void SetBakedLightmaps(BakedLightmaps? baked, bool discardRetail);

    /// <summary>Renders each distinct model in the scene into the surface cache the light bake reads.</summary>
    /// <param name="scene">The scene to capture.</param>
    /// <param name="cardResolution">How many cards to spend per unit of surface.</param>
    /// <returns>The capture and its per-placement instances, or null when there was nothing to capture.</returns>
    /// <remarks>
    /// Here rather than on a bake helper because only the world knows how its models resolve to geometry
    /// and textures, and the capture reuses that resolve rather than repeating it. A title with no such
    /// resolve returns null, which the caller already treats as "nothing to bake".
    /// </remarks>
    (SurfaceCacheCapture.Result Capture, SurfaceCacheInstances? Instances)? CaptureSurfaceCache(
        WorldScene scene,
        float cardResolution);

    /// <summary>Appends the presenter's per-frame counters, one line each.</summary>
    /// <param name="into">Receives the lines, already formatted.</param>
    void Describe(ICollection<string> into);
}

/// <summary>What a title needs in order to build an <see cref="IWorldPresenter"/>.</summary>
/// <param name="Device">The graphics device to upload through.</param>
/// <param name="Resources">The registry models and textures are resolved through.</param>
/// <param name="Logger">Where load diagnostics go.</param>
/// <remarks>
/// Deliberately thin. Everything else a world needs arrives afterwards, through the presenter's own
/// properties, because the host sets each one at the point in the mission load where the thing it binds
/// was built — and that ordering is behaviour, not incidental.
/// </remarks>
public readonly record struct WorldPresentationRequest(
    IGraphicsDevice Device,
    ResourceRegistry Resources,
    IOpenIgiLogger Logger);

/// <summary>Draws a title's skinned characters.</summary>
public interface ICharacterPresenter
{
    /// <summary>Draws every live character from one viewpoint.</summary>
    /// <param name="camera">Where the characters are seen from.</param>
    /// <param name="projection">The projection to draw under.</param>
    void Draw(in Camera3D camera, in Matrix4f projection);

    /// <summary>Draws the player's own body, which no character loop holds.</summary>
    /// <param name="camera">Where it is seen from.</param>
    /// <param name="projection">The projection to draw under.</param>
    /// <remarks>
    /// The player is not one of the title's characters -- it is the thing they are drawn around -- so it
    /// is never in <see cref="Draw"/>, and a pass that wants every caster has to ask for it separately.
    /// It costs a shadow only while something is showing it: a third-person view, a ladder, a death.
    /// Defaulted to nothing, so a title with no third-person body of its own ignores it.
    /// </remarks>
    void DrawPlayerBody(in Camera3D camera, in Matrix4f projection)
    {
        _ = camera;
        _ = projection;
    }
}

/// <summary>The renderers one title draws its world with.</summary>
/// <remarks>
/// <para>
/// Every presenter is nullable so that a title under construction renders the passes it has and skips
/// the rest, rather than failing the frame. An I.G.I. 2 build with terrain but no characters is a
/// useful build; one that will not present is not.
/// </para>
/// <para>
/// This carries the two passes that have a consumer today — <see cref="Rendering.ShadowCasterPass"/> draws the
/// world and the characters into the sun's cascades, and it is the one place a pass is handed its
/// renderers rather than reaching for them. The view model, the HUD and the menus are drawn by the
/// host against concrete renderers still, and move behind this interface when there is a second
/// implementation to shape them against: an abstraction extracted from one implementation is usually
/// the wrong one, and the cost of widening this later is a method, not a redesign.
/// </para>
/// </remarks>
public interface ITitlePresentation
{
    /// <summary>Gets the presenter for static world geometry, or null when the title has none loaded.</summary>
    IWorldPresenter? World { get; }

    /// <summary>Gets the presenter for skinned characters, or null when the title has none loaded.</summary>
    ICharacterPresenter? Characters { get; }
}
