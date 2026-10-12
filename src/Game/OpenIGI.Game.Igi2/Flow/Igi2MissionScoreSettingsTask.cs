using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>How a mission was played, <c>HumanMissionScoreData_s</c>: what its rank is reckoned from.</summary>
/// <param name="Ticks">The ticks it took, <c>nTime</c>.</param>
/// <param name="TimesSpotted">The times the player was spotted, <c>nSpotted</c>.</param>
/// <param name="Health">The health left, in per cent, <c>nHealth</c>.</param>
/// <param name="Shots">The rounds fired, <c>nShots</c>.</param>
/// <param name="Hits">The rounds that hit, <c>nHits</c>.</param>
public readonly record struct Igi2MissionScore(uint Ticks, int TimesSpotted, int Health, uint Shots, uint Hits);

/// <summary>
/// What each rank asks of a mission, I.G.I. 2's <c>MissionScoreSettings</c> (missionscoresettings.c): for the ranks Bodyguard, Secret
/// Agent and IGI Operative, the time, times spotted, loads, health and accuracy required. It does nothing as the level runs; the
/// mission's end reads it.
/// </summary>
public sealed partial class Igi2MissionScoreSettingsTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MissionScoreSettings";

    /// <summary>The requirements' places in a rank: time, times spotted, loads, health, accuracy.</summary>
    public const int Time = 0, Spotted = 1, Loads = 2, Health = 3, Accuracy = 4;

    /// <summary>Gets the requirements, by rank (nought Bodyguard to two IGI Operative) then item, <c>atMissionRankRequirement</c>.</summary>
    public float[,] Required { get; } = new float[3, 5];

    /// <summary>Gets or sets the most seconds Bodyguard allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time Required (secs) (Bodyguard)")]
    public float TimeBodyguard { get => Required[0, Time]; set => Required[0, Time] = value; }

    /// <summary>Gets or sets the most seconds Secret Agent allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time Required (secs) (Secret Agent)")]
    public float TimeSecretAgent { get => Required[1, Time]; set => Required[1, Time] = value; }

    /// <summary>Gets or sets the most seconds IGI Operative allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time Required (secs) (IGI Operative)")]
    public float TimeOperative { get => Required[2, Time]; set => Required[2, Time] = value; }

    /// <summary>Gets or sets the most times spotted Bodyguard allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Times Spotted Limit (Bodyguard)")]
    public float SpottedBodyguard { get => Required[0, Spotted]; set => Required[0, Spotted] = value; }

    /// <summary>Gets or sets the most times spotted Secret Agent allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Times Spotted Limit (Secret Agent)")]
    public float SpottedSecretAgent { get => Required[1, Spotted]; set => Required[1, Spotted] = value; }

    /// <summary>Gets or sets the most times spotted IGI Operative allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Times Spotted Limit (IGI Operative)")]
    public float SpottedOperative { get => Required[2, Spotted]; set => Required[2, Spotted] = value; }

    /// <summary>Gets or sets the most loads Bodyguard allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Number of Loads Allowed (Bodyguard)")]
    public float LoadsBodyguard { get => Required[0, Loads]; set => Required[0, Loads] = value; }

    /// <summary>Gets or sets the most loads Secret Agent allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Number of Loads Allowed (Secret Agent)")]
    public float LoadsSecretAgent { get => Required[1, Loads]; set => Required[1, Loads] = value; }

    /// <summary>Gets or sets the most loads IGI Operative allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Number of Loads Allowed (IGI Operative)")]
    public float LoadsOperative { get => Required[2, Loads]; set => Required[2, Loads] = value; }

    /// <summary>Gets or sets the least health, in per cent, Bodyguard allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Health Required (%) (Bodyguard)")]
    public float HealthBodyguard { get => Required[0, Health]; set => Required[0, Health] = value; }

    /// <summary>Gets or sets the least health, in per cent, Secret Agent allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Health Required (%) (Secret Agent)")]
    public float HealthSecretAgent { get => Required[1, Health]; set => Required[1, Health] = value; }

    /// <summary>Gets or sets the least health, in per cent, IGI Operative allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Health Required (%) (IGI Operative)")]
    public float HealthOperative { get => Required[2, Health]; set => Required[2, Health] = value; }

    /// <summary>Gets or sets the least accuracy, in per cent, Bodyguard allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Accuracy Required (%) (Bodyguard)")]
    public float AccuracyBodyguard { get => Required[0, Accuracy]; set => Required[0, Accuracy] = value; }

    /// <summary>Gets or sets the least accuracy, in per cent, Secret Agent allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Accuracy Required (%) (Secret Agent)")]
    public float AccuracySecretAgent { get => Required[1, Accuracy]; set => Required[1, Accuracy] = value; }

    /// <summary>Gets or sets the least accuracy, in per cent, IGI Operative allows or requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Accuracy Required (%) (IGI Operative)")]
    public float AccuracyOperative { get => Required[2, Accuracy]; set => Required[2, Accuracy] = value; }

    /// <summary>The string a rank's name is read from, <c>MissionScoreSettings_GetRankDesc</c>.</summary>
    /// <param name="rank">The rank, -1 for none.</param>
    /// <param name="difficulty">The difficulty, nought to two.</param>
    /// <returns><c>Unranked</c>, or <c>Rank1</c> to <c>Rank9</c>: three a difficulty.</returns>
    public static string RankName(int rank, int difficulty) =>
        rank < 0 ? "Unranked" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Rank{(3 * difficulty) + 1 + rank}");

    /// <summary>
    /// The highest rank a mission earned, -1 for none (<c>MissionScoreSettings_GetRank</c>): from IGI Operative down, the first rank
    /// whose every requirement is met. A requirement is skipped when rank nought's value for it is below nought (retail reads rank
    /// nought's for every rank); the time required is at least the seconds taken, the spotted limit at least the times spotted,
    /// the health required at most the health left, and the accuracy required at most the hits per hundred rounds fired (met with
    /// none fired). Loads are not checked. A mission that took no time earns none.
    /// </summary>
    /// <param name="score">How it was played.</param>
    /// <param name="met">Receives, for the last rank weighed, which of the five items were met.</param>
    /// <returns>The rank.</returns>
    public int GetRank(in Igi2MissionScore score, bool[]? met = null)
    {
        bool[] items = new bool[5];
        if (score.Ticks == 0)
        {
            Report(items, met);
            return -1;
        }

        if (met is not null)
        {
            Array.Fill(met, true);
        }

        for (int rank = 2; rank >= 0; rank--)
        {
            Array.Clear(items);
            bool failed = false;
            for (int item = 0; item < 5; item++)
            {
                if (Required[0, item] < 0f)
                {
                    continue;
                }

                switch (item)
                {
                    case Time:
                        failed |= !Check(ref items[item], (long)Required[rank, Time] >= score.Ticks / 30u);
                        break;
                    case Spotted:
                        failed |= !Check(ref items[item], (long)Required[rank, Spotted] >= score.TimesSpotted);
                        break;
                    case Health:
                        failed |= !Check(ref items[item], (long)Required[rank, Health] <= score.Health);
                        break;
                    case Accuracy:
                        bool accurate = score.Shots == 0 || (long)Required[rank, Accuracy] <= (int)(100u * score.Hits / score.Shots);
                        failed |= !Check(ref items[item], accurate);
                        break;
                }
            }

            if (!failed)
            {
                return rank;
            }

            Report(items, met);
        }

        Report(items, met);
        return -1;
    }

    private static void Report(bool[] items, bool[]? met)
    {
        if (met is not null)
        {
            Array.Copy(items, met, Math.Min(items.Length, met.Length));
        }
    }

    private static bool Check(ref bool slot, bool ok)
    {
        slot = ok;
        return ok;
    }
}
