using OpenIGI.Game.Scoring;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <content>A co-op mission's score at its end, told to every player.</content>
public sealed partial class ServerSession
{
    /// <summary>
    /// Scores the session's squad as one at the end of a co-op mission and tells every player (<see cref="SessionMessage.MissionScore"/>):
    /// each player's own counts from the world, put together into what holds for all of them (<see cref="SquadScore.Of"/>).
    /// </summary>
    /// <param name="sheet">The mission as the host's player played it: the squad's clock, difficulty and requirements.</param>
    /// <param name="secondsLeft">The seconds before the squad goes on by itself.</param>
    /// <returns>The squad's score, as told.</returns>
    public SquadScore TellMissionScore(MissionScoreSheet sheet, int secondsLeft = SquadScore.AutoAdvanceSeconds)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        List<SquadScoreMember> players = [];
        foreach (MatchPlayer player in Match.Players.Values.OrderBy(static p => p.Slot))
        {
            if (_world.ScoreOf(player.Slot) is { } score && EntityOfSlot(player.Slot) is var entity and >= 0)
            {
                players.Add(new SquadScoreMember(entity, player.Name, score));
            }
        }

        SquadScore squad = SquadScore.Of(sheet, players, _world.SquadTimesSpotted, secondsLeft);
        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.MissionScore, 8);
        SquadScoreMessage.Write(_writer, squad);
        _net.Broadcast(NetChannel.Reliable, _writer.AsSpan());
        return squad;
    }
}
