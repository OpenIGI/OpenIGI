using OpenIGI.Engine.Events;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Services;
using OpenIGI.Platform;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // What settles a mission at the end of each of its ticks: the failed page, or the next mission.
    private readonly MissionOutcome _outcome;

    // A shell page over a mission whose level stays loaded under it: the failed page, or a co-op room gone.
    private void OpenOverMission(Frontend.Shell shell, Action<Frontend.Shell> page)
    {
        _computerOpen = false;
        _computerFlight.Reset();
        HostedPlayer?.SetMapComputerOpen(false);

        // The result is a shell page, not a drawn card, so its choices can be clicked and navigated like every other page
        // and the shell's own cursor is the one on screen. Menu mode hands it the frame's input and draw; the pause flag
        // holds the showcase off, because the level this is drawn over is still loaded -- the same pair the pause menu sets.
        _menuFlow.MenuMode = true;
        _menuFlow.PauseMenu = true;
        page(shell);

        // The platform cursor stays hidden: the shell draws its own, so revealing this one would put a second, unrelated
        // pointer beside it.
        _window.CursorVisible = false;
        HostedPlayer?.HoldStill();
        _window.CursorLocked = false;
    }

    /// <summary>The host as deciding a mission reads it.</summary>
    /// <param name="host">The host.</param>
    private sealed class MissionOutcomeHost(GameHost host) : IMissionOutcomeHost
    {
        public MissionObjectiveRuntime? Objectives => host._mission.Objectives;

        public CutsceneRuntime? Cutscenes => host._mission.Cutscenes;

        public ITitlePlayer? TitlePlayer => host.TitlePlayer;

        public MenuFlow Menu => host._menuFlow;

        public IGameWindow Window => host._window;

        public PlayerProfile? ActiveProfile => host._configuration.Active;

        public IMissionCatalogue<MissionDescriptor> Missions => host._missions;

        public GameEvents? Events => host._title is { } title && title.Services.TryGet(out GameEvents? events) ? events : null;

        public void OpenOverMission(Frontend.Shell shell, Action<Frontend.Shell> page) => host.OpenOverMission(shell, page);

        public void SaveConfiguration() => HostConfiguration.Save(host._configuration, host._logger);

        public void StartMission(int id) => host.StartMission(id);

        public bool FollowsSession => host.TickObserver is Multiplayer.ISessionFollower;

        public void ReleasePlayedMission() => host.ReleasePlayedMission();
    }
}
