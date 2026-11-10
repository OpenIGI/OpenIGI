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
    /// Not retail keys: I.G.I. 2 saves through its computer, and Project I.G.I. kept no save games. Fixed keys for now -- the
    /// rebindable action word has no bit left for two more.
    /// </remarks>
    private void HandleSaveKeys()
    {
        bool save = _window.Input.Keyboard.WasPressed(Input.KeyCode.F5);
        bool load = _window.Input.Keyboard.WasPressed(Input.KeyCode.F9);
        if ((save || load) && !_menuFlow.MenuMode)
        {
            QuickSaveOrLoad(save);
        }
    }

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
        if (TickObserver is not null || (TitlePlayer?.SaveGames ?? _hosted.SaveGames) is not { } saves)
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
        _mission.Objectives?.ShowTransientMessage(said);
    }
}
