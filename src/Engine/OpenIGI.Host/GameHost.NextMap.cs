using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Host.Multiplayer;

namespace OpenIGI.Host;

/// <content>
/// A listen host's next map: when a versus match is decided its players vote among this map again, the next mission and two
/// others under other modes; the same map under any mode starts over in place, another is gone into as the host's own missions
/// are. The same flow a dedicated server runs (<see cref="NextMapFlow"/>), and what carries a <c>/vote map</c> here too.
/// </content>
public sealed partial class GameHost
{
    private NextMapFlow? _nextMap;
    private HostedSession? _nextMapFor;

    /// <summary>Gets the missions this game has, in campaign order, with their names: what a server's vote offers.</summary>
    /// <remarks>A name is its menus' string; where the menus' language table isn't read (a server), it is empty and the vote
    /// names the mission by its number.</remarks>
    public IReadOnlyList<(int Id, string Name)> MissionList =>
        _missions is null ? [] : [.. _missions.InCampaignOrder().Select(m => (m.Id, _menuStrings?.Get(m.Name) is { } shown && shown != m.Name ? shown : string.Empty))];

    // Once a frame: the listen host's session followed from match to match.
    private void UpdateNextMap()
    {
        if (TickObserver is not HostedSession { IncludesHost: true } hosted)
        {
            _nextMap = null;
            _nextMapFor = null;
            return;
        }

        if (!ReferenceEquals(hosted, _nextMapFor))
        {
            _nextMapFor = hosted;
            hosted.RotatesMaps = ArenaLevel.IsVersus(hosted.Mode);
            hosted.ChangeMap = (mission, mode) => GoOnTo(hosted, mission, mode);
            _nextMap = new NextMapFlow(
                hosted,
                () =>
                {
                    IReadOnlyList<MapChoice> offered = NextMapFlow.Around(ArenaMissionList, MissionOf(hosted), hosted.Mode, Random.Shared);
                    _logger.Information("Coop", $"The match is decided; voting on the next map: {string.Join(", ", offered.Select(c => $"{c.Mission} {c.Mode}"))}.");
                    return offered;
                },
                picked =>
                {
                    _logger.Information("Coop", picked is null ? "Nobody voted; playing this map again." : $"The players picked mission {picked.Mission} ({picked.Mode}).");
                    if (GoOnTo(hosted, picked?.Mission ?? MissionOf(hosted), picked?.Mode) is { } problem)
                    {
                        _logger.Warning("Coop", $"The next map can't be played: {problem}");
                    }
                });
        }

        _nextMap?.Tick();
    }

    // The same map under the mode asked starts over where it stands; another is gone into, the joined players following.
    private string? GoOnTo(HostedSession hosted, int mission, string? mode)
    {
        if (mode is { Length: > 0 } && !hosted.UseMode(mode))
        {
            return $"'{mode}' is not a mode";
        }

        hosted.RotatesMaps = ArenaLevel.IsVersus(hosted.Mode);
        if (mission == MissionOf(hosted))
        {
            hosted.PlayAgain();
            return null;
        }

        if (_missions is null || !_missions.TryGet(mission, out _))
        {
            return $"this game has no mission {mission}";
        }

        _voteMission = mission;
        return null;
    }

    private static int MissionOf(HostedSession hosted) =>
        int.TryParse(hosted.Map, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int id) ? id : 0;
}
