namespace OpenIGI.Engine.Flow;

/// <summary>
/// What the flow state machine asks the game to do when a state is entered.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these lives above the engine kernel — menus, screens, missions — so the arbiter reaches
/// them through this interface instead of referencing them. That is the seam that keeps
/// <c>OpenIGI.Engine</c> free of <c>OpenIGI.Game</c>, and it is why a headless boot needs no menus at
/// all.
/// </para>
/// <para>
/// The bookkeeping around these calls — assigning the current state, resetting the tick rate, the
/// fall-through from <see cref="FlowEvent.EndGame"/> into <see cref="FlowEvent.MainMenu"/> — stays in
/// <see cref="FlowTask"/>, because it is the part that has to stay 1:1. See
/// the original.
/// </para>
/// </remarks>
public interface IFlowHost
{
    /// <summary>Shows the intro screens and movies.</summary>
    /// <remarks>The original's <c>Screen_Open(root, 0, 0)</c> (<c>0x4154B0</c>).</remarks>
    void ShowIntroScreens();

    /// <summary>Loads and shows the main menu.</summary>
    /// <remarks>
    /// The original's <c>MenuSystem_Load(root, "LOCAL:menusystem\mainmenu.qsc", 1, 1, 1)</c>
    /// (<c>0x418B00</c>). Called for both <see cref="FlowEvent.MainMenu"/> and, after the in-game task
    /// is destroyed, <see cref="FlowEvent.EndGame"/>.
    /// </remarks>
    void ShowMainMenu();

    /// <summary>Shows the outro screens.</summary>
    /// <remarks>The original's <c>Screen_Open(root, 1, 0)</c>.</remarks>
    void ShowOutroScreens();

    /// <summary>Shows the intro screens again as an attract loop.</summary>
    /// <remarks>
    /// The original's <c>Screen_Open(root, 0, 1)</c> — the same list as
    /// <see cref="ShowIntroScreens"/> with the screen task's third argument set.
    /// </remarks>
    void ShowAttractScreens();

    /// <summary>Creates the in-game task as a child of the flow task.</summary>
    /// <param name="flow">The flow task, which is the new task's parent.</param>
    /// <remarks>
    /// The original's <c>Game_Begin</c> (<c>0x416900</c>). The task it creates outlives level reloads —
    /// <see cref="FlowEvent.RestartGame"/> does not recreate it — and is destroyed by
    /// <see cref="EndGame"/>.
    /// </remarks>
    void BeginGame(FlowTask flow);

    /// <summary>Loads the current mission.</summary>
    /// <remarks>
    /// The original's <c>Level_LoadAll</c> (<c>0x416FE0</c>), which wraps the whole sequence in the
    /// excluded-time stopwatch so the load does not leave the simulation ten ticks behind.
    /// </remarks>
    void LoadLevel();

    /// <summary>Destroys the in-game task.</summary>
    /// <remarks>The original's <c>Task_Delete(Game_GetTask())</c>.</remarks>
    void EndGame();
}
