using OpenIGI.Core.Configuration;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Player;
using OpenIGI.Host.Titles;

namespace OpenIGI.Host;

/// <content>Which mission the host holds: one per game, made the first time the game is played and kept for the host's life.</content>
public sealed partial class GameHost
{
    // The mission of the game whose missions bring their own systems (the catalog's), made when first played.
    private IHostedMission? _catalogMission;

    // The missions of the games that run their own levels behind their players, by the gameplay that makes the player.
    private readonly Dictionary<ITitleGameplay, IHostedMission> _titleMissions = new(ReferenceEqualityComparer.Instance);

    // The console a mission registers its commands in, once the host has started.
    private ConsoleSystem? _missionConsole;

    /// <summary>Takes up the mission of the game a binding plays: the game's own where it runs its level itself, else the catalog's.</summary>
    /// <param name="titles">The catalog.</param>
    /// <param name="binding">The game being played.</param>
    /// <remarks>
    /// A game that keeps its simulation behind its player takes none of another game's mission systems
    /// (<see cref="TitleMission"/>). A mission is kept once made, so what it registered and what it holds between levels
    /// survive a switch away and back.
    /// </remarks>
    private void BindMission(ITitleCatalog titles, TitleBinding binding)
    {
        IHostedMission mission;
        if (binding.Gameplay is { } own)
        {
            if (!_titleMissions.TryGetValue(own, out mission!))
            {
                mission = new TitleMission(_missionHost, own);
                _titleMissions.Add(own, mission);
                Welcome(mission);
            }
        }
        else
        {
            if (_catalogMission is null)
            {
                _catalogMission = titles.CreateHostedMission(_missionHost);
                Welcome(_catalogMission);
            }

            mission = _catalogMission;
        }

        _hosted = mission;
        _mission = mission.World;
    }

    // A mission made after the host started is given what the first was given at startup.
    private void Welcome(IHostedMission mission)
    {
        if (_missionConsole is { } console)
        {
            mission.Impacts.Register(console, () => _inspectorEye, _logger);
        }

        if (_audioOut?.Sounds is not null)
        {
            mission.Audio.OnDeviceOpened();
        }
    }
}
