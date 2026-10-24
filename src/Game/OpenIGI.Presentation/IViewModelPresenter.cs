using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>Which of a weapon's parts one <see cref="IHeldWeaponPresenter.Draw"/> call covers.</summary>
/// <remarks>
/// The split exists because the two kinds of part belong to different passes on the modern renderer:
/// the gun's body is geometry, but its muzzle flame is LIGHT — additive, depth-read-only — and light
/// drawn into the deferred G-buffer comes out shaded like scenery. That was visible as a one-frame
/// dark square at a firing soldier's muzzle. A soldier now draws its body in the geometry and shadow
/// passes and its flame in the forward pass; the first-person weapon draws whole in the forward pass
/// and keeps <see cref="All"/>.
/// </remarks>
public enum WeaponDrawParts
{
    /// <summary>Every part — the first-person path, which draws entirely in the forward pass.</summary>
    All,

    /// <summary>The rigid parts only, for the geometry and shadow passes.</summary>
    BodyOnly,

    /// <summary>The muzzle flame only, for the forward pass.</summary>
    FlameOnly,
}

/// <summary>Draws the arms the player sees holding the weapon.</summary>
/// <remarks>
/// Separate from <see cref="IHeldWeaponPresenter"/> because the two have different lifetimes: the arms
/// are the player's own model and last the mission, while the gun is rebuilt every time the loadout
/// changes. Merging them would mean rebuilding the arms to change weapon.
/// </remarks>
public interface IViewModelPresenter : IDisposable
{
    /// <summary>Loads the arms model for a mission.</summary>
    /// <param name="mission">The mission whose packs the model is read from.</param>
    /// <returns><see langword="false"/> when the mission ships no arms model.</returns>
    bool Load(LoadedMission mission);

    /// <summary>Marks the pose stale, so the next draw rebuilds it.</summary>
    void Invalidate();

    /// <summary>Draws the arms.</summary>
    /// <param name="view">The player's view state, which the pose follows.</param>
    /// <param name="camera">Where the arms are seen from.</param>
    /// <param name="projection">The view-model projection, which is not the world's.</param>
    /// <param name="originUnits">Where to anchor the rig, or null to take the view's own origin.</param>
    /// <param name="previousPoseShare">How much of the previous tick's pose to blend in.</param>
    /// <param name="orientation">An orientation to override the view's, or null to use it.</param>
    void Draw(
        HumanView view,
        in Camera3D camera,
        in Matrix4f projection,
        Vector3d? originUnits = null,
        float previousPoseShare = 0f,
        Matrix3f? orientation = null);
}

/// <summary>Draws what a title's own player holds in front of its eyes, from the frame the player gives.</summary>
/// <remarks>
/// The counterpart of <see cref="IViewModelPresenter"/> for a player the title brings itself
/// (<see cref="ITitleFirstPerson"/>): the pose and the parts come from the title, the models from its packs.
/// </remarks>
public interface ITitleViewModelPresenter : IDisposable
{
    /// <summary>Gets or sets how the level lights an object with no bake, which is what the view's models are.</summary>
    ObjectShading Shading { get; set; }

    /// <summary>Gets ready to draw a mission's models.</summary>
    /// <param name="mission">The mission whose packs the models are read from.</param>
    /// <returns><see langword="false"/> when the mission has nothing to draw them from.</returns>
    bool Load(LoadedMission mission);

    /// <summary>Draws the first-person view.</summary>
    /// <param name="view">What the eyes hold.</param>
    /// <param name="camera">Where it is seen from, in metres.</param>
    /// <param name="projection">The view model's projection, which is not the world's.</param>
    void Draw(TitleFirstPersonView view, in Camera3D camera, in Matrix4f projection);
}

