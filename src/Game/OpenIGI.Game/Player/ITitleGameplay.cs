using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Player;

/// <summary>A title that brings its own player rather than walking the host's.</summary>
/// <remarks>
/// <para>
/// The host's walking rig is Project I.G.I.'s, built over that game's body, weapons and HUD. A title whose player
/// is a different machine — I.G.I. 2's <c>HumanPlayer</c>, with its own states, collision and view — supplies it
/// here, and the host steps it and looks through its eyes. A title that has none returns <see langword="null"/>
/// from the composition root and keeps the host's own player.
/// </para>
/// <para>
/// A fifth lookup in the composition root for the reason the other four are: the reference graph forbids putting it
/// on <see cref="IGameTitleRuntime"/>, and only that root may name a title.
/// </para>
/// </remarks>
public interface ITitleGameplay
{
    /// <summary>Makes the player a loaded level places.</summary>
    /// <param name="request">The level, what it was built into, and the session it runs in.</param>
    /// <returns>The player, or <see langword="null"/> when the level places none.</returns>
    ITitlePlayer? CreatePlayer(in TitlePlayerRequest request);
}

/// <summary>What a title needs to make its player.</summary>
/// <param name="World">The request the level's scene was built from: the mission, the level tree and its resources.</param>
/// <param name="Built">What the scene walk produced: the placements and the ground.</param>
/// <param name="Services">The tables the title's content modules loaded.</param>
/// <param name="Difficulty">The active profile's difficulty.</param>
public readonly record struct TitlePlayerRequest(
    WorldBuildRequest World,
    WorldBuildResult Built,
    IServiceCatalog Services,
    AiDifficulty Difficulty);

/// <summary>One tick of the player's input, in the host's vocabulary.</summary>
/// <param name="MouseX">The relative mouse movement since the last tick, in device counts, right positive.</param>
/// <param name="MouseY">The relative mouse movement since the last tick, in device counts, down positive.</param>
/// <param name="Held">The actions held this tick.</param>
/// <param name="Pressed">The actions pressed since the last tick.</param>
/// <param name="MouseSensitivity">The profile's mouse sensitivity.</param>
/// <param name="InvertMouse">Whether the profile inverts the vertical look.</param>
public readonly record struct TitlePlayerInput(
    float MouseX,
    float MouseY,
    PlayerAction Held,
    PlayerAction Pressed,
    float MouseSensitivity,
    bool InvertMouse);

/// <summary>A title's own player, as the host steps it and looks through it.</summary>
public interface ITitlePlayer
{
    /// <summary>Gets where the player's feet are, in world units.</summary>
    Vector3d Position { get; }

    /// <summary>Gets where the player's eyes are, in world units.</summary>
    Vector3d EyePosition { get; }

    /// <summary>Gets which way the eyes look: rows are the view's right, up and forward axes, the camera's convention.</summary>
    Matrix3f ViewOrientation { get; }

    /// <summary>Gets how wide the eyes see, as the tangent of half the view's horizontal angle: one, ninety degrees, unless a zoom narrows it.</summary>
    float FieldOfViewScale => 1f;

    /// <summary>Runs one game tick.</summary>
    /// <param name="input">The tick's input.</param>
    void Tick(in TitlePlayerInput input);
}
