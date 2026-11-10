using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Computer;
using OpenIGI.Game.Player;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // What the computer's save page saves with, made once and asked for the mission's saves each time.
    private HostComputerSaves? _computerSaves;

    // The save page asked the computer to close: its upload is done, or a download is under way.
    private bool _computerQuit;

    /// <summary>Gets the save games of the mission that is up, or none: a session with other players keeps none of its own.</summary>
    private ITitleSaveGames? MissionSaveGames => TickObserver is null ? TitlePlayer?.SaveGames ?? _hosted.SaveGames : null;

    /// <summary>
    /// Gets what the computer's save page saves and loads with -- the remastered computer's, and Project I.G.I.'s own -- or
    /// <see langword="null"/> for a mission that keeps no save games.
    /// </summary>
    /// <remarks>
    /// Saving is the computer's, as it is in I.G.I. 2 (<c>ComputerSaveMenu</c>): an upload to one of five slots that takes a
    /// moment and one of the battery's charges. The page runs in the mission's tick or in the display's frame; what it asks for
    /// is done here between two ticks, as <c>HumanPlayer_RunDelayedHandler</c> does it after the player's run.
    /// </remarks>
    private IComputerSaves? ComputerSaves => MissionSaveGames is null ? null : _computerSaves ??= new HostComputerSaves(this);

    /// <summary>Does what the computer's save page asked for since the last frame: the load, then the save.</summary>
    private void RunComputerSaves()
    {
        if (_computerSaves is not { } asked || (asked.PendingLoad == 0 && asked.PendingSave == 0))
        {
            return;
        }

        int load = asked.PendingLoad, save = asked.PendingSave;
        asked.PendingLoad = 0;
        asked.PendingSave = 0;
        if (MissionSaveGames is not { } saves)
        {
            return;
        }

        string? said = null;
        if (load != 0)
        {
            said = saves.Load(load - 1) ? "Loading complete" : "Loading failed";
        }

        if (save != 0)
        {
            said = saves.Save(save - 1) ? "Saving complete" : saves.SavesLeft == 0 ? "No saves are left for this mission" : "Saving failed";
        }

        if (said is not null)
        {
            _logger.Information("Saves", said + ".");
            _mission.Objectives?.ShowTransientMessage(said);
        }
    }

    // The mission's save games as a computer's save page asks of them.
    private sealed class HostComputerSaves(GameHost host) : IComputerSaves
    {
        private IReadOnlyList<TitleSaveSlot>? _listed;

        /// <summary>Gets or sets the slot a load was asked from, one past it, nought for none.</summary>
        public int PendingLoad { get; set; }

        /// <summary>Gets or sets the slot a finished upload is written to, one past it, nought for none.</summary>
        public int PendingSave { get; set; }

        public float BatteryLevel => host.MissionSaveGames?.BatteryLevel ?? 0f;

        public bool IsSaveAllowed => host.MissionSaveGames?.CanSave ?? false;

        public int QuickLoadSlot => 0;

        public (bool IsUsed, string Info, string When) Slot(int slot)
        {
            // The page reads its slots in a row, from the first: one listing serves them all.
            if (slot == 0 || _listed is null)
            {
                _listed = host.MissionSaveGames?.Slots ?? [];
            }

            foreach (TitleSaveSlot listed in _listed)
            {
                if (listed.Slot == slot && listed.IsUsed)
                {
                    return (true, "Mission time: " + listed.Description, listed.When);
                }
            }

            return (false, string.Empty, string.Empty);
        }

        // The charge is used when the save is written, which counts itself.
        public void BeginSave(int slot)
        {
        }

        public void FinishSave(int slot) => PendingSave = slot + 1;

        public void Load(int slot) => PendingLoad = slot + 1;

        // The computer holds the player's input until it has closed.
        public void FreezeFire()
        {
        }

        public void QuitComputer() => host._computerQuit = true;
    }
}
