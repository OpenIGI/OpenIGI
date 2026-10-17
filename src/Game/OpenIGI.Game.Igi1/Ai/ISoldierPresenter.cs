using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi1.Physics;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>How the level's soldiers are drawn: their meshes, the player's own third-person body, and what is hit through the pose on screen.</summary>
/// <remarks>
/// Declared beside <see cref="SoldierSimulation"/> because every member speaks its language; the title's presentation
/// implements it and registers it for a <see cref="SoldierPresentationRequest"/>. The simulation it carries is the one
/// it draws.
/// </remarks>
public interface ISoldierPresenter : IDisposable
{
    /// <summary>Gets the soldiers' simulation, which the renderer draws.</summary>
    SoldierSimulation Simulation { get; }

    /// <summary>Gets or sets the derived surface-detail maps, or <see langword="null"/> for none.</summary>
    /// <remarks>Owned by the shell: one pack serves every renderer that loads a texture.</remarks>
    MaterialDetailPack? DetailMaps { get; set; }

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

    /// <summary>Raised where a round met a body, so the blood can be thrown there.</summary>
    /// <remarks>
    /// <c>Human</c>'s damage handler (<c>0x45F930</c>) calls the blood (<c>0x463AD0</c>) as its very
    /// first act, <b>before</b> the invulnerability and instant-death gates and before any damage is
    /// applied. So this is raised on contact, not on damage: a friendly round or one on an invulnerable
    /// guard still makes it bleed, exactly as it does in the original. The first argument identifies
    /// the body, whose two particle systems the effect runtime keeps and reuses.
    /// </remarks>
    Action<object, Vector3d, Vector3f>? BloodSpray { get; set; }

    /// <summary>Gets a value indicating whether the player's third-person body is posed and drawing.</summary>
    /// <remarks>Set once the player has died and <see cref="KillPlayerBody"/> has run; the death camera reads it.</remarks>
    bool IsPlayerBodyActive { get; }

    /// <summary>Gets where the player's corpse actually is, in engine units — what the death camera frames.</summary>
    /// <value>
    /// The body's centre of mass, or <see langword="null"/> when no ragdoll is running — in which case
    /// the death camera's own fixed point off the authoritative body is still the right answer.
    /// </value>
    /// <remarks>
    /// Without this the camera keeps looking at the point the retail clip moved the body to, which once
    /// physics is drawing it somewhere else means framing an empty patch of floor.
    /// </remarks>
    Vector3d? PlayerRagdollCentre { get; }

    /// <summary>Gets the number of soldiers that resolved and will draw.</summary>
    int Count { get; }

    /// <summary>Draws a selection glow around one soldier.</summary>
    /// <param name="index">The soldier's index in the order <see cref="SoldierSimulation.Snapshot"/> fills.</param>
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
    /// The walk mirrors <see cref="SoldierSimulation.Snapshot"/> exactly — group, then member — because that walk is what
    /// defines the handle the inspector asks with.
    /// </para>
    /// </remarks>
    bool DrawOutline(int index, in Camera3D camera, Bgra32 colour, float radius, bool throughWalls);

    /// <summary>Draws a team-coloured through-wall glow over every soldier the last binocular scan marked.</summary>
    /// <remarks>
    /// Reuses the same inverted-hull selection glow as <see cref="DrawOutline"/>, keyed by the contacts
    /// <see cref="SoldierSimulation.BinocularTargets"/> cached this frame, so a scanned enemy stays legible through cover
    /// while the remastered optics are up. Must run inside the scene-capture pass, before the resolve, so
    /// the glow carries the lens and bloom with the rest of the world.
    /// </remarks>
    /// <param name="camera">The eye the scene is being drawn through.</param>
    /// <param name="radiusPixels">The glow's screen-space radius, in pixels.</param>
    void DrawBinocularOutlines(in Camera3D camera, float radiusPixels);

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
    /// <see cref="PlayerLoadout.WeaponsOf"/>). <b>Which model</b> comes from that id's row of the weapon
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

    /// <summary>Loads the weapon models the vehicles' guns carry and takes where the runtime poses them.</summary>
    /// <param name="mission">The mission whose packs hold the models.</param>
    /// <param name="weaponModels">Every vehicle gun's weapon model (magic-object argument 1).</param>
    /// <param name="guns">Each gun's weapon object as this tick posed it.</param>
    /// <remarks>
    /// An <c>AIStationaryGun</c> on a vehicle carries its weapon as an object of its own (<c>+452</c>), which the
    /// mount model does not include: a T80's turret has no barrel until its cannon is drawn on it. The weapon's
    /// own children come with it, the M2HB's gun flame among them.
    /// </remarks>
    void ArmVehicleGuns(
        LoadedMission mission,
        IEnumerable<string> weaponModels,
        Func<IEnumerable<VehicleGunVisual>> guns);

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
    /// The body is built from a synthetic <see cref="HumanSoldierTask"/> because that is all
    /// <see cref="SoldierView"/> needs — skeleton, stand clip, position and heading — and the player's
    /// constructor (<c>0x410370</c>) builds exactly that on <c>Human+104</c> (see <see cref="HumanPlayerBody"/>).
    /// It is kept out of <c>_groups</c> so nothing else draws or ticks it; only the death path does.
    /// </remarks>
    bool LoadPlayerBody(
        LoadedMission mission, string model, BoneAnimationSet animations, Vector3d position, float gamma);