/// <summary>Draws the rigid models a title's own player has lying in its level, among the world's geometry.</summary>
/// <remarks>
/// The world-pass counterpart of <see cref="ITitleViewModelPresenter"/>, for a player that is also
/// <see cref="ITitleWorldModels"/>: the same models from the same packs, under the world's projection and depth.
/// </remarks>
public interface ITitleWorldModelPresenter
{
    /// <summary>Draws the models, between the last two ticks.</summary>
    /// <param name="models">The models and where they stand.</param>
    /// <param name="camera">Where they are seen from, in metres.</param>
    /// <param name="projection">The world's projection.</param>
    /// <param name="blend">
    /// How far the frame is between the last two ticks: a model the title names by its <see cref="TitleWorldModel.Instance"/>
    /// is drawn that far from where it was a tick ago to where it is, its pose with it; one draws every model where the last
    /// tick left it.
    /// </param>
    void DrawWorld(IReadOnlyList<TitleWorldModel> models, in Camera3D camera, in Matrix4f projection, float blend = 1f);

    /// <summary>Draws camera-facing additive lights against the world's depth.</summary>
    /// <param name="sprites">The title's lights.</param>
    /// <param name="camera">The world camera.</param>
    /// <param name="projection">The world projection.</param>
    void DrawWorldSprites(IReadOnlyList<TitleWorldSprite> sprites, in Camera3D camera, in Matrix4f projection) { }

    /// <summary>
    /// Draws the muzzle flashes of the models the last <see cref="DrawWorld"/> drew, as light over the lit scene: drawn among
    /// the solid models they land in the G-buffer, where most of each is cut away and the rest is shaded as a wall.
    /// </summary>
    /// <param name="camera">The world camera.</param>
    /// <param name="projection">The world projection.</param>
    void DrawWorldMuzzleFlashes(in Camera3D camera, in Matrix4f projection) { }

    /// <summary>Draws marks lying on the ground, blended over the world and tested against its depth.</summary>
    /// <param name="decals">The marks.</param>
    /// <param name="camera">The world camera.</param>
    /// <param name="projection">The world projection.</param>
    void DrawGroundDecals(IReadOnlyList<TitleGroundDecal> decals, in Camera3D camera, in Matrix4f projection) { }
}

/// <summary>Draws the weapon the player is holding.</summary>
public interface IHeldWeaponPresenter : IDisposable
{
    /// <summary>Gets whether a weapon model is loaded and can be drawn.</summary>
    bool IsReady { get; }

    /// <summary>Gets or sets what each of the weapon's textures is made of.</summary>
    MaterialLibrary Materials { get; set; }

    /// <summary>Gets where the weapon's muzzle is, in metres in the weapon's own frame, or null when it has no gun flame.</summary>
    /// <remarks>The gun-flame part's place in the magic-object tree: where the original draws the flame.</remarks>
    Vector3f? MuzzleMetres => null;

    /// <summary>Loads one weapon's model.</summary>
    /// <param name="mission">The mission whose packs the model is read from.</param>
    /// <param name="gunModel">The model to load.</param>
    /// <returns><see langword="false"/> when the model is missing or unchanged.</returns>
    bool Load(LoadedMission mission, string gunModel);

    /// <summary>Draws the weapon on the view bone.</summary>
    /// <param name="attachment">Where the weapon hangs on the arms.</param>
    /// <param name="camera">Where it is seen from.</param>
    /// <param name="projection">The view-model projection.</param>
    /// <param name="firedThisTick">Whether the shot animation starts this tick.</param>
    /// <param name="shotSequence">Which shot this is, so a repeating animation can vary.</param>
    /// <param name="parts">Which parts of the weapon to draw.</param>
    /// <param name="magazineAttachment">Where the magazine hangs, or null to leave it on the weapon.</param>
    /// <param name="movingPartAttachment">Where the moving part hangs, or null to leave it in place.</param>
    void Draw(
        BoneAttachment attachment,
        in Camera3D camera,
        in Matrix4f projection,
        bool firedThisTick,
        int shotSequence,
        WeaponDrawParts parts = WeaponDrawParts.All,
        BoneAttachment? magazineAttachment = null,
        BoneAttachment? movingPartAttachment = null);
}

/// <summary>Draws the grenades a level has in flight.</summary>
/// <remarks>
/// Not a view-model pass despite arriving with one: a thrown grenade is in the world, and the host
/// draws it in the world region of the frame. It is grouped with the view model only because the two
/// are built from the same request.
/// </remarks>
public interface IGrenadePresenter : IDisposable
{
    /// <summary>Loads the grenade models a mission uses.</summary>
    /// <param name="mission">The mission whose packs the models are read from.</param>
    void Load(LoadedMission mission);

