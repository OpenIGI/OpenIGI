using OpenIGI.Game.Scoring;

namespace OpenIGI.Game.Multiplayer;

/// <content>A co-op mission's score, as the server told it.</content>
public sealed partial class ClientSession
{
    /// <summary>Gets the score of the co-op mission the server last ended, until it is taken.</summary>
    public SquadScore? MissionScore { get; private set; }

    /// <summary>Takes the score the server last told, once.</summary>
    /// <returns>The score, or <see langword="null"/> when none has come since it was last taken.</returns>
    public SquadScore? TakeMissionScore()
    {
        SquadScore? score = MissionScore;
        MissionScore = null;
        return score;
    }
}