    /// <summary>
    /// Poses the player's body with its death fall and starts drawing it — the player has died.
    /// </summary>
    /// <param name="deathAnimation">The fall clip the death state chose, from the player's hit-zone row.</param>
    /// <param name="feet">Where the body falls, in engine units — the player's own position.</param>
    /// <param name="gamma">Which way the body faces as it goes down.</param>
    /// <param name="bone">The bone the killing round went through, or <c>-1</c> when none was named.</param>
    /// <param name="travel">The killing round's own line, from the muzzle towards where it ended.</param>
    /// <param name="category">What fired it, which is where the impulse's magnitude comes from.</param>
    /// <param name="velocity">What the player was doing at the moment of death, in units per tick.</param>
    /// <remarks>
    /// Does nothing when no body was loaded; the death still happens, just without a body to show. The
    /// last four arguments are the ragdoll's, and are ignored entirely when <c>g_ragdoll</c> is off —
    /// the retail clip above them runs either way.
    /// </remarks>
    void KillPlayerBody(
        int deathAnimation,
        Vector3d feet,
        float gamma,
        int bone = -1,
        Vector3d travel = default,
        WeaponCategory category = WeaponCategory.Gun,
        Vector3d velocity = default);

    /// <summary>Advances the player body's death clip by one tick and re-skins it, once it is active.</summary>
    /// <remarks>
    /// The same per-tick re-skin a corpse gets: the clip advances, holding its last frame when it runs out,
    /// and the body's own mesh is rebuilt from the new pose so the fall plays through.
    /// </remarks>
    void TickPlayerBody();

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
    /// <b>It has to place and re-skin, not merely make visible.</b> <see cref="LoadPlayerBody"/> uploads
    /// the model's REST mesh and leaves the body unplaced — the death path places and poses it, the
    /// climbing path places and poses it, and until one of them runs there is no pose in the vertex
    /// buffer at all. Drawing it in that state put enormous black shards across the middle of the descent,
    /// which is exactly what an unskinned rig looks like when its vertices are still at the origin.
    /// </para>
    /// <para>
    /// Cleared by <see cref="SoldierSimulation.HidePlayerBody"/> and by <c>Release</c>, so a mission that ends
    /// mid-shot does not leave a body standing in the next one.
    /// </para>
    /// </remarks>
    void StandPlayerBodyAt(Vector3d feetUnits, float facing);

    /// <summary>Walks the third-person body with the player, in the clip the player's own legs are in.</summary>
    /// <param name="feetUnits">Where the player is standing, in engine units — <c>PlayerBody.Position</c>.</param>
    /// <param name="facing">Which way the body faces, in radians — the player's yaw.</param>
    /// <param name="animation">
    /// The clip the player's <see cref="HumanLocomotionAnimator"/> is playing, or
    /// <see cref="HumanLocomotionStates.NoAnimation"/> for the states that name none.
    /// </param>
    /// <param name="loops">Whether that state's table entry loops.</param>
    /// <param name="pose">
    /// The half of the body the legs' clip does not drive — the weapon's torso animation and where the
    /// player is looking. See <see cref="PlayerBodyPose"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Called on the simulation tick, not on the frame.</b> The body's clip advances one authoring
    /// frame per <see cref="SoldierView.Tick"/> and the player's animator advances one per tick, so
    /// stepping it here — and only here — is what keeps the two in phase without any clock being copied
    /// between them: the same state entered on the same tick starts the same clip at the same zero. Step
    /// it from the draw instead and a display slower than 30 Hz walks the legs at less than the speed the
    /// body is actually travelling, which is a character skating along the ground.
    /// </para>
    /// <para>
    /// The placement is the ordinary one a soldier gets — the task position lifted by the clip's own root
    /// height (<c>PlayerBodyPlacement</c>) — so a crouch sits where a crouching guard sits.
    /// </para>
    /// <para>
    /// <paramref name="facing"/> is a <b>gamma</b>, not the player's yaw: the two run opposite ways, and
    /// the caller negates. See the call site in <c>DesktopGame</c>.
    /// </para>
    /// </remarks>
    void FollowPlayerBody(Vector3d feetUnits, float facing, int animation, bool loops, in PlayerBodyPose pose);

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