    /// <summary>Draws every grenade in flight.</summary>
    /// <param name="grenades">The simulation holding them.</param>
    /// <param name="camera">Where they are seen from.</param>
    /// <param name="projection">The projection to draw under.</param>
    /// <param name="alpha">How far the frame sits between the previous tick and the next.</param>
    void Draw(GrenadeSimulation grenades, in Camera3D camera, in Matrix4f projection, float alpha);
}

/// <summary>Draws the rockets and grenade-launcher rounds a level has in flight.</summary>
public interface IMissilePresenter : IDisposable
{
    /// <summary>Loads the projectile models a mission uses.</summary>
    /// <param name="mission">The mission whose packs the models are read from.</param>
    void Load(LoadedMission mission);

    /// <summary>Draws every rocket in flight.</summary>
    /// <param name="missiles">The simulation holding them.</param>
    /// <param name="camera">Where they are seen from.</param>
    /// <param name="projection">The projection to draw under.</param>
    /// <param name="alpha">How far the frame sits between the previous tick and the next.</param>
    void Draw(MissileSimulation missiles, in Camera3D camera, in Matrix4f projection, float alpha);

    /// <summary>Draws every launched grenade round in flight.</summary>
    /// <param name="projectiles">The simulation holding them.</param>
    /// <param name="camera">Where they are seen from.</param>
    /// <param name="projection">The projection to draw under.</param>
    /// <param name="alpha">How far the frame sits between the previous tick and the next.</param>
    void Draw(M203Simulation projectiles, in Camera3D camera, in Matrix4f projection, float alpha);
}

/// <summary>Draws the weapons lying in the world for the player to pick up.</summary>
public interface IGunPickupPresenter : IDisposable
{
    /// <summary>Gets or sets what each of the weapons' textures is made of.</summary>
    MaterialLibrary Materials { get; set; }

    /// <summary>Loads the models for every pickup a level starts with.</summary>
    /// <param name="mission">The mission whose packs the models are read from.</param>
    /// <param name="pickups">The level's pickups.</param>
    void Load(LoadedMission mission, GunPickupRegistry pickups);

    /// <summary>Loads the model for one pickup that appeared after the level started.</summary>
    /// <param name="mission">The mission whose packs the model is read from.</param>
    /// <param name="pickup">The pickup to add.</param>
    void Add(LoadedMission mission, GunPickup pickup);

    /// <summary>Prepares the frame, interpolating any pickup still settling between ticks.</summary>
    /// <param name="tickFraction">How far the frame sits between the previous tick and the next.</param>
    /// <param name="smooth">Whether to interpolate at all.</param>
    void BeginFrame(float tickFraction, bool smooth);

    /// <summary>Draws every pickup.</summary>
    /// <param name="camera">Where they are seen from.</param>
    /// <param name="projection">The projection to draw under.</param>
    void Draw(in Camera3D camera, in Matrix4f projection);
}

/// <summary>What a title needs in order to build any of the first-person and projectile presenters.</summary>
/// <param name="Device">The graphics device to upload through.</param>
/// <param name="Resources">The registry models and textures are resolved through.</param>
/// <param name="Magic">The magic-object table the projectile and pickup models are named by.</param>
/// <param name="Logger">Where load diagnostics go.</param>
/// <param name="Work">The host's work scheduler, which a presenter hands parallel work to; none runs it in line.</param>
/// <remarks>
/// One request for five presenters because all five want the same four things, and a request per
/// presenter would be four-fifths repetition. <see cref="Magic"/> is unused by the arms, which name no
/// magic object; a presenter ignoring a field it does not need is cheaper than a second request type.
/// </remarks>
public readonly record struct ViewModelPresentationRequest(
    IGraphicsDevice Device,
    ResourceRegistry Resources,
    MagicObjectRegistry? Magic,
    IOpenIgiLogger Logger,
    Core.Threading.IWorkScheduler? Work = null);
