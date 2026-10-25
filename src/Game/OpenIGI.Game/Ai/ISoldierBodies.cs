using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Ai;

/// <summary>A level's soldiers as the frame draws and asks after them: their bodies, the player's own, and who stands where.</summary>
/// <remarks>
/// The title's presentation implements it and registers it for a <c>SoldierPresentationRequest</c>. What the title's
/// own simulation needs of the soldiers it keeps to its own extension of this.
/// </remarks>
public interface ISoldierBodies : IDisposable
{
    /// <summary>Gets or sets whether the followed body is actually drawn this frame.</summary>
    /// <value>
    /// <see langword="true"/> once the camera has left the head far enough for a body to be a body
    /// rather than the inside of a skull filling the screen.
    /// </value>
    /// <remarks>
    /// Deliberately separate from <c>FollowPlayerBody</c>'s latch, and read per FRAME rather than
    /// per tick. The follow keeps placing, animating and capturing the tick pair for the whole of the
    /// move between the two views; only the draw is switched, so crossing the threshold cannot be
    /// mistaken for the body appearing out of nowhere and reset the interpolation and temporal history
    /// that make it move smoothly.
    /// </remarks>
    bool PlayerBodyFollowVisible { get; set; }

    /// <summary>Draws a team-coloured through-wall glow over every soldier the last binocular scan marked.</summary>
    /// <remarks>
    /// Reuses the same inverted-hull selection glow as <c>DrawOutline</c>, keyed by the contacts
    /// <see cref="BinocularTargets"/> cached this frame, so a scanned enemy stays legible through cover
    /// while the remastered optics are up. Must run inside the scene-capture pass, before the resolve, so
    /// the glow carries the lens and bloom with the rest of the world.
    /// </remarks>
    /// <param name="camera">The eye the scene is being drawn through.</param>
    /// <param name="radiusPixels">The glow's screen-space radius, in pixels.</param>
    void DrawBinocularOutlines(in Camera3D camera, float radiusPixels);

    /// <summary>
    /// Gets what else the last binocular scan marked -- another game's humans in the level -- and the class of contact each is, for
    /// whatever draws them to outline as <see cref="DrawBinocularOutlines"/> outlines the level's own.
    /// </summary>
    IReadOnlyDictionary<object, OpenIGI.Game.Player.BinocularMarkerColor> BinocularMarkedOthers => NoneMarked;

    /// <summary>Nothing marked.</summary>
    private static readonly IReadOnlyDictionary<object, OpenIGI.Game.Player.BinocularMarkerColor> NoneMarked = new Dictionary<object, OpenIGI.Game.Player.BinocularMarkerColor>();

    /// <summary>Builds the drawable soldiers from a loaded level.</summary>
    /// <param name="mission">The mission whose packs hold the models and animation sets.</param>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <param name="terrain">The level's terrain, for standing soldiers on the ground it declares.</param>
    /// <param name="cast">The placed-object ray cast, for soldiers standing on floors and platforms.</param>
    /// <returns>The number of soldiers that resolved.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    int Load(LoadedMission mission, GameTask levelRoot, IGroundHeightField? terrain = null, WorldRayCast? cast = null);

    /// <summary>
    /// Names the soldier under a crosshair ray by tracing its <b>skinned collision mesh</b> — the same
    /// geometry a round is scored against, so the readout hits exactly where a shot would.
    /// </summary>
    /// <param name="originUnits">The eye, in world units.</param>
    /// <param name="direction">The look direction; need not be unit length.</param>
    /// <param name="reachUnits">How far to look, in world units.</param>
    /// <param name="name">Receives the struck soldier's model instance name.</param>
    /// <param name="distanceUnits">Receives how far along the ray it sits.</param>
    /// <param name="feetUnits">Receives the struck soldier's feet position, in world units.</param>
    /// <param name="zone">Receives the hit zone the round would score, or <c>-1</c> when none.</param>
    /// <returns><see langword="false"/> when the crosshair is not on a soldier's body.</returns>
    bool TryPick(
        Vector3d originUnits,
        Vector3d direction,
        double reachUnits,
        out string name,
        out double distanceUnits,
        out Vector3d feetUnits,
        out int zone);

