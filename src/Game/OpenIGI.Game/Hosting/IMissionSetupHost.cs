using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Hosting;

/// <summary>What bringing up a title's cutscenes and objectives asks of whatever plays and shows them.</summary>
public interface IMissionSetupHost : IGuardRoundHost
{
    /// <summary>Gets the log.</summary>
    IOpenIgiLogger Logger { get; }

    /// <summary>Gets the game's loop, whose variables the runtimes read and write.</summary>
    GameLoop Game { get; }

    /// <summary>Gets the resources the mission's text is read from.</summary>
    ResourceRegistry Resources { get; }

    /// <summary>Gets the difficulty <c>LevelFlow</c> exports as <c>eDifficulty</c>, unclamped.</summary>
    int ScriptDifficulty { get; }

    /// <summary>Gets the difficulty every guard reads its combat row at: the <c>-difficulty</c> override, or the active profile's.</summary>
    AiDifficulty Difficulty { get; }

    /// <summary>Gets where <see cref="Difficulty"/> came from, for the log.</summary>
    string DifficultySource { get; }

    /// <summary>Gets where the game is installed, which the nav graphs are read from.</summary>
    string GamePath { get; }

    /// <summary>Gets what runs work off the frame: the host's scheduler, which a mission hands its own reads to.</summary>
    OpenIGI.Core.Threading.IWorkScheduler Work => OpenIGI.Core.Threading.InlineWorkScheduler.Instance;

    /// <summary>Gets a value indicating whether the level's cinematics play, or are broken as Escape breaks them.</summary>
    bool PlayCutscenes { get; }

    /// <summary>Gets the active profile's place among the profiles, which names its save games.</summary>
    int ActiveProfileIndex => 0;

    /// <summary>Gets the mods that change play this session is played with, which a save game records beside itself.</summary>
    IReadOnlyList<OpenIGI.Core.Content.ModReference> PlayedMods => [];

    /// <summary>Starts the mission that is up afresh, as the pause menu's restart does.</summary>
    void RestartMission()
    {
    }

    /// <summary>
    /// Tells the host a save game has been read onto the level that is up: what the host keeps of the level played on -- a
    /// mission it had failed -- is over.
    /// </summary>
    void SaveReadOntoLevel()
    {
    }

    /// <summary>Answers a native the level's cutscene scripts call that the host owns: the music, the menus.</summary>
    /// <param name="name">The native.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>Its answer, or <see langword="null"/> when the host has none.</returns>
    QvmValue? HostNative(string name, ScriptArguments arguments);

    /// <summary>Plays or stops a cutscene's sound.</summary>
    /// <param name="sound">What changed.</param>
    void CutsceneSound(CutsceneSoundEvent sound);

    /// <summary>Records that a stage of the load has finished, for the load's timings.</summary>
    /// <param name="what">What finished.</param>
    void EndStage(string what);

    /// <summary>Stops every cutscene sound.</summary>
    void StopCutsceneSounds();

    /// <summary>Reads a key, for the <c>Key0..9.isPressed</c> globals.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Whether it is held.</returns>
    bool IsKeyDown(KeyCode key);
}
