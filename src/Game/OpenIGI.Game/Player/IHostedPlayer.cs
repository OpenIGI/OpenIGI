using OpenIGI.Core.Numerics;
using OpenIGI.Game.Config;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Player;

/// <summary>
/// A title's player that the host steers with its own input and looks through: its look and the cursor it captures, its
/// keys, the camera at its eyes, and the body that stands in the level.
/// </summary>
/// <remarks>
/// Project I.G.I.'s player is one. The host reads the player only through this, so the title's own types stay the title's.
/// </remarks>
public interface IHostedPlayer
{
    /// <summary>Gets the body that stands in the level.</summary>
    PlayerBody Body { get; }

    /// <summary>Gets the first-person rig, or <see langword="null"/> before it is built.</summary>
    HumanView? View { get; }

    /// <summary>Gets what the player carries, or <see langword="null"/> before it carries anything.</summary>
    PlayerWeapons? Weapons { get; }

    /// <summary>Gets the eye the scene is drawn from, in the renderer's metres.</summary>
    Camera3D Camera { get; }

    /// <summary>Gets where the eyes are, in world units.</summary>
    Vector3d EyeUnits { get; }

    /// <summary>Gets or sets the profile whose bindings the player's keys are read through.</summary>
    PlayerProfile? InputProfile { get; set; }

    /// <summary>Gets or sets how far the mouse turns the view.</summary>
    MouseLookSettings LookSettings { get; set; }

    /// <summary>Gets a value indicating whether the look holds the cursor.</summary>
    bool IsCapturingMouse { get; }

    /// <summary>Gets a value indicating whether the player is on a ladder.</summary>
    bool IsOnLadder { get; }

    /// <summary>Gets a value indicating whether a world interaction has the body.</summary>
    bool IsInWorldInteraction { get; }

    /// <summary>Gets a value indicating whether the player is on a stationary gun, whose look turns the barrel.</summary>
    bool IsMountedStationaryGun { get; }

    /// <summary>Gets a value indicating whether the player is looking through the binoculars.</summary>
    bool IsBinocularsOpen { get; }

    /// <summary>Gets a value indicating whether the binoculars stepped their zoom this tick.</summary>
    bool BinocularZoomSteppingThisTick { get; }

    /// <summary>Gets a value indicating whether a throw is being charged.</summary>
    bool IsChargingThrow { get; }

    /// <summary>Gets the grenades in the air.</summary>
    GrenadeSimulation Grenades { get; }

    /// <summary>Gets the rounds in flight.</summary>
    BallisticSimulation Ballistics { get; }

    /// <summary>Gets the marks the player's rounds have left.</summary>
    BulletMarks Marks { get; }

    /// <summary>Gets the ray that finds what a mark is left on, or <see langword="null"/> before the level has walls.</summary>
    WorldRayCast? MarkProbe { get; }

    /// <summary>Gets or sets the trace that covers the terrain and the placed collision, which a grenade bounces off.</summary>
    WorldRayCast? GrenadeWalls { get; set; }

    /// <summary>Turns the view by this frame's mouse and keys.</summary>
    /// <param name="input">This frame's input.</param>
    /// <param name="seconds">Real seconds since the last call, for the key turn.</param>
    void Look(InputState input, float seconds);

    /// <summary>Lets go of the cursor and stops the look, dropping the rig's tick pair, as a menu or a pause takes the view.</summary>
    void HoldStill();

    /// <summary>Lets go of the cursor and stops the look, keeping the rig's tick pair, as another camera takes the view.</summary>
    void ReleaseLook();

    /// <summary>Gets whether a configured action was pressed in the current input snapshot.</summary>
    /// <param name="input">The input.</param>
    /// <param name="action">The action.</param>
    /// <returns>Whether it was pressed.</returns>
    bool WasActionPressed(InputState input, PlayerAction action);

    /// <summary>Enters or leaves the map computer.</summary>
    /// <param name="open">Whether it opens.</param>
    void SetMapComputerOpen(bool open);

    /// <summary>Puts the player on the ground under a point.</summary>
    /// <param name="terrain">The level's ground, or <see langword="null"/> for an object-only interior.</param>
    /// <param name="at">Where to try standing, in the renderer's metres.</param>
    /// <param name="yaw">Which way to face.</param>
    /// <returns><see langword="false"/> when there is no ground under the point.</returns>
    bool Place(IGroundHeightField? terrain, Vector3d at, float yaw);

    /// <summary>Gets the eye for a frame between two ticks, in metres.</summary>
    /// <param name="alpha">How far the frame sits between the last two ticks.</param>
    /// <returns>The eye.</returns>
    Vector3d SmoothedEyeMetres(float alpha);

    /// <summary>Gets the camera's orientation for a frame between two ticks.</summary>
    /// <param name="alpha">How far the frame sits between the last two ticks.</param>
    /// <returns>The orientation.</returns>
    Matrix3f SmoothedLookRotation(float alpha);
}