    /// <summary>Puts each soldier's own weapon model in its hand.</summary>
    /// <param name="mission">The mission whose packs hold the weapon models.</param>
    /// <param name="roster">The weapon table, which maps a <c>WEAPON_ID_*</c> to the model that draws it.</param>
    /// <returns>How many soldiers ended up holding something.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Three reads join up here. <b>What a soldier carries is authored</b>, as ordinary <c>Gun</c> child
    /// tasks of the <c>HumanSoldier</c> carrying a <c>WEAPON_ID_*</c> — the same mechanism that arms the
    /// player — and the <b>first</b> of them is the one in hand, because everything that asks a human what
    /// it is holding takes slot 0 of the list at <c>Human+1220</c> (see
    /// <c>PlayerLoadout.WeaponsOf</c>). <b>Which model</b> comes from that id's row of the weapon
    /// table, whose <c>GunModel</c> field is what the world draws it with. And <b>where it hangs</b> is
    /// <see cref="HumanAttachPoints.ThirdPersonWeapon"/>, bone 22, which <c>0x4600C0</c>'s third-person arm
    /// reads out of the body's own palette at <c>+2928</c>/<c>+2940</c>.
    /// </para>
    /// <para>
    /// <b>Correction to the milestone's own framing.</b> The weapon is <em>not</em> resolved from the
    /// soldier's <c>AIType</c>. The AK in <c>AITYPE_PATROL_AK</c> is part of the <c>settings.qsc</c> key
    /// <c>(AIType, Difficulty)</c>, and a level-1 guard whose type says AK routinely carries an MP5SD —
    /// because its own child task says so. Nothing in the engine maps a type to a model.
    /// </para>
    /// </remarks>
    int Arm(LoadedMission mission, WeaponTypeRegistry roster);

    /// <summary>
    /// Records which soldiers the main view draws closely enough to refresh their eyes this frame -- the
    /// frustum sphere test retail's draw callback makes before it skins a body (<c>0x4D06C0</c>, the model's
    /// radius times 1.41), and the LOD range within which that skinning reaches the eye bone.
    /// </summary>
    /// <param name="camera">The frame's main camera.</param>
    /// <param name="projection">Its projection.</param>
    /// <remarks>Only the main view counts: the shadow cascades' light cameras skin nothing in the original.</remarks>
    void ObserveView(in Camera3D camera, in Matrix4f projection);

    /// <summary>Resolves where every soldier is drawn for one rendered frame.</summary>
    /// <param name="tickFraction">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <param name="smooth">
    /// Whether to draw between the two tick states. Off, every value is this tick's verbatim — the
    /// original's stepped presentation, which the vanilla reference captures depend on being exact.
    /// </param>
    /// <remarks>
    /// Once per frame, before any pass draws. <see cref="Draw"/> runs once per shadow cascade and once
    /// for the geometry pass, so anything resolved per frame — the blend, the ground snap, the motion
    /// bookkeeping — has to be resolved here or the passes would disagree with each other.
    /// </remarks>
    void BeginFrame(float tickFraction, bool smooth);

    /// <summary>Draws every soldier.</summary>
    /// <param name="camera">The eye the scene is drawn from.</param>
    /// <param name="projection">The world's view-to-clip transform — soldiers use no other.</param>
    /// <remarks>
    /// Called once per shadow cascade and once for the geometry pass; everything per-frame was settled
    /// by <see cref="BeginFrame"/>, so every pass draws the same bodies in the same places.
    /// </remarks>
    void Draw(in Camera3D camera, in Matrix4f projection);

    /// <summary>Draws the player's third-person body, when it is active — what the death camera looks at.</summary>
    /// <param name="camera">The eye, which by now is the death camera pulled back off the body.</param>
    /// <param name="projection">The projection to draw with.</param>
    /// <remarks>
    /// Drawn on its own, not in the soldier loop, and only when active — so it never appears in front of
    /// the first-person view during normal play. The body sits at its own position lifted by the death
    /// clip's root height, exactly as a fallen soldier is (see <see cref="Draw"/>).
    /// </remarks>
    void DrawPlayerBody(in Camera3D camera, in Matrix4f projection);