    /// <summary>Starts the third-person body in one of IGI1's ladder animation states.</summary>
    /// <param name="animation">The state's authored clip.</param>
    /// <param name="loops">Whether the clip loops.</param>
    /// <param name="backwards">Whether it starts at its end and runs backwards.</param>
    /// <param name="paused">Whether its playback rate starts at zero.</param>
    void StartPlayerLadderAnimation(
        int animation,
        bool loops,
        bool backwards = false,
        bool paused = false);

    /// <summary>Starts a non-ladder interaction clip with its authored root height intact.</summary>
    /// <remarks>
    /// Wire animations 172/173 carry a non-zero root-height track. IGI1 adds that sample when locating
    /// the body; suppressing it as if the wire were a ladder leaves both hands visibly off the cable.
    /// </remarks>
    void StartPlayerInteractionAnimation(int animation, bool loops);

    /// <summary>Advances the current ladder clip and returns its movement and state signals.</summary>
    /// <param name="facing">Which way the body faces — into the ladder.</param>
    /// <param name="rootDelta">
    /// Receives the clip's root-motion delta for this tick, already rotated into the facing frame (world
    /// units) — the same transform a walking guard's motion gets. The caller applies it to the position.
    /// </param>
    /// <param name="rungBoundary">Receives whether animation 168 crossed authored event 8.</param>
    /// <param name="ended">Receives whether a non-looping top transition reached its endpoint.</param>
    /// <remarks>Ticks and re-skins the body but does not move it; placement is the caller's, via
    /// <see cref="PlacePlayerBodyClimbing"/>.</remarks>
    void AdvancePlayerLadderAnimation(
        float facing,
        out Vector3d rootDelta,
        out bool rungBoundary,
        out bool ended);

    /// <summary>Places the climbing body without advancing its clip — holds the pose at a rung boundary.</summary>
    /// <param name="taskPosition">Where IGI1 places the human task root, in engine units.</param>
    /// <param name="facing">Which way it faces — into the ladder.</param>
    void PlacePlayerBodyClimbing(Vector3d taskPosition, float facing);

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

    /// <summary>Offers a round to every soldier and scores it against the nearest one it meets.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="rayEnd">Where it stops.</param>
    /// <param name="damageFactor">The weapon's <c>Damage Factor</c>.</param>
    /// <param name="category">The firing weapon's category, which decides how a body it kills falls.</param>
    /// <param name="hit">The nearest soldier struck.</param>
    /// <returns><see langword="false"/> when the round meets nobody.</returns>
    /// <remarks>
    /// <b>The damage is applied here</b>, because the health belongs to the soldier and not to the
    /// round. A soldier already dead is skipped rather than shot again — the original stops a corpse
    /// taking damage through a flag on the human, and this is the same effect with the state that
    /// exists.
    /// </remarks>
    bool TryShoot(
        Vector3d origin,
        Vector3d rayEnd,
        float damageFactor,
        WeaponCategory category,
        out HumanHit hit);

    /// <summary>Gets a delegate that offers rounds to these soldiers.</summary>
    /// <returns>The delegate the player takes.</returns>
    HumanShot Shooter();

    /// <summary>Advances every soldier's animation by one tick and re-skins the groups.</summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing advanced a soldier's rig before this existed</b>, so every guard in the level stood frozen on
    /// the first frame of its stand animation — which reads as a default A-pose. Soldiers are grouped by
    /// (model, skeleton, animation) and share one skinned mesh, so ticking the group's first soldier and
    /// re-skinning from its pose animates the whole group: they run the same clip from the same start, so they
    /// share a pose by construction.
    /// </para>
    /// <para>
    /// The device's meshes are immutable, so this destroys and re-uploads. There are only a handful of groups
    /// in a level, so it is a few uploads a tick — worth replacing with a dynamic vertex buffer, but not worth
    /// leaving the guards frozen for.
    /// </para>
    /// </remarks>
    /// <param name="enemy">The enemy's eye position, which the view cones are tested against.</param>
    /// <param name="enemyAlive">
    /// Whether the enemy is still a target. A dead player is not: the detection scan (<c>0x489750</c>)
    /// unregisters a human that reports itself invalid, so the guards lose it and stop firing at the
    /// corpse — passing <see langword="false"/> reproduces that.
    /// </param>
    /// <param name="enemyFeet">
    /// The enemy task's position — its feet, what combat aims at (<c>sub_451380</c>). Defaults to
    /// <paramref name="enemy"/> when the caller has no body to speak of.
    /// </param>
    /// <param name="enemyCrouching">Whether the enemy is crouching, which halves the aim raise.</param>
    void Tick(
        Vector3d enemy = default,
        bool enemyAlive = true,
        Vector3d? enemyFeet = null,
        bool enemyCrouching = false);

    /// <summary>Closes a tick: the soldiers' collision, corpses, dropped weapons and skinning.</summary>
    void EndTick();
}
