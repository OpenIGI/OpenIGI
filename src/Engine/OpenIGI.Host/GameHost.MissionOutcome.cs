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

        public void CloseComputer()
        {
            host._computerOpen = false;
            host._computerFlight.Reset();
            host.HostedPlayer?.SetMapComputerOpen(false);
        }

        public void HoldPlayerStill() => host.HostedPlayer?.HoldStill();

        public void SaveConfiguration() => HostConfiguration.Save(host._configuration, host._logger);

        public void StartMission(int id) => host.StartMission(id);

        public bool FollowsSession => host.TickObserver is Multiplayer.ISessionFollower;

        public void ReleasePlayedMission() => host.ReleasePlayedMission();
    }
}