    /// <summary>Draws the one-tick muzzle flames of every soldier that fired, additively.</summary>
    /// <param name="camera">The eye the scene is drawn from.</param>
    /// <param name="projection">The world's view-to-clip transform.</param>
    /// <remarks>
    /// Once per frame, in the <b>forward</b> pass — never from <see cref="Draw"/>, which serves the
    /// geometry and shadow passes where an additive glow would be shaded like scenery. The flame is
    /// drawn at the same resolved weapon transform the body pass used, so the two halves of the gun
    /// agree to the pixel.
    /// </remarks>
    void DrawMuzzleFlashes(in Camera3D camera, in Matrix4f projection);

    /// <summary>Draws the soldiers' rounds' tracers over the lit scene: one pass in eleven, and every pass through a wall.</summary>
    /// <param name="camera">The eye the scene is drawn from.</param>
    /// <param name="projection">The world's view-to-clip transform.</param>
    /// <param name="renderHeight">The height of the image they are rasterised into, which sizes them.</param>
    /// <param name="retailLine">Whether to draw the original's thin unfogged line rather than the remaster's glowing streak.</param>
    void DrawGunShotTracers(in Camera3D camera, in Matrix4f projection, int renderHeight, bool retailLine)
    {
    }

    /// <summary>Where the third-person body's hand is this frame, for the weapon it is holding.</summary>
    /// <returns>
    /// The hand bone's world placement in engine units, or <see langword="null"/> when no body is being
    /// followed or the rig has no such bone.
    /// </returns>
    /// <remarks>
    /// <see cref="HumanAttachPoints.ThirdPersonWeapon"/> — bone 22, the arm <c>0x4600C0</c> resolves for
    /// a body seen from outside, which is the same bone a guard's rifle rides. Interpolated between the
    /// same two ticks the body itself is drawn between, or the gun steps at 30 Hz in a hand that glides.
    /// </remarks>
    BoneAttachment? PlayerBodyHandAttachment();

    /// <summary>Draws a selection glow around one soldier.</summary>
    /// <param name="index">The soldier's index in the order <see cref="Snapshot"/> fills.</param>
    /// <param name="camera">The eye the frame is drawn from.</param>
    /// <param name="colour">The glow colour.</param>
    /// <param name="radius">How far the glow reaches past the silhouette, in pixels.</param>
    /// <param name="throughWalls">Whether it ignores the depth buffer.</param>
    /// <returns><see langword="true"/> when something was drawn.</returns>
    /// <remarks>
    /// <para>
    /// The pose is handed to the device the same way the ordinary draw hands it over, because a
    /// soldier's mesh is uploaded as two poses the shader blends between. Without it the glow would
    /// stand in whichever pose was uploaded last and lag a step behind the body inside it.
    /// </para>
    /// <para>
    /// The walk mirrors <see cref="Snapshot"/> exactly — group, then member — because that walk is what
    /// defines the handle the inspector asks with.
    /// </para>
    /// </remarks>
    bool DrawOutline(int index, in Camera3D camera, Bgra32 colour, float radius, bool throughWalls);

    /// <summary>Copies every soldier's state into a list, group by group and member by member.</summary>
    /// <param name="into">The list, which is filled in the order <see cref="DrawOutline"/> indexes.</param>
    void Snapshot(List<SoldierSnapshot> into);

    /// <summary>
    /// Loads the player's third-person body — the model the level's <c>HumanPlayer</c> declares, on
    /// skeleton 0's set, standing in clip 2 — so the death camera has a body to reveal.
    /// </summary>
    /// <param name="mission">The mission whose packs hold the model.</param>
    /// <param name="model">The player's model, from the <c>HumanPlayer</c> task's <c>Model</c> parameter.</param>
    /// <param name="animations">Skeleton 0's animation set (<c>000.iff</c>), the same one the player walks on.</param>
    /// <param name="position">Where the level places the player, in engine units.</param>
    /// <param name="gamma">The heading the player starts facing.</param>
    /// <returns><see langword="false"/> when the model does not resolve or does not upload.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The body is built from a synthetic <c>HumanSoldierTask</c> because that is all
    /// <c>SoldierView</c> needs — skeleton, stand clip, position and heading — and the player's
    /// constructor (<c>0x410370</c>) builds exactly that on <c>Human+104</c> (see <c>HumanPlayerBody</c>).
    /// It is kept out of <c>_groups</c> so nothing else draws or ticks it; only the death path does.
    /// </remarks>
    bool LoadPlayerBody(
        LoadedMission mission, string model, BoneAnimationSet animations, Vector3d position, float gamma);

