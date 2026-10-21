using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>Where the game picks up again once the other renderer has been built.</summary>
/// <param name="MissionId">The mission being played, or nought for the menu.</param>
/// <param name="Conditions">The time of day and weather the campaign screen chose for it.</param>
/// <param name="TimeHours">
/// The hour to light the level at, as <c>r_time_of_day</c> takes it: 0 to 24, or negative for the level's
/// own authored sun. Carried apart from the campaign's choice because the hour is set in more places than
/// that -- the settings menu, the console, a day that has been running -- and the switch has to come back
/// to the same sky whichever it was.
/// </param>
/// <param name="Pose">Where the player stood or the free camera hung, or null for the mission's own start.</param>
/// <param name="Frame">The last picture the switched-from renderer presented, or null where none was read.</param>
/// <param name="Target">
/// The API to build next, where <c>r_renderer</c> named one; null for the next in the switch key's order.
/// </param>
/// <param name="ShellTab">The menu section to open on when there is no mission to resume, or null for the landing page.</param>
/// <remarks>
/// A renderer is not swapped under a running game: every texture, mesh and pipeline the game holds was
/// made by the one it has, and the host keeps them for the life of a level. So the game is ended, the
/// other renderer is built on the same window, and a new game starts the same mission and puts the player
/// back -- the same load a mission start costs, and what it restores is what a look at the picture needs:
/// the place, the facing and the hour. Progress through the mission is not carried over.
/// <para>
/// The load is not shown. The switched-from renderer's last picture is held on screen, exactly as it
/// was, until the new one reveals the level, so the eye goes from one renderer's picture straight to the
/// other's -- which is what comparing them needs, and what a loading screen between them destroys.
/// </para>
/// </remarks>
public sealed record RendererSwitch(
    int MissionId,
    MissionConditions Conditions,
    float TimeHours,
    RendererSwitchPose? Pose,
    RendererSwitchFrame? Frame = null,
    RendererBackend? Target = null,
    string? ShellTab = null);

/// <summary>A picture held across a switch of renderer.</summary>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
/// <param name="Pixels">Its pixels, row-major and top row first.</param>
public sealed record RendererSwitchFrame(int Width, int Height, Bgra32[] Pixels);

/// <summary>Where the view was when a renderer switch was asked for.</summary>
/// <param name="Position">The player's feet, or the free camera's eye when <paramref name="Flying"/>, in metres.</param>
/// <param name="Yaw">Which way it faced, in radians.</param>
/// <param name="Pitch">How far it looked up or down, in radians.</param>
/// <param name="Flying">Whether the free camera rather than the player was the view.</param>
public readonly record struct RendererSwitchPose(Vector3d Position, float Yaw, float Pitch, bool Flying);
