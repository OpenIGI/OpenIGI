namespace OpenIGI.Game.Scoring;

/// <summary>The figures a rank asks of a mission, by their places in a rank's requirements.</summary>
public static class MissionRankItem
{
    /// <summary>The most seconds the mission may take.</summary>
    public const int Time = 0;

    /// <summary>The most times the player may be spotted.</summary>
    public const int Spotted = 1;

    /// <summary>The most save games that may be loaded: kept, but never weighed.</summary>
    public const int Loads = 2;

    /// <summary>The least health, in per cent, the player must end with.</summary>
    public const int Health = 3;

    /// <summary>The least accuracy, in per cent, the player's rounds must have.</summary>
    public const int Accuracy = 4;

    /// <summary>How many figures a rank asks.</summary>
    public const int Count = 5;
}

/// <summary>
/// What each of a mission's ranks asks, I.G.I. 2's <c>atMissionRankRequirement</c> (<c>missionscoresettings.c</c>): for the three
/// ranks, lowest first, the figures of <see cref="MissionRankItem"/>; and how a score is weighed against them.
/// </summary>
public sealed class MissionRankRequirements
{
    /// <summary>The ranks a mission can earn.</summary>
    public const int RankCount = 3;

    private readonly float[,] _required = new float[RankCount, MissionRankItem.Count];

    /// <summary>Gets or sets a figure a rank asks.</summary>
    /// <param name="rank">The rank, nought the lowest.</param>
    /// <param name="item">The figure, one of <see cref="MissionRankItem"/>.</param>
    /// <returns>The figure.</returns>
    public float this[int rank, int item]
    {
        get => _required[rank, item];
        set => _required[rank, item] = value;
    }

    /// <summary>Builds requirements from rows of figures, lowest rank first.</summary>
    /// <param name="rows">A row of <see cref="MissionRankItem.Count"/> figures for each rank.</param>
    /// <returns>The requirements.</returns>
    public static MissionRankRequirements FromRows(params float[][] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Length != RankCount || rows.Any(static row => row.Length != MissionRankItem.Count))
        {
            throw new ArgumentException($"Requirements take {RankCount} rows of {MissionRankItem.Count} figures.", nameof(rows));
        }

        var requirements = new MissionRankRequirements();
        for (int rank = 0; rank < RankCount; rank++)
        {
            for (int item = 0; item < MissionRankItem.Count; item++)
            {
                requirements[rank, item] = rows[rank][item];
            }
        }

        return requirements;
    }

    /// <summary>Gets whether a figure is weighed: retail leaves out one whose lowest rank's requirement is below nought.</summary>
    /// <param name="item">The figure.</param>
    /// <returns>Whether it is weighed.</returns>
    public bool Weighs(int item) => _required[0, item] >= 0f;

    /// <summary>
    /// The highest rank a mission earned, -1 for none (<c>MissionScoreSettings_GetRank</c>): from the top down, the first rank
    /// whose every weighed requirement is met. The time required is at least the seconds taken, the spotted limit at least the
    /// times spotted, the health required at most the health left, and the accuracy required at most the hits per hundred
    /// rounds fired (met with none fired). Loads are not checked. A mission that took no time earns none.
    /// </summary>
    /// <param name="score">How it was played.</param>
    /// <param name="met">Receives, for the rank the score is weighed against -- the one above the rank earned, or the top one
    /// once it is earned -- which of the figures were met.</param>
    /// <returns>The rank.</returns>
    public int GetRank(in MissionScore score, bool[]? met = null)
    {
        bool[] items = new bool[MissionRankItem.Count];
        if (score.Ticks == 0)
        {
            Report(items, met);
            return -1;
        }

        if (met is not null)
        {
            Array.Fill(met, true);
        }

        for (int rank = RankCount - 1; rank >= 0; rank--)
        {
            Array.Clear(items);
            bool failed = false;
            for (int item = 0; item < MissionRankItem.Count; item++)
            {
                if (!Weighs(item) || item == MissionRankItem.Loads)
                {
                    continue;
                }

                items[item] = Meets(rank, item, score);
                failed |= !items[item];
            }

            if (!failed)
            {
                return rank;
            }

            Report(items, met);
        }

        return -1;
    }

    /// <summary>
    /// Whether a score is to be kept in place of the one kept, <c>MissionScoreSettings_UpdateMissionStats</c>: always where none
    /// is kept; else when it was played at a difficulty no lower, took some time, and earns a rank no lower by these requirements
    /// than the kept one does.
    /// </summary>
    /// <param name="score">The score just played.</param>
    /// <param name="kept">The score kept, or <see langword="null"/> for none.</param>
    /// <returns>Whether to keep it.</returns>
    public bool Improves(in MissionScore score, MissionScore? kept) =>
        kept is not { } best || (score.Difficulty >= best.Difficulty && score.Ticks != 0 && GetRank(score) >= GetRank(best));

    // One figure against one rank, as retail compares them: the requirement truncated to a whole number.
    private bool Meets(int rank, int item, in MissionScore score)
    {
        long required = (long)_required[rank, item];
        return item switch
        {
            MissionRankItem.Time => required >= score.Ticks / (uint)MissionScore.TicksPerSecond,
            MissionRankItem.Spotted => required >= score.TimesSpotted,
            MissionRankItem.Health => required <= score.Health,
            _ => score.Shots == 0 || required <= (int)(100u * score.Hits / score.Shots),
        };
    }

    private static void Report(bool[] items, bool[]? met)
    {
        if (met is not null)
        {
            Array.Copy(items, met, Math.Min(items.Length, met.Length));
        }
    }
}
