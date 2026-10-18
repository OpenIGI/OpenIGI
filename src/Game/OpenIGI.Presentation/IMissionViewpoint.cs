using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>What the mission's viewpoint is composed from on one frame.</summary>
/// <param name="PlayerMode">Whether the player walks the level, rather than the free camera flying it.</param>
/// <param name="Smooth">Whether the frame is smoothed between ticks.</param>
/// <param name="TickBlend">How far the frame is between the last tick and the next.</param>
/// <param name="TitleEye">The eye of a player the title brings itself, or <see langword="null"/> when it brings none.</param>
/// <param name="FreeEye">The free camera's eye.</param>
/// <param name="ThirdPersonAllowed">
/// Whether nothing the host shows has taken the view: the failure menu, the computer, the entry shot, a cutscene or the menu's orbit.
/// </param>
/// <param name="Paused">Whether the game is paused, which holds the over-the-shoulder move where it is.</param>
/// <param name="NowMilliseconds">The loop's clock, which the over-the-shoulder move is timed on.</param>
/// <param name="Walls">What the over-the-shoulder camera keeps out of, or <see langword="null"/> when the level has nothing yet.</param>
public readonly record struct ViewpointFrame(
    bool PlayerMode,
    bool Smooth,
    float TickBlend,
    Camera3D? TitleEye,
    Camera3D FreeEye,
    bool ThirdPersonAllowed,
    bool Paused,
    int NowMilliseconds,
    DeathCameraVantage.Blocked? Walls);

/// <summary>
/// A title's own player's viewpoint: the eye it looks through, and the cameras that take it -- the one that watches the body
/// fall, the orbit around a climbing or interacting body, and the move over the shoulder.
/// </summary>
public interface IMissionViewpoint
{
    /// <summary>Sets how far the mouse turns the cameras it orbits: the player's own look settings.</summary>
    MouseLookSettings LookSettings { set; }

    /// <summary>Gets a value indicating whether the dead player is watched from the camera over the body.</summary>
    bool IsDeathViewActive { get; }

    /// <summary>Gets a value indicating whether the orbit around a climbing or interacting body has the view.</summary>
    bool IsInteractionViewActive { get; }

    /// <summary>Gets a value indicating whether the first-person arms and weapon are drawn this frame.</summary>
    bool ShowsViewModel { get; }

    /// <summary>Gets a value indicating whether the player's body is drawn this frame.</summary>
    bool ShowsBody { get; }

    /// <summary>Gets a value indicating whether the camera is anywhere other than exactly at the player's eye.</summary>
    bool IsThirdPersonEngaged { get; }

    /// <summary>Gets a value indicating whether the title has a player of its own in the level.</summary>
    bool HasPlayer { get; }

    /// <summary>Gets where the player's feet are, in world units, or <see langword="null"/> when there is no player.</summary>
    Vector3d? BodyPositionUnits { get; }

    /// <summary>Gets where the player's eyes are, in world units, or <see langword="null"/> when there is no player.</summary>
    Vector3d? EyeUnits { get; }

    /// <summary>Gets the developer overlay's lines about the walking player: where it stands, which way it looks, what it touches.</summary>
    /// <returns>The lines, or <see langword="null"/> when there is no player.</returns>
    IReadOnlyList<string>? DebugLines();

    /// <summary>Gets the trace that covers the terrain and the placed collision, which the rain lands on, or <see langword="null"/>.</summary>
    WorldRayCast? GroundTrace { get; }

    /// <summary>Gets a value indicating whether the player is looking through the binoculars.</summary>
    bool IsBinocularsOpen { get; }

    /// <summary>Gets a value indicating whether the player has a HUD this frame: a first-person view, and no gun's sight taking it.</summary>
    bool HasPlayerHud { get; }

    /// <summary>Gets a value indicating whether the player's HUD is the first-person one, rather than a climb's or an interaction's.</summary>
    bool HasFirstPersonHud { get; }

    /// <summary>Gets the player's sight zoom this frame, or <see langword="null"/> when there is no player.</summary>
    /// <param name="tickBlend">How far the frame is between ticks.</param>
    /// <returns>The zoom.</returns>
    SightZoomFrame? SightZoom(float tickBlend);

    /// <summary>Gets how far the binoculars have zoomed in, from nought at the widest to one at the tightest.</summary>
    /// <param name="tanX">The frame's horizontal half-angle tangent.</param>
    /// <returns>The fraction, or nought when there is no player.</returns>
    float BinocularZoomIn(float tanX);

    /// <summary>Gets what the player's interaction resolver is offering this frame.</summary>
    /// <param name="tickBlend">How far the frame is between ticks.</param>
    /// <returns>The target, or <see langword="null"/> when there is none.</returns>
    InteractionPromptTarget? InteractionPrompt(float tickBlend);

    /// <summary>Gets the walking player's own eye, smoothed between ticks when asked, which the map computer is held in front of.</summary>
    /// <param name="smooth">Whether the frame is smoothed between ticks.</param>
    /// <param name="tickBlend">How far the frame is between ticks.</param>
    /// <returns>The eye, or <see langword="null"/> when there is no player or a climb or an interaction has the body.</returns>
    Camera3D? WalkingEye(bool smooth, float tickBlend);

    /// <summary>Makes the death and interaction cameras afresh, as the frame loop starts.</summary>
    void Renew();

    /// <summary>Turns the camera over the dead body with the mouse.</summary>
    /// <param name="deltaX">The mouse's horizontal counts.</param>
    /// <param name="deltaY">The mouse's vertical counts.</param>
    void ControlDeathView(float deltaX, float deltaY);

    /// <summary>Turns the orbit around a climbing or interacting body with the mouse.</summary>
    /// <param name="deltaX">The mouse's horizontal counts.</param>
    /// <param name="deltaY">The mouse's vertical counts.</param>
    void ControlInteractionView(float deltaX, float deltaY);

    /// <summary>Hands the view to the orbit behind a climbing or interacting body.</summary>
    /// <param name="feet">The body's feet, in world units.</param>
    /// <param name="facing">The yaw the body faces.</param>
    /// <param name="walls">What pulls the orbit in, or <see langword="null"/>.</param>
    void BeginInteractionView(Vector3d feet, float facing, WorldRayCast? walls);

    /// <summary>Moves the orbit's pivot after the body.</summary>
    /// <param name="feet">The body's feet, in world units.</param>
    void FollowInteractionView(Vector3d feet);

    /// <summary>Eases the orbit once on the tick.</summary>
    void TickInteractionView();

    /// <summary>Takes the view back from the orbit.</summary>
    void ResetInteractionView();

    /// <summary>Hands the view to the camera that pulls back over the fallen body.</summary>
    /// <param name="position">The body, in world units.</param>
    /// <param name="facing">The way it was thrown.</param>
    /// <param name="walls">What pulls the camera in.</param>
    void BeginDeathView(Vector3d position, float facing, WorldRayCast walls);

    /// <summary>Points the death camera at where the body has come to rest.</summary>
    /// <param name="centre">The body's centre.</param>
    void FollowDeathView(Vector3d centre);

    /// <summary>Puts the death camera away, as a new mission starts.</summary>
    void ResetDeathView();

    /// <summary>Composes the eye the frame is drawn from.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="bodyEye">The body's own eye, before the move over the shoulder takes the camera off it.</param>
    /// <returns>The eye.</returns>
    Camera3D ComposeEye(in ViewpointFrame frame, out Camera3D bodyEye);
}
