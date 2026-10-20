using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;

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
    /// <summary>Gets additional, rebindable controls in this title's own action vocabulary.</summary>
    IReadOnlyList<TitleInputBinding> InputBindings => [];

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
/// <param name="Audio">The host's mixer, or <see langword="null"/> where nothing is heard.</param>
/// <param name="Natives">
/// The namespace the level's scripts resolved against, holding its tasks' exported variables, or <see langword="null"/> when
/// the level runs no scripts of its own.
/// </param>
public readonly record struct TitlePlayerRequest(
    WorldBuildRequest World,
    WorldBuildResult Built,
    IServiceCatalog Services,
    AiDifficulty Difficulty,
    ITitleAudio? Audio = null,
    NativeRegistry? Natives = null);

/// <summary>One tick of the player's input, in the host's vocabulary.</summary>
/// <param name="MouseX">The relative mouse movement since the last tick, in device counts, right positive.</param>
/// <param name="MouseY">The relative mouse movement since the last tick, in device counts, down positive.</param>
/// <param name="Held">The actions held this tick.</param>
/// <param name="Pressed">The actions pressed since the last tick.</param>
/// <param name="MouseSensitivity">The profile's mouse sensitivity.</param>
/// <param name="InvertMouse">Whether the profile inverts the vertical look.</param>
/// <param name="BreakCutscene">Whether the cutscene break key is down: Escape, the key retail's level scripts read.</param>
/// <param name="UnfilteredMouse">Whether Smooth Motion uses linear mouse counts instead of the title's tick filters.</param>
/// <param name="TitleHeld">Additional held actions in the title's vocabulary.</param>
/// <param name="TitlePressed">Additional actions pressed since the last tick.</param>
/// <param name="Raw">The fixed keys and buttons held, and the wheel's turns since the last tick, which a title's own screens read past the bindings.</param>
public readonly record struct TitlePlayerInput(
    float MouseX,
    float MouseY,
    PlayerAction Held,
    PlayerAction Pressed,
    float MouseSensitivity,
    bool InvertMouse,
    bool BreakCutscene = false,
    bool UnfilteredMouse = false,
    ulong TitleHeld = 0,
    ulong TitlePressed = 0,
    TitleRawKeys Raw = TitleRawKeys.None);

/// <summary>
/// Keys and buttons a title's own screens read directly, as the originals read the keyboard and the mouse past their bindings: held at
/// the tick, the wheel's flags set when it turned since the last.
/// </summary>
[Flags]
public enum TitleRawKeys
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>The left mouse button.</summary>
    MouseLeft = 0x1,

    /// <summary>The right mouse button.</summary>
    MouseRight = 0x2,

    /// <summary>The wheel turned away from the user.</summary>
    WheelUp = 0x4,

    /// <summary>The wheel turned toward the user.</summary>
    WheelDown = 0x8,

    /// <summary>The up arrow.</summary>
    Up = 0x10,

    /// <summary>The down arrow.</summary>
    Down = 0x20,

    /// <summary>The left arrow.</summary>
    Left = 0x40,

    /// <summary>The right arrow.</summary>
    Right = 0x80,

    /// <summary>Enter.</summary>
    Enter = 0x100,

    /// <summary>Home.</summary>
    Home = 0x200,

    /// <summary>Either Ctrl.</summary>
    Control = 0x400,
}

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

    /// <summary>Gets a value indicating whether the title's level played a cutscene on the last tick, which Escape breaks rather than pausing.</summary>
    bool IsCutscenePlaying => false;

    /// <summary>Gets a value indicating whether the title's level lets the in-game menu open.</summary>
    bool IsMenuEnabled => true;

    /// <summary>Gets a value indicating whether the title hides the weather, as a screen of its own over the view does.</summary>
    bool IsWeatherHidden => false;

    /// <summary>Gets where the title's own objects move what they placed, or <see langword="null"/> when nothing it placed moves.</summary>
    IPlacementMotion? Motions => null;

    /// <summary>Gets which of the title's own placements are destroyed, or <see langword="null"/> when nothing it placed can be.</summary>
    IPlacementDestruction? Destruction => null;

    /// <summary>Gets which of their models the title's own placements wear, or <see langword="null"/> when none changes model.</summary>
    IPlacementVariant? Variants => null;

    /// <summary>Gets the title's own save games for the running mission, or <see langword="null"/> when it keeps none.</summary>
    ITitleSaveGames? SaveGames => null;

    /// <summary>
    /// Gets how the title's own level has ended the mission, for a title whose level decides it rather than the host's objectives.
    /// </summary>
    TitleMissionOutcome Outcome => TitleMissionOutcome.Running;

    /// <summary>Tells the title its mission is complete and which mission follows, before that one is loaded.</summary>
    /// <param name="nextMissionId">The mission that follows.</param>
    void MissionCompleted(int nextMissionId)
    {
    }

    /// <summary>Runs one game tick.</summary>
    /// <param name="input">The tick's input.</param>
    void Tick(in TitlePlayerInput input);
}

/// <summary>How a title's level has ended its mission.</summary>
public enum TitleMissionOutcome
{
    /// <summary>The mission goes on.</summary>
    Running,

    /// <summary>The level has completed it.</summary>
    Completed,

    /// <summary>The level has failed it.</summary>
    Failed,
}
