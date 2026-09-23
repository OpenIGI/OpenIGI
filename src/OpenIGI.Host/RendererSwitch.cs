using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>Where the game picks up again once the other renderer has been built.</summary>
/// <param name="MissionId">The mission being played, or nought for the menu.</param>
/// <param name="Conditions">The time of day and weather the campaign screen chose for it.</param>
/// <param name="Pose">Where the player stood or the free camera hung, or null for the mission's own start.</param>
/// <remarks>
/// A renderer is not swapped under a running game: every texture, mesh and pipeline the game holds was
/// made by the one it has, and the host keeps them for the life of a level. So the game is ended, the
/// other renderer is built on the same window, and a new game starts the same mission and puts the player
/// back -- the same load a mission start costs, and what it restores is what a look at the picture needs:
/// the place, the facing and the hour. Progress through the mission is not carried over.
/// </remarks>
public sealed record RendererSwitch(int MissionId, MissionConditions Conditions, RendererSwitchPose? Pose);

/// <summary>Where the view was when a renderer switch was asked for.</summary>
/// <param name="Position">The player's feet, or the free camera's eye when <paramref name="Flying"/>, in metres.</param>
/// <param name="Yaw">Which way it faced, in radians.</param>
/// <param name="Pitch">How far it looked up or down, in radians.</param>
/// <param name="Flying">Whether the free camera rather than the player was the view.</param>
public readonly record struct RendererSwitchPose(Vector3d Position, float Yaw, float Pitch, bool Flying);
