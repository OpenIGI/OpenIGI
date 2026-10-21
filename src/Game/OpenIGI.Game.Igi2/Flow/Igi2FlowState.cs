using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// The game-wide switches the level's scripts read and flip through natives: the cutscene break key, whether the in-game
/// menu may open, and whether the music plays.
/// </summary>
/// <remarks>
/// Every native here returns one, so a script chaining them with <c>&amp;&amp;</c> — mission 1's intro runs
/// <c>MenuManager_SetEnabled (FALSE) &amp;&amp; Game_DisableMusic()</c> — makes every call.
/// </remarks>
public sealed class Igi2FlowState
{
    /// <summary>The native that reads the cutscene break key.</summary>
    public const string GetBreakCutSceneKeyNative = "LevelFlow_GetBreakCutSceneKey";

    /// <summary>The native that lets the in-game menu open, or not.</summary>
    public const string SetMenuEnabledNative = "MenuManager_SetEnabled";

    /// <summary>The native that lets the music play.</summary>
    public const string EnableMusicNative = "Game_EnableMusic";

    /// <summary>The native that silences the music.</summary>
    public const string DisableMusicNative = "Game_DisableMusic";

    /// <summary>Gets or sets a value indicating whether the break key is down, retail's Escape (<c>anCurrentKey[1]</c>).</summary>
    public bool IsBreakKeyDown { get; set; }

    /// <summary>Gets a value indicating whether the in-game menu may open, the menu manager's state 19 against 18.</summary>
    public bool IsMenuEnabled { get; private set; } = true;

    /// <summary>Gets a value indicating whether the music plays, <c>Game_isMusicEnabled</c>.</summary>
    public bool IsMusicEnabled { get; private set; } = true;

    /// <summary>Registers the natives for compiled scripts and script text alike.</summary>
    /// <param name="natives">The namespace.</param>
    /// <param name="functions">The natives script text may call.</param>
    public void Register(NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);

        // LevelFlow_GetBreakCutSceneKeyCB (igi2 0x521910): the key's state byte, 0x80 when down.
        functions.Register(natives, GetBreakCutSceneKeyNative, _ => QvmValue.FromInt32(IsBreakKeyDown ? 0x80 : 0), 0);

        // MenuManager_EnableCB (igi2 0x430E40): enabled only by exactly one.
        functions.Register(natives, SetMenuEnabledNative, call =>
        {
            IsMenuEnabled = call.GetInt32(0) == 1;
            return QvmValue.FromInt32(1);
        }, 1);

        // Game_EnableMusicCB (igi2 0x406E10) and Game_DisableMusicCB (0x406E60).
        functions.Register(natives, EnableMusicNative, _ =>
        {
            IsMusicEnabled = true;
            return QvmValue.FromInt32(1);
        }, 0);
        functions.Register(natives, DisableMusicNative, _ =>
        {
            IsMusicEnabled = false;
            return QvmValue.FromInt32(1);
        }, 0);
    }

    /// <summary>Puts the switches back as a level starts them.</summary>
    public void Reset()
    {
        IsBreakKeyDown = false;
        IsMenuEnabled = true;
        IsMusicEnabled = true;
    }
}
