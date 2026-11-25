using System.Globalization;

namespace OpenIGI.Game.Scoring;

/// <summary>
/// The words a mission's score is told in: I.G.I. 2's own keys (<c>MissionScoreScreen_New</c> looks each up with
/// <c>Config_GetLocalizedStringForLanguage</c>), read from the language where it holds them and otherwise in the English the
/// original ships. Project I.G.I.'s language holds none, so it is told in that English, with I.G.I. 2's ranks.
/// </summary>
/// <param name="lookup">Finds a key's text in the language, or <see langword="null"/> where it holds none; none to use English.</param>
public sealed class MissionScoreWords(Func<string, string?>? lookup = null)
{
    /// <summary>The heading over the score.</summary>
    public const string Title = "Mission Stats";

    /// <summary>The head of the labels' column.</summary>
    public const string Category = "Category";

    /// <summary>The head of the achieved column.</summary>
    public const string Achieved = "Achieved";

    /// <summary>The head of the next rank's column.</summary>
    public const string Target = "Target";

    /// <summary>The time's label.</summary>
    public const string Time = "Time To Complete Mission";

    /// <summary>The times spotted's label.</summary>
    public const string Spotted = "Times Spotted";

    /// <summary>The health's label.</summary>
    public const string Health = "Health";

    /// <summary>The accuracy's label.</summary>
    public const string Accuracy = "Accuracy";

    /// <summary>The rank's label.</summary>
    public const string Rank = "Rank";

    /// <summary>What the accuracy says with no round fired.</summary>
    public const string NotAvailable = "Not Available";

    /// <summary>The name of no rank.</summary>
    public const string Unranked = "Unranked";

    // The English I.G.I. 2 ships for the keys whose text is not the key itself (language/english/menusystem.res).
    private static readonly Dictionary<string, string> English = EnglishWords();

    /// <summary>Gets the words in English.</summary>
    public static MissionScoreWords InEnglish { get; } = new();

    /// <summary>Gets a key's text.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Its text in the language, else in English.</returns>
    public string this[string key] =>
        lookup?.Invoke(key) is { Length: > 0 } text ? text : English.GetValueOrDefault(key, key);

    /// <summary>
    /// The key of a rank's name, <c>MissionScoreSettings_GetRankDesc</c>: <c>Unranked</c>, or <c>Rank1</c> to <c>Rank9</c>, three a
    /// difficulty, lowest first.
    /// </summary>
    /// <param name="rank">The rank, -1 for none.</param>
    /// <param name="difficulty">The difficulty, nought to two.</param>
    /// <returns>The key.</returns>
    public static string RankKey(int rank, int difficulty) =>
        rank < 0
            ? Unranked
            : string.Create(CultureInfo.InvariantCulture, $"Rank{(MissionRankRequirements.RankCount * Math.Clamp(difficulty, 0, 2)) + 1 + rank}");

    // The English words: not available's, and the ranks' (Rank1 to Rank9) as every menu names them.
    private static Dictionary<string, string> EnglishWords()
    {
        Dictionary<string, string> words = new(StringComparer.Ordinal) { [NotAvailable] = "N/A" };
        for (int i = 0; i < OpenIGI.Core.Titles.GameTitle.RankNames.Count; i++)
        {
            words[string.Create(CultureInfo.InvariantCulture, $"Rank{i + 1}")] = OpenIGI.Core.Titles.GameTitle.RankNames[i];
        }

        return words;
    }

    /// <summary>Gets a rank's name.</summary>
    /// <param name="rank">The rank, -1 for none.</param>
    /// <param name="difficulty">The difficulty it was earned at.</param>
    /// <returns>The name.</returns>
    public string RankName(int rank, int difficulty) => this[RankKey(rank, difficulty)];
}
