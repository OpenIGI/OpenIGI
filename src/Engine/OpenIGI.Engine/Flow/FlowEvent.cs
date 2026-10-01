namespace OpenIGI.Engine.Flow;

/// <summary>
/// The eight states the game moves between, and the requests that move it.
/// </summary>
/// <remarks>
/// <para>
/// The original stores these as bare integers in two fields of the root <c>Flow</c> task: the state in
/// effect and the one pending. Five have script-visible names, registered as constants by
/// <c>0x401FC0</c>; the other three are only ever requested from C. See
/// the original and §5.4.
/// </para>
/// <para>
/// The usual path is <see cref="Intro"/> or <see cref="Game"/> at boot, then
/// <see cref="MainMenu"/> → <see cref="RestartGame"/> (load) → <see cref="Game"/> (play) →
/// <see cref="EndGame"/> (unload) → <see cref="MainMenu"/>.
/// </para>
/// </remarks>
public enum FlowEvent
{
    /// <summary>No request pending. Not a state the game is ever in.</summary>
    None = 0,

    /// <summary>Leave the frame immediately. Script name <c>FLOW_EVENT_QUIT</c>.</summary>
    /// <remarks>
    /// The only event that unwinds: it is the sole caller of the original's <c>longjmp</c>, modelled by
    /// <see cref="GameAbortException"/>. On its own it does not end the process — the quit menu item
    /// clears the run flag separately, and a script that requests this without doing so simply loses the
    /// rest of one frame.
    /// </remarks>
    Quit = 1,

    /// <summary>Show the intro screens and movies. Script name <c>FLOW_EVENT_INTRO</c>.</summary>
    Intro = 2,

    /// <summary>Load and show the main menu. Script name <c>FLOW_EVENT_MAINMENU</c>.</summary>
    MainMenu = 3,

    /// <summary>Create the in-game task and start playing. Script name <c>FLOW_EVENT_GAME</c>.</summary>
    /// <remarks>Resets the tick rate to 30 Hz, which restarts the frame schedule.</remarks>
    Game = 4,

    /// <summary>Load the current mission, then continue in <see cref="Game"/>. Script name <c>FLOW_EVENT_RESTART_GAME</c>.</summary>
    /// <remarks>
    /// Does <em>not</em> create the in-game task — that is <see cref="Game"/>'s job, and the task
    /// survives a reload. The state ends up at <see cref="Game"/> without the event ever being requested.
    /// </remarks>
    RestartGame = 5,

    /// <summary>Destroy the in-game task and return to the main menu. No script name.</summary>
    /// <remarks>Falls straight into <see cref="MainMenu"/>'s handling rather than requesting it.</remarks>
    EndGame = 6,

    /// <summary>Show the outro screens. No script name.</summary>
    /// <remarks>Requested when a finished mission has no successor.</remarks>
    Outro = 7,

    /// <summary>Replay the intro screens as an attract loop. No script name.</summary>
    /// <remarks>Requested by the main menu after 3600 idle ticks — two minutes at 30 Hz.</remarks>
    AttractScreens = 8,
}
