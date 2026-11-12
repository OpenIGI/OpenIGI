using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>What the title's loose objects in a level are drawn from: the level, its materials and what makes each presenter.</summary>
/// <param name="Presenters">The title's presenters.</param>
/// <param name="Models">What a model-drawing presenter is made from.</param>
/// <param name="Effects">What a sprite-drawing presenter is made from.</param>
/// <param name="Mission">The loaded level.</param>
/// <param name="Materials">The level's materials.</param>
public readonly record struct WorldObjectsRequest(
    PresenterRegistry Presenters,
    ViewModelPresentationRequest Models,
    EffectsPresentationRequest Effects,
    LoadedMission Mission,
    MaterialLibrary Materials);

/// <summary>
/// What a title has put in the world beside the level's own geometry, drawn among it: the weapons lying about, what flies,
/// the marks rounds left and the lamps its objects carry. The host calls it at each point of a frame and knows none of them.
/// </summary>
public interface IMissionWorldView
{
    /// <summary>Gets whether the player's weapon is drawn in its hand, and the shot it is on.</summary>
    /// <param name="shotSequence">The player's shot counter, which the muzzle's parts follow.</param>
    /// <returns><see langword="false"/> when there is no player or what it holds hides its weapon.</returns>
    bool TryHeldWeapon(out int shotSequence);

    /// <summary>Makes what draws the weapons lying in the level and what flies in it.</summary>
    /// <param name="request">What they are made from.</param>
    /// <param name="lying">The weapons lying in the level, or <see langword="null"/> when it has none to draw.</param>
    void LoadLooseObjects(in WorldObjectsRequest request, IReadOnlyList<GunPickup>? lying)
    {
    }

    /// <summary>Makes what draws the marks rounds leave.</summary>
    /// <param name="request">What it is made from.</param>
    void LoadMarks(in WorldObjectsRequest request)
    {
    }

    /// <summary>Makes what draws the lamps the level's objects carry.</summary>
    /// <param name="request">What it is made from.</param>
    /// <param name="fileSystem">Where its sprite is read from.</param>
    void LoadLamps(in WorldObjectsRequest request, VirtualFileSystem fileSystem)
    {
    }

    /// <summary>Frees the weapons lying in the level and the marks, ahead of a player being stood again.</summary>
    void ReleaseLyingWeaponsAndMarks()
    {
    }

    /// <summary>Frees everything made for the level.</summary>
    void ReleaseLevel()
    {
    }

    /// <summary>Lights the loose models by the level's light, as the original lights any unbaked model.</summary>
    /// <param name="sun">The level's sun, or <see langword="null"/>.</param>
    void Light(FixedFunctionSun? sun)
    {
    }

    /// <summary>Opens a frame: where between two ticks it is drawn.</summary>
    /// <param name="tickFraction">How far the frame sits between the previous tick and the next.</param>
    /// <param name="smooth">Whether what moves is drawn between its ticks.</param>
    void BeginFrame(float tickFraction, bool smooth)
    {
    }

    /// <summary>Tells the view of a weapon dropped in the level.</summary>
    /// <param name="mission">The loaded level.</param>
    /// <param name="pickup">The weapon.</param>
    void WeaponDropped(LoadedMission mission, GunPickup pickup)
    {
    }

    /// <summary>Tells the view a lying weapon is gone.</summary>
    /// <param name="pickup">The weapon.</param>
    void WeaponRemoved(GunPickup pickup)
    {
    }

    /// <summary>Draws the weapons lying in the level.</summary>
    /// <param name="eye">The eye, in metres.</param>
    /// <param name="projection">The world's projection.</param>
    void DrawLyingWeapons(in Camera3D eye, in Matrix4f projection)
    {
    }

    /// <summary>Draws the grenades, rockets and launcher rounds in the air.</summary>
    /// <param name="eye">The eye, in metres.</param>
    /// <param name="projection">The world's projection.</param>
    /// <param name="tickBlend">How far the frame is between ticks.</param>
    void DrawProjectiles(in Camera3D eye, in Matrix4f projection, float tickBlend);

    /// <summary>Draws the marks the player's rounds have left.</summary>
    /// <param name="eye">The eye, in metres.</param>
    /// <param name="projection">The world's projection.</param>
    /// <param name="poses">Where the world's placements are drawn this frame, which the marks ride.</param>
    void DrawMarks(in Camera3D eye, in Matrix4f projection, PlacementPose? poses);

    /// <summary>Draws one lamp for each of the level's security cameras.</summary>
    /// <param name="cameras">The cameras.</param>
    /// <param name="eye">Where the lamps are seen from.</param>
    /// <param name="projection">The projection to draw under.</param>
    /// <param name="tickFraction">How far the frame sits between the previous tick and the next.</param>
    /// <param name="smooth">Whether to draw a camera still turning between its ticks.</param>
    void DrawCameraLamps(IReadOnlyList<SecurityCamera> cameras, in Camera3D eye, in Matrix4f projection, float tickFraction, bool smooth)
    {
    }

    /// <summary>Draws glows over the world with the lamps' sprite: an alarm light's flare.</summary>
    /// <param name="glows">The glows.</param>
    /// <param name="eye">Where they are seen from.</param>
    /// <param name="projection">The projection to draw under.</param>
    void DrawGlows(IReadOnlyList<WorldGlow> glows, in Camera3D eye, in Matrix4f projection)
    {
    }
}
