using OpenIGI.Game.Scoring;

namespace OpenIGI.Host.Multiplayer;

/// <summary>
/// A co-op session's part in a completed mission's score: the server scores every player and tells them all; a client takes what
/// it was told. <b>Not retail</b>, as <see cref="SquadScore"/>.
/// </summary>
public interface ISessionMissionScore
{
    /// <summary>Gets this end's own player's entity, or <see langword="null"/> for nobody's (a dedicated server's).</summary>
    int? LocalEntity { get; }

    /// <summary>Scores every player and tells them all: the server's to do.</summary>
    /// <param name="sheet">The mission as the host's player played it: the squad's clock, difficulty and requirements.</param>
    /// <returns>The squad's score, or <see langword="null"/> where this end serves no session.</returns>
    SquadScore? AnnounceMissionScore(MissionScoreSheet sheet) => null;

    /// <summary>Takes the score the server last told, once: a client's to do.</summary>
    /// <returns>The squad's score, or <see langword="null"/> when none has come.</returns>
    SquadScore? TakeMissionScore() => null;
}
