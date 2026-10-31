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
/// implements it and registers it, as the <see cref="ISoldierBodies"/> the frame draws, for a
/// <c>SoldierPresentationRequest</c>. The simulation it carries is the one it draws.
/// </remarks>
public interface ISoldierPresenter : ISoldierBodies, IShootableBodies
{
    /// <summary>Gets the soldiers' simulation, which the renderer draws.</summary>
    SoldierSimulation Simulation { get; }

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

    /// <summary>Stands the fallen player's body up again, as a co-op player is brought back.</summary>
    void RevivePlayerBody()
    {
    }

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

    /// <summary>Follows a co-op session's partners' bodies for a tick: each made the first time, posed and stepped; one gone, let go.</summary>
    /// <param name="partners">The partners to show this tick.</param>
    /// <remarks>Each body is built as the player's was; a presenter with none to build shows none.</remarks>
    void FollowPartnerBodies(IReadOnlyList<PartnerBody> partners)
    {
    }

    /// <summary>Keeps the partners' bodies out of sight, or shows them again: followed all the while, drawn only when shown.</summary>
    /// <param name="hidden">Whether they are hidden, as a scene playing hides them.</param>
    void HidePartnerBodies(bool hidden)
    {
    }

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
    /// <param name="rungEventTime">
    /// Receives the authored time of the event 8 the clip crossed, or null when it crossed none.
    /// </param>
    /// <param name="ended">Receives whether a non-looping top transition reached its endpoint.</param>
    /// <remarks>Ticks and re-skins the body but does not move it; placement is the caller's, via
    /// <see cref="PlacePlayerBodyClimbing"/>.</remarks>
    void AdvancePlayerLadderAnimation(
        float facing,
        out Vector3d rootDelta,
        out float? rungEventTime,
        out bool ended);

    /// <summary>Places the climbing body without advancing its clip — holds the pose at a rung boundary.</summary>
    /// <param name="taskPosition">Where IGI1 places the human task root, in engine units.</param>
    /// <param name="facing">Which way it faces — into the ladder.</param>
    void PlacePlayerBodyClimbing(Vector3d taskPosition, float facing);

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

    /// <inheritdoc/>
    IEnumerable<HumanOccupant> ISoldierBodies.LiveOccupants() => Simulation.LiveOccupants();

    /// <inheritdoc/>
    void ISoldierBodies.Snapshot(List<SoldierSnapshot> into) => Simulation.Snapshot(into);

    /// <inheritdoc/>
    void ISoldierBodies.HidePlayerBody() => Simulation.HidePlayerBody();

    /// <inheritdoc/>
    IReadOnlyList<Vector3d> ISoldierBodies.MapContacts() => Simulation.MapContacts();

    /// <inheritdoc/>
    void ISoldierBodies.LogAiSummary() => Simulation.LogAiSummary();

    /// <inheritdoc/>
    IReadOnlyList<EnemyOverlayBox> ISoldierBodies.BinocularTargets(
        Camera3D camera,
        float horizontalTangent,
        int screenWidth,
        int screenHeight,
        int tick,
        float maxDetectionMetres,
        int simulationTick) =>
        Simulation.BinocularTargets(camera, horizontalTangent, screenWidth, screenHeight, tick, maxDetectionMetres, simulationTick);

    /// <inheritdoc/>
    bool ISoldierBodies.TryDetectionInfo(
        Vector3d originUnits, Vector3d direction, double reachUnits, Vector3d enemyEyeUnits, out string info) =>
        Simulation.TryDetectionInfo(originUnits, direction, reachUnits, enemyEyeUnits, out info);
}