    /// <summary>Stands the player's body at a place and draws it, outside death and outside climbing.</summary>
    /// <param name="feetUnits">Where the body stands, in engine units — the level's own spawn.</param>
    /// <param name="facing">Which way it faces, as the spawn's gamma.</param>
    /// <remarks>
    /// <para>
    /// A third reason to draw the body, and deliberately a separate flag rather than a reuse of either
    /// existing one. The mission-entry shot dives onto the character standing at their spawn and seats
    /// the view inside their head, so it needs the body visible for a second — but it must not touch
    /// <c>_playerBodyActive</c>, which is the death latch and also drives the fall's per-tick re-skin and
    /// the knock-back, nor the climbing latch, which the ladder code clears on its own schedule.
    /// </para>
    /// <para>
    /// <b>It has to place and re-skin, not merely make visible.</b> <c>LoadPlayerBody</c> uploads
    /// the model's REST mesh and leaves the body unplaced — the death path places and poses it, the
    /// climbing path places and poses it, and until one of them runs there is no pose in the vertex
    /// buffer at all. Drawing it in that state put enormous black shards across the middle of the descent,
    /// which is exactly what an unskinned rig looks like when its vertices are still at the origin.
    /// </para>
    /// <para>
    /// Cleared by <see cref="HidePlayerBody"/> and by <c>Release</c>, so a mission that ends
    /// mid-shot does not leave a body standing in the next one.
    /// </para>
    /// </remarks>
    void StandPlayerBodyAt(Vector3d feetUnits, float facing);

    /// <summary>Puts the player's third-person body away, as a shot that showed it ends.</summary>
    void HidePlayerBody();

    /// <summary>Gets where the soldiers the map computer marks stand.</summary>
    /// <returns>Their feet, in world units.</returns>
    IReadOnlyList<Vector3d> MapContacts();

    /// <summary>Logs a summary of the soldiers' brains, as the game closes.</summary>
    void LogAiSummary();

    /// <summary>Every soldier where it stands at this moment.</summary>
    /// <returns>The soldiers, dead and alive.</returns>
    IEnumerable<HumanOccupant> LiveOccupants();

    /// <summary>Projects the contacts the binoculars mark this frame.</summary>
    /// <param name="camera">The presented eye the contacts are projected through.</param>
    /// <param name="horizontalTangent">The optics' horizontal half-angle tangent (the zoom).</param>
    /// <param name="screenWidth">The back buffer width in pixels.</param>
    /// <param name="screenHeight">The back buffer height in pixels.</param>
    /// <param name="tick">The millisecond tick driving slot lifetime and the entry pulse.</param>
    /// <param name="maxDetectionMetres">The furthest a contact may be and still be marked, in metres of forward distance.</param>
    /// <param name="simulationTick">The fixed simulation tick line-of-sight results are cached on.</param>
    /// <returns>The marked contacts.</returns>
    IReadOnlyList<EnemyOverlayBox> BinocularTargets(
        Camera3D camera,
        float horizontalTangent,
        int screenWidth,
        int screenHeight,
        int tick,
        float maxDetectionMetres,
        int simulationTick);

    /// <summary>Names the detection state of the soldier under a ray, toward a target eye.</summary>
    /// <param name="originUnits">The ray origin in engine units.</param>
    /// <param name="direction">The ray direction; need not be normalised.</param>
    /// <param name="reachUnits">How far to look, in units.</param>
    /// <param name="enemyEyeUnits">The eye the guards actually test against, in units.</param>
    /// <param name="info">The formatted detection line, empty when no soldier is under the ray.</param>
    /// <returns><see langword="true"/> when a soldier was under the ray and had a brain to ask.</returns>
    bool TryDetectionInfo(Vector3d originUnits, Vector3d direction, double reachUnits, Vector3d enemyEyeUnits, out string info);
}
