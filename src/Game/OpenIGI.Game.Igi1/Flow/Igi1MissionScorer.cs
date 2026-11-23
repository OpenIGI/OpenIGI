using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Scoring;

namespace OpenIGI.Game.Igi1.Flow;

/// <summary>
/// How Project I.G.I.'s player is playing its mission, for the mission's rank. <b>Not retail</b>: the figures I.G.I. 2's player
/// keeps (<c>Human.tMissionScoreData</c>), read from what the first game's mission already counts.
/// </summary>
/// <param name="world">The played mission.</param>
/// <param name="requirements">What the mission's ranks ask (<see cref="Igi1MissionRanks"/>), or <see langword="null"/> where it is not ranked.</param>
public sealed class Igi1MissionScorer(MissionWorld world, MissionRankRequirements? requirements)
{
    private readonly MissionWorld _world = world ?? throw new ArgumentNullException(nameof(world));

    /// <summary>Gets the player's score so far beside the mission's requirements.</summary>
    /// <param name="missionId">The mission being played.</param>
    /// <returns>The sheet, or <see langword="null"/> for a mission that is not ranked or a level with no player of its own.</returns>
    /// <remarks>
    /// The time is the ticks the player has run (a save keeps them), the times spotted the squads' first sightings of it, the
    /// health what is left of its maximum in whole per cent, and the shots, hits and kills its guns' rounds and what they did to
    /// the soldiers (<see cref="ShotTally"/>). The difficulty is the one the mission is played at.
    /// </remarks>
    public MissionScoreSheet? Sheet(int missionId)
    {
        if (requirements is null || _world.Player is not { } rig)
        {
            return null;
        }

        MissionScore score = ScoreOf(_world, rig.Shots, _world.PlayerLife.Health) with { MissionId = missionId };
        return new MissionScoreSheet(score, requirements);
    }

    /// <summary>
    /// Gets how one of the mission's players -- its own, or a co-op partner on the server -- has played: the mission's clock and
    /// loads, the times its soldiers spotted the squad (Project I.G.I.'s soldiers fight every player as one target, so a sighting is
    /// the squad's), the player's health left in whole per cent, and its guns' rounds and what they did to the soldiers.
    /// </summary>
    /// <param name="world">The played mission.</param>
    /// <param name="shots">The player's tally.</param>
    /// <param name="health">The player's health.</param>
    /// <returns>The score, its mission left for the caller to say.</returns>
    internal static MissionScore ScoreOf(MissionWorld world, ShotTally shots, OpenIGI.Game.Player.HumanHealth health)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(shots);
        ArgumentNullException.ThrowIfNull(health);
        return new MissionScore(
            (uint)Math.Max(0L, Math.Min(world.PlayerTicks, uint.MaxValue)),
            world.PlayerTask?.TimesSpotted ?? 0,
            (int)MathF.Floor(Math.Clamp(health.RemainingFraction, 0f, 1f) * 100f),
            (uint)shots.RoundsFired,
            (uint)shots.SoldierHits)
        {
            Difficulty = world.PlayerDifficulty,
            GameLoads = world.GameLoads,
            Kills = shots.SoldierKills,
        };
    }
}
