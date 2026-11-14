using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Player;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>
    /// The quick save and quick load keys, for a mission that keeps save games: F5 saves to the quick slot, F9 loads the newest
    /// save of the mission. Not while a menu is up, and not in a session with other players, whose level is the server's.
    /// </summary>
    /// <remarks>
    /// Not retail keys: I.G.I. 2 saves through its computer, and Project I.G.I. kept no save games. They are bindings of the
    /// profile like any other, kept by name beside the title's own extra ones, since the shared action word has no bit left.
    /// </remarks>
    private void HandleSaveKeys()
    {
        RunComputerSaves();
        ulong pressed = TitleInputBindings.Sample(_configuration.Active, ViewInput, SaveBindings, pressed: true);
        // Not under a menu -- but the quick load does answer the failed page, where it is what a dead player reaches for.
        bool save = (pressed & QuickSaveBit) != 0;
        if (pressed != 0 && (!_menuFlow.MenuMode || (!save && _session.FailedMenu)))
        {
            QuickSaveOrLoad(save);
        }
    }

    private const ulong QuickSaveBit = 1UL << 62;
    private const ulong QuickLoadBit = 1UL << 63;

    // The two bindings, F5 and F9 until the player chooses others.
    private static readonly TitleInputBinding[] SaveBindings =
    [
        new(
            QuickSaveBit,
            new Game.Config.InputBinding("QuickSave", Game.Config.InputBindingKeyNames.KeyboardDevice, Game.Config.InputBindingKeyNames.ToRetailKeyboardKey(Input.KeyCode.F5)),
            "Quick save"),
        new(
            QuickLoadBit,
            new Game.Config.InputBinding("QuickLoad", Game.Config.InputBindingKeyNames.KeyboardDevice, Game.Config.InputBindingKeyNames.ToRetailKeyboardKey(Input.KeyCode.F9)),
            "Quick load"),
    ];

    /// <summary>Gets the bindings a profile keeps beside the shared actions: the title's own, then the save keys.</summary>
    private IReadOnlyList<TitleInputBinding> ExtraBindings => [.. Gameplay?.InputBindings ?? [], .. SaveBindings];

    /// <summary>Registers <c>quicksave</c> and <c>quickload</c>, which do what the keys do.</summary>
    /// <param name="console">The console to register with.</param>
    private void RegisterSaveCommands(OpenIGI.Core.Configuration.ConsoleSystem console)
    {
        console.Commands.Add("quicksave", "Saves the mission to the quick slot, as F5 does.", _ => QuickSaveOrLoad(save: true));
        console.Commands.Add("quickload", "Loads the mission's newest save, as F9 does.", _ => QuickSaveOrLoad(save: false));
    }

    // One quick save or quick load, said on the screen and in the log.
    private void QuickSaveOrLoad(bool save)
    {
        if (MissionSaveGames is not { } saves)
        {
            _logger.Information("Saves", "This mission keeps no save games.");
            return;
        }

        string said;
        if (save)
        {
            said = saves.QuickSave() ? "Saving complete" : saves.SavesLeft == 0 ? "No saves are left for this mission" : "Saving failed";
        }
        else
        {
            said = saves.QuickLoad() ? "Loading complete" : "Loading failed";
        }

        _logger.Information("Saves", said + ".");
        _mission.Progress?.ShowTransientMessage(said);
    }
}
